using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RaiGuard.CodeFixes.Fixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SystemIoPathCodeFix)), Shared]
public sealed class SystemIoPathCodeFix : CodeFixProvider
{
	private const string DiagnosticId = "RAI003";

	public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticId);

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

		var token = root.FindToken(diagnosticSpan.Start);
		var invocation = token.Parent?.FirstAncestorOrSelf<InvocationExpressionSyntax>()
			?? root.FindNode(diagnosticSpan).FirstAncestorOrSelf<InvocationExpressionSyntax>();

		if (invocation is null || invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
		{
			return;
		}

		var methodName = memberAccess.Name.Identifier.Text;
		if (methodName != "Combine" || invocation.ArgumentList.Arguments.Count < 2)
		{
			return;
		}

		var title = "Replace with operator '/'";
		var codeAction = CodeAction.Create(
			title: title,
			createChangedDocument: ct => ReplaceWithSlashOperatorAsync(context.Document, root, invocation, ct),
			equivalenceKey: title);

		context.RegisterCodeFix(codeAction, diagnostic);
	}

	private static Task<Document> ReplaceWithSlashOperatorAsync(
		Document document,
		SyntaxNode root,
		InvocationExpressionSyntax invocation,
		CancellationToken cancellationToken)
	{
		var args = invocation.ArgumentList.Arguments;

		ExpressionSyntax current = SyntaxFactory.ObjectCreationExpression(
			SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space),
			SyntaxFactory.IdentifierName("RaiPath"),
			SyntaxFactory.ArgumentList(
				SyntaxFactory.SingletonSeparatedList(
					SyntaxFactory.Argument(args[0].Expression))),
			null);

		for (int i = 1; i < args.Count; i++)
		{
			var slashToken = SyntaxFactory.Token(SyntaxKind.SlashToken)
				.WithLeadingTrivia(SyntaxFactory.Space)
				.WithTrailingTrivia(SyntaxFactory.Space);

			current = SyntaxFactory.BinaryExpression(
				SyntaxKind.DivideExpression,
				current,
				slashToken,
				args[i].Expression);
		}

		var newExpression = current.WithTriviaFrom(invocation);
		var newRoot = root.ReplaceNode(invocation, newExpression);

		if (!HasOsLibUsing(newRoot))
		{
			newRoot = AddOsLibUsing(newRoot);
		}

		return Task.FromResult(document.WithSyntaxRoot(newRoot));
	}

	private static bool HasOsLibUsing(SyntaxNode root)
	{
		return root.DescendantNodes()
			.OfType<UsingDirectiveSyntax>()
			.Any(u => u.Name?.ToString() == "OsLib");
	}

	private static SyntaxNode AddOsLibUsing(SyntaxNode root)
	{
		if (root is not CompilationUnitSyntax compUnit)
		{
			return root;
		}

		var lineEnding = root.ToFullString().Contains("\r\n")
			? SyntaxFactory.CarriageReturnLineFeed
			: SyntaxFactory.LineFeed;

		if (compUnit.Usings.Count > 0)
		{
			var lastUsing = compUnit.Usings.Last();
			var trailingTrivia = lastUsing.GetTrailingTrivia();
			var singleEol = trailingTrivia.FirstOrDefault(t => t.IsKind(SyntaxKind.EndOfLineTrivia));
			if (singleEol == default)
			{
				singleEol = lineEnding;
			}

			var updatedLastUsing = lastUsing.WithTrailingTrivia(singleEol);
			var osLibUsing = SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName("OsLib"))
				.NormalizeWhitespace()
				.WithTrailingTrivia(trailingTrivia);

			return compUnit
				.ReplaceNode(lastUsing, updatedLastUsing)
				.AddUsings(osLibUsing);
		}
		else
		{
			var osLibUsing = SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName("OsLib"))
				.NormalizeWhitespace()
				.WithTrailingTrivia(lineEnding);
			return compUnit.AddUsings(osLibUsing);
		}
	}
}
