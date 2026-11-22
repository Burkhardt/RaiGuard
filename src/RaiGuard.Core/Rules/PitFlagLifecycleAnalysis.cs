using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RaiGuard.Core.Diagnostics;

namespace RaiGuard.Core.Rules;

/// <summary>
/// Semantic analysis of the JsonPit flag/ticket lifecycle (RAI018, RAI019, RAI020).
/// Symbols are resolved through the SemanticModel; names alone never establish JsonPit types.
/// Unresolved symbols establish nothing, so incomplete inspection compilations stay silent.
/// RAI018: only an actual write claim may write a ProcessFlagFile (fail-closed allowlist of members).
/// RAI019: the Master ticket may only change inside claim/return, and every public entry point
///   that can claim the ticket must be able to reach ReturnClaim.
/// RAI020: change-file cleanup must not be suppressed by a parameter, option or grace override.
/// </summary>
internal sealed class PitFlagLifecycleAnalysis
{
	private const string PitBase = "JsonPit.JsonPitBase";
	private const string ProcessFlag = "JsonPit.ProcessFlagFile";
	private const string MasterFlag = "JsonPit.MasterFlagFile";

	/// <summary>Members of a JsonPitBase-derived type that represent genuine write access and may claim the ticket.</summary>
	private static readonly HashSet<string> WritePathMembers = new(StringComparer.Ordinal)
	{
		"BeginSave", "Save", "SaveAsync", "Store", "StoreVerified", "Maintain", "ApplyMaintenance", "MergeChanges"
	};

	/// <summary>Members of JsonPitBase that implement the claim/return protocol.</summary>
	private static readonly HashSet<string> ClaimProtocolMembers = new(StringComparer.Ordinal)
	{
		"TryAcquireMaster", "ClaimAvailableTicket", "ReturnClaim"
	};

	private static readonly string[] SuppressTrueNames =
		{ "keepchangefiles", "skipcleanup", "nocleanup", "skipchangefilecleanup", "retainchangefiles", "preservechangefiles", "disablecleanup", "keepfiles" };
	private static readonly string[] SuppressFalseNames =
		{ "cleanup", "cleanchangefiles", "cleanupchangefiles", "deletechangefiles", "removechangefiles", "purgechangefiles", "cleanafter" };

	private readonly SemanticModel model;
	private readonly Func<string, bool> matches;
	private readonly Action<DiagnosticDescriptor, Location, object[]> emit;
	private readonly Dictionary<(IMethodSymbol, string), bool> reach = new(new ReachComparer());

	internal PitFlagLifecycleAnalysis(SemanticModel model, Func<string, bool> matches, Action<DiagnosticDescriptor, Location, object[]> emit)
	{ this.model = model; this.matches = matches; this.emit = emit; }

	private sealed class ReachComparer : IEqualityComparer<(IMethodSymbol, string)>
	{
		public bool Equals((IMethodSymbol, string) x, (IMethodSymbol, string) y) =>
			SymbolEqualityComparer.Default.Equals(x.Item1, y.Item1) && x.Item2 == y.Item2;
		public int GetHashCode((IMethodSymbol, string) obj) => SymbolEqualityComparer.Default.GetHashCode(obj.Item1) ^ obj.Item2.GetHashCode();
	}

	internal void Analyze(SyntaxNode root)
	{
		var wants18 = matches("RAI018");
		var wants19 = matches("RAI019");
		var wants20 = matches("RAI020");
		if (!wants18 && !wants19 && !wants20) return;
		foreach (var node in root.DescendantNodes())
		{
			switch (node)
			{
				case InvocationExpressionSyntax invocation:
					if (model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method)
					{
						if (wants18) CheckProcessFlagWrite(invocation, method);
						if (wants19) CheckTicketMutation(invocation, method);
						if (wants20) CheckCleanupArguments(invocation.ArgumentList, method);
					}
					break;
				case ObjectCreationExpressionSyntax creation:
					if (wants20 && creation.ArgumentList != null && model.GetSymbolInfo(creation).Symbol is IMethodSymbol ctor)
						CheckCleanupArguments(creation.ArgumentList, ctor);
					if (wants19) CheckTicketConstructor(creation);
					break;
				case AssignmentExpressionSyntax assignment when wants20:
					CheckCleanupAssignment(assignment);
					break;
				case MethodDeclarationSyntax declaration when wants19:
					CheckEntryPointRelease(declaration);
					break;
			}
		}
	}

