using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace RaiGuard.CodeFixes.Fixes;

/// <summary>Rewrites immediately executed seed calls only when all argument tokens are known.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RAI006CodeFixProvider)), Shared]
public sealed class RAI006CodeFixProvider : CodeFixProvider
{
	public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create("RAI006");
	public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

	public override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
		if (root is null || model is null) return;
		var optionsType = model.Compilation.GetTypeByMetadataName("OsLib.PitsCommandOptions");
		if (optionsType is null || !optionsType.GetMembers("Tenant").OfType<IPropertySymbol>()
			.Any(property => property.DeclaredAccessibility == Accessibility.Public && property.Type.SpecialType == SpecialType.System_String)) return;
		var creation = root.FindNode(context.Span).FirstAncestorOrSelf<ObjectCreationExpressionSyntax>();
		if (creation?.Initializer != null || creation?.ArgumentList?.Arguments.Count != 2) return;
		if (model.GetTypeInfo(creation).Type?.ToDisplayString() != "OsLib.RaiSystem") return;
		if (creation.Parent is not MemberAccessExpressionSyntax member || member.Expression != creation ||
			member.Name.Identifier.ValueText != "ExecResult" || member.Parent is not InvocationExpressionSyntax call ||
			call.ArgumentList.Arguments.Count != 0) return;
		var executable = model.GetConstantValue(creation.ArgumentList.Arguments[0].Expression);
		if (!executable.HasValue || executable.Value is not string name || name != "pits") return;
		var tokens = GetTokens(creation.ArgumentList.Arguments[1].Expression, model);
		var replacement = tokens is null ? null : SeedExpression(tokens);
		if (replacement is null) return;
		context.RegisterCodeFix(CodeAction.Create(
			"Use typed PitsCommand seed request",
			_ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(call,
				SyntaxFactory.ParseExpression(replacement).WithTriviaFrom(call).WithAdditionalAnnotations(Formatter.Annotation)))),
			nameof(RAI006CodeFixProvider)), context.Diagnostics[0]);
	}

	private static IReadOnlyList<string>? GetTokens(ExpressionSyntax expression, SemanticModel model)
	{
		var constant = model.GetConstantValue(expression);
		if (constant.HasValue && constant.Value is string text)
		{
			// Quoting/escaping of a raw argument string is host-dependent. Do not infer it.
			if (text.IndexOfAny(new[] { '\'', '"', '\\' }) >= 0) return null;
			return text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		}
		var initializer = expression switch
		{
			ArrayCreationExpressionSyntax array => array.Initializer,
			ImplicitArrayCreationExpressionSyntax array => array.Initializer,
			_ => null
		};
		if (initializer is null) return null;
		var tokens = new List<string>();
		foreach (var element in initializer.Expressions)
		{
			var value = model.GetConstantValue(element);
			if (!value.HasValue || value.Value is not string token) return null;
			tokens.Add(token);
		}
		return tokens;
	}

	private static string? SeedExpression(IReadOnlyList<string> tokens)
	{
		if (tokens.Count < 4 || tokens[0] != "seed" || !PlainName(tokens[1])) return null;
		string? source = null, root = null, cloud = null;
		var flags = new HashSet<string>(StringComparer.Ordinal);
		for (var index = 2; index < tokens.Count; index++)
		{
			var key = tokens[index] switch
			{
				"-s" or "--source" => "source",
				"-r" or "--pitroot" => "root",
				"-c" or "--cloud" or "--cloudprovider" => "cloud",
				"-n" or "--nologo" => "NoLogo",
				"--debug" => "Debug",
				"--retain-window" => "RetainWindow",
				"--require-existing" or "--patch" => "RequireExisting",
				_ => null
			};
			if (key is null || !flags.Add(key)) return null;
			if (key is "source" or "root" or "cloud")
			{
				if (++index >= tokens.Count || string.IsNullOrWhiteSpace(tokens[index]) || tokens[index].StartsWith("-", StringComparison.Ordinal)) return null;
				if (key == "source") source = tokens[index];
				if (key == "root") root = tokens[index];
				if (key == "cloud") cloud = tokens[index];
			}
		}
		if (source is null) return null;
		var options = new List<string>();
		if (root != null)
		{
			if (PlainName(root))
			{
				if (cloud is null) return null;
				options.Add("Tenant = " + Literal(root));
			}
			else
			{
				var rooted = root.StartsWith("/", StringComparison.Ordinal) ||
					(root.Length >= 3 && char.IsLetter(root[0]) && root[1] == ':' && (root[2] == '/' || root[2] == '\\'));
				if (!rooted || cloud != null) return null;
				options.Add("PitRoot = new global::OsLib.RaiPath(" + Literal(root) + ")");
			}
		}
		if (cloud != null) options.Add("CloudProvider = " + Literal(cloud));
		foreach (var flag in new[] { "NoLogo", "Debug", "RetainWindow" })
			if (flags.Contains(flag)) options.Add(flag + " = true");
		var settings = new List<string>();
		if (options.Count > 0) settings.Add("Options = new global::OsLib.PitsCommandOptions { " + string.Join(", ", options) + " }");
		if (flags.Contains("RequireExisting")) settings.Add("RequireExisting = true");
		var request = "global::OsLib.PitsSeedRequest.ForPit(" + Literal(tokens[1]) + ", new global::OsLib.RaiFile(" + Literal(source) + "))";
		if (settings.Count > 0) request += " with { " + string.Join(", ", settings) + " }";
		return "new global::OsLib.PitsCommand().Seed(" + request + ")";
	}

	private static bool PlainName(string name) => !string.IsNullOrWhiteSpace(name) && name == name.Trim() &&
		name is not ("." or ".." or "~") && !name.StartsWith("-", StringComparison.Ordinal) &&
		name.IndexOfAny(new[] { '/', '\\', ':', '\0' }) < 0;

	private static string Literal(string value) => SyntaxFactory.Literal(value).ToString();
}
