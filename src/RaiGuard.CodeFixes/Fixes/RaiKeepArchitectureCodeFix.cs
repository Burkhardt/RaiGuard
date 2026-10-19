using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RaiGuard.CodeFixes.Fixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RaiKeepArchitectureCodeFix)), Shared]
public sealed class RaiKeepArchitectureCodeFix : CodeFixProvider
{
	public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(
		"RAI004",
		"RAI013",
		"RAI016"
	);

	public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

	public override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		if (root is null)
		{
			return;
		}

		var diagnostic = context.Diagnostics[0];
		var diagnosticSpan = diagnostic.Location.SourceSpan;
		var node = root.FindNode(diagnosticSpan);

		if (diagnostic.Id == "RAI004")
		{
			var literal = node.FirstAncestorOrSelf<LiteralExpressionSyntax>();
			if (literal != null && literal.IsKind(SyntaxKind.StringLiteralExpression))
			{
				var currentVal = literal.Token.ValueText;
				var newVal = currentVal + "/";
				var title = $"Append trailing '/' ('\"{newVal}\"')";
				context.RegisterCodeFix(
					CodeAction.Create(
						title: title,
						createChangedDocument: _ =>
						{
							var newLiteral = SyntaxFactory.LiteralExpression(
								SyntaxKind.StringLiteralExpression,
								SyntaxFactory.Literal(newVal)).WithTriviaFrom(literal);
							return Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(literal, newLiteral)));
						},
						equivalenceKey: title),
					diagnostic);
			}
		}
		else if (diagnostic.Id == "RAI013")
		{
			var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
			if (invocation?.Expression is MemberAccessExpressionSyntax outerMa &&
				outerMa.Name.Identifier.Text == "Exists" &&
				outerMa.Expression is MemberAccessExpressionSyntax innerMa &&
				innerMa.Name.Identifier.Text == "PitDir")
			{
				var title = "Replace 'PitDir.Exists()' with 'JsonFile.Path.Exists()'";
				context.RegisterCodeFix(
					CodeAction.Create(
						title: title,
						createChangedDocument: _ =>
						{
							var jsonFileMa = SyntaxFactory.MemberAccessExpression(
								SyntaxKind.SimpleMemberAccessExpression,
								innerMa.Expression,
								SyntaxFactory.IdentifierName("JsonFile"));
							var pathMa = SyntaxFactory.MemberAccessExpression(
								SyntaxKind.SimpleMemberAccessExpression,
								jsonFileMa,
								SyntaxFactory.IdentifierName("Path"));
							var existsMa = SyntaxFactory.MemberAccessExpression(
								SyntaxKind.SimpleMemberAccessExpression,
								pathMa,
								SyntaxFactory.IdentifierName("Exists"));
							var newInv = invocation.WithExpression(existsMa).WithTriviaFrom(invocation);
							return Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(invocation, newInv)));
						},
						equivalenceKey: title),
					diagnostic);
			}
		}
		else if (diagnostic.Id == "RAI016")
		{
			var memberAccess = node.FirstAncestorOrSelf<MemberAccessExpressionSyntax>();
			if (memberAccess != null && memberAccess.ToString() == "ImageNamingConvention.Legacy")
			{
				var title = "Replace with 'ImageNamingConvention.Structured'";
				context.RegisterCodeFix(
					CodeAction.Create(
						title: title,
						createChangedDocument: _ =>
						{
							var newMa = memberAccess.WithName(SyntaxFactory.IdentifierName("Structured")).WithTriviaFrom(memberAccess);
							return Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(memberAccess, newMa)));
						},
						equivalenceKey: title),
					diagnostic);
			}
		}
	}
}
