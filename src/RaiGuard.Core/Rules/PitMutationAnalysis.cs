using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RaiGuard.Core.Diagnostics;

namespace RaiGuard.Core.Rules;

/// <summary>
/// CR077: bounded, chronological provenance analysis. Unknown values do not establish
/// violations. Names never establish JsonPit types. Branches retain only agreed facts.
/// The compiler and rail both supply a SemanticModel to this engine.
/// </summary>
internal sealed class PitMutationAnalysis
{
	private sealed class Value
	{
		internal readonly string Kind;
		internal readonly string? Owner;
		internal readonly string? Key;
		internal readonly Value? Element;
		internal readonly ExpressionSyntax? Payload;
		internal Value(string kind = "unknown", string? owner = null, string? key = null, Value? element = null, ExpressionSyntax? payload = null)
		{ Kind = kind; Owner = owner; Key = key; Element = element; Payload = payload; }
	}
	private static readonly Value Unknown = new();
	private readonly SemanticModel model;
	private readonly Func<string, bool> matches;
	private readonly Action<DiagnosticDescriptor, Location, object[]> emit;
	private readonly HashSet<string> emitted = new();
	private HashSet<string> retired = new();
	private int depth;

	internal PitMutationAnalysis(SemanticModel model, Func<string, bool> matches, Action<DiagnosticDescriptor, Location, object[]> emit)
	{ this.model = model; this.matches = matches; this.emit = emit; }

	internal void Analyze(SyntaxNode root)
	{
		foreach (var body in root.DescendantNodes().OfType<BlockSyntax>().Where(b => b.Parent is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax))
		{
			retired = new HashSet<string>();
			Block(body.Statements, NewState());
		}
		foreach (var arrow in root.DescendantNodes().OfType<ArrowExpressionClauseSyntax>())
		{ retired = new HashSet<string>(); Evaluate(arrow.Expression, NewState()); }
	}

	private static Dictionary<ISymbol, Value> NewState() => new(SymbolEqualityComparer.Default);
	private static Dictionary<ISymbol, Value> Copy(Dictionary<ISymbol, Value> state) => new(state, SymbolEqualityComparer.Default);
	private static bool IsType(ITypeSymbol? type, string name)
	{
		for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
			if (current.ToDisplayString() == name) return true;
		return false;
	}
	private ITypeSymbol? Type(ExpressionSyntax expression) => model.GetTypeInfo(expression).Type;
	private ISymbol? Symbol(SyntaxNode node) => node is VariableDeclaratorSyntax or SingleVariableDesignationSyntax
		? model.GetDeclaredSymbol(node) : model.GetSymbolInfo(node).Symbol;
	private string? Text(ExpressionSyntax expression)
	{
		var value = model.GetConstantValue(expression);
		return value.HasValue ? value.Value as string : null;
	}
	private string? Key(ExpressionSyntax? expression)
	{
		if (expression == null) return null;
		var text = Text(expression);
		if (text != null) return "literal:" + text;
		var symbol = Symbol(expression);
		return symbol == null ? null : "symbol:" + symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ":" + symbol.Locations.FirstOrDefault()?.SourceSpan.Start;
	}
	private string Identity(SyntaxNode expression) => model.SyntaxTree.FilePath + ":" + expression.SpanStart;
	private static bool Equivalent(Value a, Value b) => a.Kind == b.Kind && a.Owner == b.Owner && a.Key == b.Key && a.Payload == b.Payload &&
		(a.Element == null && b.Element == null || a.Element != null && b.Element != null && Equivalent(a.Element, b.Element));
	private static Value Join(IEnumerable<Value> sequence)
	{
		var values = sequence.ToList();
		return values.Count != 0 && values.All(v => Equivalent(v, values[0])) ? values[0] : Unknown;
	}
	private void Report(string rule, SyntaxNode location, string detail)
	{
		if (!matches(rule) || !emitted.Add(rule + ":" + location.SpanStart)) return;
		emit(rule == "RAI011" ? DiagnosticDescriptors.ProhibitPitReadModifyWrite : DiagnosticDescriptors.ProhibitProtectedPitItemMutation,
			location.GetLocation(), new object[] { detail });
	}
	private static bool Protected(string? key) => key != null && new[] { "Id", "Modified", "Deleted" }.Contains(key, StringComparer.OrdinalIgnoreCase);
	private static bool Item(Value value) => value.Kind is "live" or "detached" or "fresh" or "item";