	// ---- RAI018 -------------------------------------------------------------------------------

	private void CheckProcessFlagWrite(InvocationExpressionSyntax invocation, IMethodSymbol method)
	{
		var original = method.OriginalDefinition;
		var owner = Owner(invocation);
		if (original.Name == "Update" && IsType(original.ContainingType, ProcessFlag))
		{
			if (owner?.Type is { } type && (IsType(type, ProcessFlag) || IsType(type, MasterFlag))) return;
			if (owner != null && owner.Name == "ClaimAvailableTicket" && IsType(owner.Type, PitBase)) return;
			emit(DiagnosticDescriptors.ProhibitProcessFlagWriteOutsideWriteClaim, invocation.GetLocation(),
				new object[] { $"'{invocation.Expression}' writes a ProcessFlagFile outside an actual write claim" });
		}
		else if (original.Name == "TryAcquireMaster" && IsType(original.ContainingType, PitBase))
		{
			if (owner?.Type is { } type && IsType(type, PitBase) &&
				(WritePathMembers.Contains(owner.Name) || ClaimProtocolMembers.Contains(owner.Name))) return;
			emit(DiagnosticDescriptors.ProhibitProcessFlagWriteOutsideWriteClaim, invocation.GetLocation(),
				new object[] { $"'TryAcquireMaster' (which writes the ProcessFlagFile) is reached from '{owner?.Name ?? "unknown"}', which is not a write path (Save/Store/Maintain/MergeChanges); reads must not claim the master ticket" });
		}
	}

	// ---- RAI019 -------------------------------------------------------------------------------

	private void CheckTicketMutation(InvocationExpressionSyntax invocation, IMethodSymbol method)
	{
		var original = method.OriginalDefinition;
		if (!(original.Name is "WriteTicket" or "Update") || !IsType(original.ContainingType, MasterFlag)) return;
		if (IsType(original.ContainingType, ProcessFlag)) return; // process flags are RAI018's concern
		var owner = Owner(invocation);
		if (owner?.Type is { } type && (IsType(type, MasterFlag) || IsType(type, ProcessFlag))) return;
		if (owner != null && IsType(owner.Type, PitBase) && ClaimProtocolMembers.Contains(owner.Name)) return;
		emit(DiagnosticDescriptors.EnforceMasterTicketRelease, invocation.GetLocation(),
			new object[] { $"'{invocation.Expression}' changes the Master ticket outside TryAcquireMaster/ReturnClaim; the ticket must only be claimed and returned through the claim protocol" });
	}

	private void CheckTicketConstructor(ObjectCreationExpressionSyntax creation)
	{
		if (model.GetSymbolInfo(creation).Symbol is not IMethodSymbol ctor) return;
		if (!IsType(ctor.ContainingType, MasterFlag) || IsType(ctor.ContainingType, ProcessFlag)) return;
		// new MasterFlagFile(dir, name, server) with a non-empty server writes the ticket as a side effect.
		if (creation.ArgumentList == null || creation.ArgumentList.Arguments.Count < 3) return;
		var server = creation.ArgumentList.Arguments[2].Expression;
		var constant = model.GetConstantValue(server);
		if (constant.HasValue && constant.Value is null) return;
		var owner = Owner(creation);
		if (owner?.Type is { } type && (IsType(type, MasterFlag) || (IsType(type, PitBase) && ClaimProtocolMembers.Contains(owner.Name)))) return;
		emit(DiagnosticDescriptors.EnforceMasterTicketRelease, creation.GetLocation(),
			new object[] { "'new MasterFlagFile(..., server)' writes the Master ticket as a side effect; use TryAcquireMaster/ReturnClaim instead" });
	}

	private void CheckEntryPointRelease(MethodDeclarationSyntax declaration)
	{
		if (model.GetDeclaredSymbol(declaration) is not IMethodSymbol method) return;
		if (method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal)) return;
		if (!IsType(method.ContainingType, PitBase)) return;
		if (ClaimProtocolMembers.Contains(method.Name)) return;
		if (!Reaches(method, "TryAcquireMaster", 8)) return;
		if (Reaches(method, "ReturnClaim", 8)) return;
		emit(DiagnosticDescriptors.EnforceMasterTicketRelease, declaration.Identifier.GetLocation(),
			new object[] { $"'{method.Name}' can claim the Master ticket (TryAcquireMaster) but no path reaches ReturnClaim; the ticket would stay held after Save/Store" });
	}

	private bool Reaches(IMethodSymbol start, string targetName, int depth)
	{
		if (depth < 0) return false;
		var key = (start.OriginalDefinition, targetName + ":" + depth);
		if (reach.TryGetValue(key, out var known)) return known;
		reach[key] = false; // cycle guard
		var found = false;
		foreach (var reference in start.OriginalDefinition.DeclaringSyntaxReferences)
		{
			var syntax = reference.GetSyntax();
			var tree = syntax.SyntaxTree;
			SemanticModel local;
			try { local = model.Compilation.GetSemanticModel(tree); }
			catch (ArgumentException) { continue; }
			foreach (var call in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
			{
				if (local.GetSymbolInfo(call).Symbol is not IMethodSymbol callee) continue;
				var original = callee.OriginalDefinition;
				if (original.Name == targetName && IsType(original.ContainingType, PitBase)) { found = true; break; }
				if (SymbolEqualityComparer.Default.Equals(original.ContainingAssembly, model.Compilation.Assembly) &&
					IsType(original.ContainingType, PitBase) && Reaches(original, targetName, depth - 1)) { found = true; break; }
			}
			if (found) break;
		}
		reach[key] = found;
		return found;
	}

	// ---- RAI020 -------------------------------------------------------------------------------

	private void CheckCleanupArguments(ArgumentListSyntax list, IMethodSymbol method)
	{
		if (!IsJsonPitSymbol(method)) return;
		for (var i = 0; i < list.Arguments.Count; i++)
		{
			var argument = list.Arguments[i];
			string? name = argument.NameColon?.Name.Identifier.Text;
			if (name == null && i < method.Parameters.Length) name = method.Parameters[i].Name;
			if (name != null && SuppressesCleanup(name, argument.Expression))
				emit(DiagnosticDescriptors.WarnChangeFileCleanupAvoided, argument.GetLocation(),
					new object[] { $"argument '{name}: {argument.Expression}' avoids change-file cleanup" });
		}
	}

	private void CheckCleanupAssignment(AssignmentExpressionSyntax assignment)
	{
		var symbol = model.GetSymbolInfo(assignment.Left).Symbol;
		if (symbol == null) return;
		if (symbol.Name == "ChangeFileCleanupGrace" && IsJsonPitSymbol(symbol))
		{
			emit(DiagnosticDescriptors.WarnChangeFileCleanupAvoided, assignment.GetLocation(),
				new object[] { "overriding 'ChangeFileCleanupGrace' postpones change-file cleanup" });
			return;
		}
		if (symbol is IPropertySymbol or IFieldSymbol && IsJsonPitSymbol(symbol) && SuppressesCleanup(symbol.Name, assignment.Right))
			emit(DiagnosticDescriptors.WarnChangeFileCleanupAvoided, assignment.GetLocation(),
				new object[] { $"option '{symbol.Name} = {assignment.Right}' avoids change-file cleanup" });
	}

	private bool SuppressesCleanup(string name, ExpressionSyntax value)
	{
		var constant = model.GetConstantValue(value);
		if (!constant.HasValue || constant.Value is not bool flag) return false;
		var key = name.Replace("_", string.Empty).ToLowerInvariant();
		if (flag && SuppressTrueNames.Contains(key)) return true;
		if (!flag && SuppressFalseNames.Contains(key)) return true;
		return false;
	}

	// ---- helpers ------------------------------------------------------------------------------

	private sealed class Member
	{
		internal readonly string Name;
		internal readonly INamedTypeSymbol Type;
		internal Member(string name, INamedTypeSymbol type) { Name = name; Type = type; }
	}

	private Member? Owner(SyntaxNode node)
	{
		var symbol = model.GetEnclosingSymbol(node.SpanStart);
		while (symbol is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction } m)
			symbol = m.ContainingSymbol;
		if (symbol is IMethodSymbol { AssociatedSymbol: { } associated }) symbol = associated;
		return symbol?.ContainingType is { } type ? new Member(symbol.Name, type) : null;
	}

	private static bool IsJsonPitSymbol(ISymbol symbol)
	{
		for (var ns = symbol.ContainingNamespace; ns != null && !ns.IsGlobalNamespace; ns = ns.ContainingNamespace)
			if (ns.Name == "JsonPit" && ns.ContainingNamespace is { IsGlobalNamespace: true }) return true;
		return false;
	}

	private static bool IsType(ITypeSymbol? type, string name)
	{
		for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
			if (current.OriginalDefinition.ToDisplayString() == name) return true;
		return false;
	}
}