	private Value Evaluate(ExpressionSyntax? expression, Dictionary<ISymbol, Value> state)
	{
		if (expression == null) return Unknown;
		if (expression is ParenthesizedExpressionSyntax paren) return Evaluate(paren.Expression, state);
		if (expression is CastExpressionSyntax cast) return Evaluate(cast.Expression, state);
		if (expression is PostfixUnaryExpressionSyntax postfix && postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression)) return Evaluate(postfix.Operand, state);
		if (expression is AwaitExpressionSyntax awaited) return Evaluate(awaited.Expression, state);
		if (expression is IsPatternExpressionSyntax pattern)
		{
			var source = Evaluate(pattern.Expression, state);
			foreach (var designation in pattern.Pattern.DescendantNodesAndSelf().OfType<SingleVariableDesignationSyntax>())
				if (Symbol(designation) is ISymbol local) state[local] = source;
			return Unknown;
		}
		if (expression is AssignmentExpressionSyntax assignment)
		{
			var value = Evaluate(assignment.Right, state);
			if (assignment.Left is ElementAccessExpressionSyntax index)
			{
				var target = Evaluate(index.Expression, state);
				var field = index.ArgumentList.Arguments.FirstOrDefault()?.Expression;
				if (Item(target) && field != null && Protected(Text(field))) Report("RAI012", field, Text(field)!);
			}
			else if (assignment.Left is MemberAccessExpressionSyntax member && Item(Evaluate(member.Expression, state)) && Protected(member.Name.Identifier.Text))
				Report("RAI012", member.Name, member.Name.Identifier.Text);
			var symbol = Symbol(assignment.Left);
			if (symbol != null) state[symbol] = value;
			return value;
		}
		if (expression is InvocationExpressionSyntax invocation) return Invoke(invocation, state);
		if (expression is ElementAccessExpressionSyntax element)
		{
			var receiver = Evaluate(element.Expression, state);
			if (receiver.Kind == "pit") return new Value("live", receiver.Owner, Key(element.ArgumentList.Arguments.FirstOrDefault()?.Expression));
			if (receiver.Kind == "collection") return receiver.Element ?? Unknown;
		}
		if (expression is ConditionalExpressionSyntax conditional)
		{
			Evaluate(conditional.Condition, state);
			return Join(new[] { Evaluate(conditional.WhenTrue, Copy(state)), Evaluate(conditional.WhenFalse, Copy(state)) });
		}
		if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.CoalesceExpression))
		{
			var left = Evaluate(binary.Left, state);
			return binary.Right is ThrowExpressionSyntax ? left : Join(new[] { left, Evaluate(binary.Right, state) });
		}
		if (expression is BaseObjectCreationExpressionSyntax creation)
		{
			var type = Type(expression);
			var arguments = creation.ArgumentList?.Arguments.Select(a => a.Expression).ToList() ?? new List<ExpressionSyntax>();
			if (IsType(type, "JsonPit.Pit")) return new Value("pit", "allocation:" + Identity(expression));
			if (IsType(type, "JsonPit.PitItem"))
			{
				var first = arguments.FirstOrDefault();
				var origin = Evaluate(first, state);
				if (origin.Kind is "live" or "detached") return new Value("detached", origin.Owner, origin.Key);
				if (origin.Payload != null) return new Value(PayloadKeys(origin.Payload).Any(p => p.Key.Equals("Modified", StringComparison.OrdinalIgnoreCase) || p.Key.Equals("Deleted", StringComparison.OrdinalIgnoreCase)) ? "detached" : "fresh");
				if (first != null && Type(first)?.SpecialType == SpecialType.System_String) return new Value("fresh", key: Key(first));
				if (first is ObjectCreationExpressionSyntax payload && payload.Initializer != null)
					return new Value(PayloadKeys(payload).Any(p => p.Key.Equals("Modified", StringComparison.OrdinalIgnoreCase) || p.Key.Equals("Deleted", StringComparison.OrdinalIgnoreCase)) ? "detached" : "fresh");
				return new Value("item");
			}
			if (IsType(type, "Newtonsoft.Json.Linq.JObject")) return new Value("payload", payload: expression);
			if (type is INamedTypeSymbol named && named.AllInterfaces.Any(i => i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>"))
				return new Value("collection", element: creation.Initializer == null || creation.Initializer.Expressions.Count == 0 ? null : Join(creation.Initializer.Expressions.Select(e => Evaluate(e, state))));
			foreach (var arg in arguments) Evaluate(arg, state);
			if (creation.Initializer != null) foreach (var expr in creation.Initializer.Expressions) Evaluate(expr, state);
		}
		if (expression is ArrayCreationExpressionSyntax array && array.Initializer != null)
			return new Value("collection", element: Join(array.Initializer.Expressions.Select(e => Evaluate(e, state))));
		if (expression is ImplicitArrayCreationExpressionSyntax implicitArray)
			return new Value("collection", element: Join(implicitArray.Initializer.Expressions.Select(e => Evaluate(e, state))));
		var symbolValue = Symbol(expression);
		if (symbolValue != null && state.TryGetValue(symbolValue, out var known)) return known;
		if (IsType(Type(expression), "JsonPit.Pit")) return new Value("pit", Key(expression) ?? "unknown-owner");
		if (IsType(Type(expression), "JsonPit.PitItem")) return new Value("item");
		return Unknown;
	}

	private IEnumerable<(string Key, ExpressionSyntax Value, SyntaxNode Location)> PayloadKeys(ExpressionSyntax expression)
	{
		var initializer = (expression as BaseObjectCreationExpressionSyntax)?.Initializer;
		if (initializer == null) yield break;
		foreach (var entry in initializer.Expressions)
		{
			if (entry is AssignmentExpressionSyntax assignment && assignment.Left is ImplicitElementAccessSyntax index && index.ArgumentList.Arguments.Count == 1)
			{
				var key = Text(index.ArgumentList.Arguments[0].Expression);
				if (key != null) yield return (key, assignment.Right, assignment);
			}
			else if (entry is InitializerExpressionSyntax pair && pair.Expressions.Count == 2)
			{
				var key = Text(pair.Expressions[0]);
				if (key != null) yield return (key, pair.Expressions[1], pair);
			}
		}
	}

	private Value Invoke(InvocationExpressionSyntax invocation, Dictionary<ISymbol, Value> state)
	{
		var method = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
		var access = invocation.Expression as MemberAccessExpressionSyntax;
		var receiver = access == null ? Unknown : Evaluate(access.Expression, state);
		var arguments = invocation.ArgumentList.Arguments.Select(a => Evaluate(a.Expression, state)).ToArray();
		if (method == null) return Unknown; // unresolved overloads do not establish a violation
		if (IsType(method.ContainingType, "JsonPit.Pit") && receiver.Kind == "pit")
		{
			if (method.Name is "Get" or "GetAt") return new Value(method.Name == "GetAt" ? "detached" : "live", receiver.Owner, Key(invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression));
			if (method.Name == "Add" && arguments.Length == 1)
			{
				var value = arguments[0];
				if (value.Kind == "detached") Report("RAI011", invocation, "a detached full projection");
				else if (value.Kind == "live")
				{
					if (retired.Contains(value.Owner + "|" + value.Key) || retired.Contains(value.Owner + "|*")) Report("RAI011", invocation, "a retired live handle");
					else if (value.Owner != receiver.Owner && (value.Owner?.StartsWith("allocation:", StringComparison.Ordinal) ?? false) && (receiver.Owner?.StartsWith("allocation:", StringComparison.Ordinal) ?? false))
						Report("RAI011", invocation, "a live handle owned by a different Pit");
				}
				else if (value.Kind == "fresh") ReplaceAliases(state, value, new Value("live", receiver.Owner, value.Key));
			}
			if (method.Name == "Delete" && invocation.ArgumentList.Arguments.Count > 0)
			{
				var key = Key(invocation.ArgumentList.Arguments[0].Expression);
				if (key != null) retired.Add(receiver.Owner + "|" + key);
			}
			if (method.Name == "Dispose") retired.Add(receiver.Owner + "|*");
		}
		else if (Item(receiver))
		{
			if (method.Name == "DeepClone") return new Value("detached", receiver.Owner, receiver.Key);
			if (method.Name is "ExtendWith" or "SetProperty" or "Merge" && invocation.ArgumentList.Arguments.Count > 0)
			{
				foreach (var property in PayloadKeys(arguments[0].Payload ?? invocation.ArgumentList.Arguments[0].Expression))
					if (property.Key.Equals("Modified", StringComparison.OrdinalIgnoreCase) || property.Key.Equals("Deleted", StringComparison.OrdinalIgnoreCase) ||
						property.Key.Equals("Id", StringComparison.OrdinalIgnoreCase) && receiver.Key?.StartsWith("literal:", StringComparison.Ordinal) == true && Text(property.Value) != null && receiver.Key != Key(property.Value))
						Report("RAI012", property.Location, property.Key);
			}
			if (method.Name is "DeleteProperty" or "DeletePropertyPath" or "Remove" && invocation.ArgumentList.Arguments.Count > 0)
			{
				var field = invocation.ArgumentList.Arguments[0].Expression;
				var key = Text(field)?.Split('.')[0];
				if (Protected(key)) Report("RAI012", field, key!);
			}
		}
		else if (receiver.Kind == "collection" && method.Name == "Add" && arguments.Length == 1)
			ReplaceAliases(state, receiver, new Value("collection", element: receiver.Element == null ? arguments[0] : Join(new[] { receiver.Element, arguments[0] })));

		// Bounded source-helper summaries. Parameter substitution preserves the caller's
		// provenance without assuming all PitItem parameters are loaded projections.
		if (depth < 4 && method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is SyntaxNode declaration && declaration.SyntaxTree == model.SyntaxTree)
		{
			var helperBody = (declaration as MethodDeclarationSyntax)?.Body ?? (declaration as LocalFunctionStatementSyntax)?.Body;
			var arrow = (declaration as MethodDeclarationSyntax)?.ExpressionBody ?? (declaration as LocalFunctionStatementSyntax)?.ExpressionBody;
			if (helperBody != null || arrow != null)
			{
				var local = Copy(state);
				for (int i = 0; i < Math.Min(arguments.Length, method.Parameters.Length); i++) local[method.Parameters[i]] = arguments[i];
				depth++; var before = new HashSet<string>(retired);
				try
				{
					Value result;
					if (arrow != null) result = Evaluate(arrow.Expression, local);
					else { var returns = new List<Value>(); Block(helperBody!.Statements, local, returns); result = Join(returns); }
					for (int i = 0; i < Math.Min(arguments.Length, method.Parameters.Length); i++)
					{
						var parameter = method.Parameters[i];
						bool reassigned = declaration.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(a => SymbolEqualityComparer.Default.Equals(Symbol(a.Left), parameter));
						if (!reassigned && local.TryGetValue(parameter, out var after) && (arguments[i].Kind == "collection" || arguments[i].Kind == "fresh" && after.Kind == "live"))
							ReplaceAliases(state, arguments[i], after);
					}
					return result;
				}
				finally { depth--; retired = before; }
			}
		}
		return Unknown;
	}
	private static void ReplaceAliases(Dictionary<ISymbol, Value> state, Value oldValue, Value newValue)
	{
		foreach (var key in state.Where(p => ReferenceEquals(p.Value, oldValue)).Select(p => p.Key).ToList()) state[key] = newValue;
	}
	private void Merge(Dictionary<ISymbol, Value> state, params Dictionary<ISymbol, Value>[] branches)
	{
		foreach (var key in branches.SelectMany(b => b.Keys).Distinct(SymbolEqualityComparer.Default))
			state[key] = Join(branches.Select(b => b.TryGetValue(key, out var value) ? value : Unknown));
	}
	private void Loop(StatementSyntax body, Dictionary<ISymbol, Value> state, List<Value>? returns)
	{
		var before = Copy(state); var iteration = Copy(state); var beforeRetired = new HashSet<string>(retired);
		Statement(body, iteration, returns); retired = beforeRetired; Merge(state, before, iteration);
	}
	private bool Block(IEnumerable<StatementSyntax> statements, Dictionary<ISymbol, Value> state, List<Value>? returns = null)
	{
		foreach (var statement in statements) if (Statement(statement, state, returns)) return true;
		return false;
	}
	private bool Statement(StatementSyntax statement, Dictionary<ISymbol, Value> state, List<Value>? returns)
	{
		switch (statement)
		{
			case BlockSyntax block: return Block(block.Statements, state, returns);
			case LocalDeclarationStatementSyntax local:
				foreach (var variable in local.Declaration.Variables)
					if (model.GetDeclaredSymbol(variable) is ISymbol symbol) state[symbol] = Evaluate(variable.Initializer?.Value, state);
				break;
			case ExpressionStatementSyntax expression: Evaluate(expression.Expression, state); break;
			case ReturnStatementSyntax ret: returns?.Add(Evaluate(ret.Expression, state)); return true;
			case ThrowStatementSyntax thrown: Evaluate(thrown.Expression, state); return true;
			case IfStatementSyntax conditional:
				Evaluate(conditional.Condition, state);
				var yes = Copy(state); var no = Copy(state); var before = new HashSet<string>(retired);
				var yesStopped = Statement(conditional.Statement, yes, returns); var yesRetired = new HashSet<string>(retired); retired = new HashSet<string>(before);
				var noStopped = conditional.Else != null && Statement(conditional.Else.Statement, no, returns); var noRetired = new HashSet<string>(retired);
				yesRetired.IntersectWith(noRetired); retired = yesRetired;
				if (yesStopped && noStopped) return true;
				Merge(state, yesStopped ? no : yes, noStopped ? yes : no); break;
			case ForEachStatementSyntax loop:
				var elements = Evaluate(loop.Expression, state); var iteration = Copy(state);
				if (model.GetDeclaredSymbol(loop) is ISymbol loopSymbol) iteration[loopSymbol] = elements.Element ?? Unknown;
				var retiredBeforeLoop = new HashSet<string>(retired);
				Statement(loop.Statement, iteration, returns); retired = retiredBeforeLoop; Merge(state, Copy(state), iteration); break;
			case WhileStatementSyntax whileLoop:
				Evaluate(whileLoop.Condition, state);
				Loop(whileLoop.Statement, state, returns); break;
			case DoStatementSyntax doLoop:
				Statement(doLoop.Statement, state, returns); Evaluate(doLoop.Condition, state); break;
			case ForStatementSyntax forLoop:
				if (forLoop.Declaration != null) foreach (var variable in forLoop.Declaration.Variables)
					if (model.GetDeclaredSymbol(variable) is ISymbol variableSymbol) state[variableSymbol] = Evaluate(variable.Initializer?.Value, state);
				foreach (var initializer in forLoop.Initializers) Evaluate(initializer, state);
				Evaluate(forLoop.Condition, state); Loop(forLoop.Statement, state, returns);
				foreach (var incrementor in forLoop.Incrementors) Evaluate(incrementor, state); break;
			case SwitchStatementSyntax selection:
				Evaluate(selection.Expression, state);
				var alternatives = new List<Dictionary<ISymbol, Value>> { Copy(state) };
				var retiredBeforeSwitch = new HashSet<string>(retired);
				foreach (var section in selection.Sections)
				{ var alternative = Copy(state); retired = new HashSet<string>(retiredBeforeSwitch); Block(section.Statements, alternative, returns); alternatives.Add(alternative); }
				retired = retiredBeforeSwitch; Merge(state, alternatives.ToArray()); break;
			case UsingStatementSyntax use:
				if (use.Declaration != null) foreach (var variable in use.Declaration.Variables)
					if (model.GetDeclaredSymbol(variable) is ISymbol symbol) state[symbol] = Evaluate(variable.Initializer?.Value, state);
				Evaluate(use.Expression, state); return Statement(use.Statement, state, returns);
			case TryStatementSyntax attempt:
				var incoming = Copy(state); var branches = new List<Dictionary<ISymbol, Value>>(); var retiredBeforeTry = new HashSet<string>(retired);
				var body = Copy(incoming); Block(attempt.Block.Statements, body, returns); branches.Add(body);
				foreach (var handler in attempt.Catches) { var caught = Copy(incoming); retired = new HashSet<string>(retiredBeforeTry); Block(handler.Block.Statements, caught, returns); branches.Add(caught); }
				retired = retiredBeforeTry; Merge(state, branches.ToArray());
				if (attempt.Finally != null) Block(attempt.Finally.Block.Statements, state, returns); break;
			case LockStatementSyntax locked: return Statement(locked.Statement, state, returns);
			case LocalFunctionStatementSyntax _: break;
			default:
				// Unsupported control flow invalidates potentially reassigned facts, rather
				// than pretending its body was a straight-line, definitely executed sequence.
				foreach (var assignment in statement.DescendantNodes().OfType<AssignmentExpressionSyntax>())
					if (Symbol(assignment.Left) is ISymbol assigned) state[assigned] = Unknown;
				break;
		}
		return false;
	}
}
