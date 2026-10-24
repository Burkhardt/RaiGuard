using System;
using System.Collections.Generic;
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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RAI017CodeFixProvider)), Shared]
public sealed class RAI017CodeFixProvider : CodeFixProvider
{
	private const string TargetUsingNamespace = "AIA.Core.Services.Media";
	private const string CodeFixTitle = "Replace with MediaUrls.Video / MediaUrls.Image";

	public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create("RAI017");

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

		var expr = node.FirstAncestorOrSelf<InterpolatedStringExpressionSyntax>()
			?? (ExpressionSyntax?)node.FirstAncestorOrSelf<BinaryExpressionSyntax>();

		if (expr is null)
		{
			return;
		}

		var replacement = BuildMediaUrlsInvocation(expr);
		if (replacement is null)
		{
			return;
		}

		context.RegisterCodeFix(
			CodeAction.Create(
				title: CodeFixTitle,
				createChangedDocument: ct => ApplyFixAsync(context.Document, root, expr, replacement, ct),
				equivalenceKey: CodeFixTitle),
			diagnostic);
	}

	private static Task<Document> ApplyFixAsync(
		Document document,
		SyntaxNode root,
		ExpressionSyntax oldNode,
		ExpressionSyntax newNode,
		CancellationToken cancellationToken)
	{
		var newRoot = root.ReplaceNode(oldNode, newNode.WithTriviaFrom(oldNode));

		if (newRoot is CompilationUnitSyntax compUnit)
		{
			bool hasUsing = compUnit.Usings.Any(u => u.Name?.ToString() == TargetUsingNamespace);
			if (!hasUsing)
			{
				var lineEnding = root.ToFullString().Contains("\r\n")
					? SyntaxFactory.CarriageReturnLineFeed
					: SyntaxFactory.LineFeed;

				var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(TargetUsingNamespace))
					.NormalizeWhitespace()
					.WithTrailingTrivia(lineEnding);

				newRoot = compUnit.AddUsings(usingDirective);
			}
		}

		return Task.FromResult(document.WithSyntaxRoot(newRoot));
	}

	public static InvocationExpressionSyntax? BuildMediaUrlsInvocation(ExpressionSyntax expr)
	{
		if (expr is InterpolatedStringExpressionSyntax interpolated)
		{
			return BuildFromInterpolation(interpolated);
		}

		if (expr is BinaryExpressionSyntax bin)
		{
			return BuildFromBinary(bin);
		}

		return null;
	}

	private static InvocationExpressionSyntax? BuildFromInterpolation(InterpolatedStringExpressionSyntax interpolated)
	{
		var holes = new List<(ExpressionSyntax Expr, string PrecedingText)>();
		string currentText = string.Empty;
		string fullText = string.Empty;

		foreach (var content in interpolated.Contents)
		{
			if (content is InterpolatedStringTextSyntax text)
			{
				currentText += text.TextToken.ValueText;
				fullText += text.TextToken.ValueText;
			}
			else if (content is InterpolationSyntax hole)
			{
				holes.Add((hole.Expression, currentText));
				fullText += "{" + hole.Expression + "}";
				currentText = string.Empty;
			}
		}

		return BuildFromComponents(holes, fullText);
	}

	private static InvocationExpressionSyntax? BuildFromBinary(BinaryExpressionSyntax bin)
	{
		var literalsAndExprs = new List<(ExpressionSyntax Expr, string PrecedingText)>();
		var parts = FlattenAdd(bin);
		string fullText = string.Empty;
		string pendingText = string.Empty;

		foreach (var part in parts)
		{
			if (part is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
			{
				pendingText += lit.Token.ValueText;
				fullText += lit.Token.ValueText;
			}
			else
			{
				literalsAndExprs.Add((part, pendingText));
				fullText += "{" + part + "}";
				pendingText = string.Empty;
			}
		}

		return BuildFromComponents(literalsAndExprs, fullText);
	}

	private static List<ExpressionSyntax> FlattenAdd(BinaryExpressionSyntax bin)
	{
		var result = new List<ExpressionSyntax>();
		void Collect(ExpressionSyntax node)
		{
			if (node is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.AddExpression))
			{
				Collect(b.Left);
				Collect(b.Right);
			}
			else
			{
				result.Add(node);
			}
		}
		Collect(bin);
		return result;
	}

	private static InvocationExpressionSyntax? BuildFromComponents(
		List<(ExpressionSyntax Expr, string PrecedingText)> holes,
		string fullText)
	{
		if (holes.Count == 0)
		{
			return null;
		}

		bool isVideo = fullText.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
			fullText.Contains(".mp4?") ||
			fullText.Contains(".mp4\"");

		bool isPoster = fullText.Contains("tmp=Poster", StringComparison.OrdinalIgnoreCase);

		if (isVideo)
		{
			// Format: /img/{tenant}/{show}_{inr}.mp4 -> MediaUrls.Video(show, inr, preset: "Stage", sub: tenant)
			// Format: /img/{show}_{inr}.mp4 -> MediaUrls.Video(show, inr, preset: "Stage")
			if (holes.Count >= 3)
			{
				var sub = holes[0].Expr;
				var show = holes[1].Expr;
				var inr = holes[2].Expr;

				return CreateMediaUrlsCall("Video",
					SyntaxFactory.Argument(show),
					SyntaxFactory.Argument(inr),
					SyntaxFactory.Argument(SyntaxFactory.NameColon("preset"), default, SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal("Stage"))),
					SyntaxFactory.Argument(SyntaxFactory.NameColon("sub"), default, sub));
			}
			else if (holes.Count == 2)
			{
				var show = holes[0].Expr;
				var inr = holes[1].Expr;

				return CreateMediaUrlsCall("Video",
					SyntaxFactory.Argument(show),
					SyntaxFactory.Argument(inr),
					SyntaxFactory.Argument(SyntaxFactory.NameColon("preset"), default, SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal("Stage"))));
			}
		}
		else if (isPoster)
		{
			// Format: https://api.aia.software/img/{show}?inr={inr}&tmp=Poster&sub={sub} -> MediaUrls.Poster(show, inr, sub: tenant)
			ExpressionSyntax? show = null;
			ExpressionSyntax? inr = null;
			ExpressionSyntax? sub = null;

			foreach (var (expr, preceding) in holes)
			{
				if (preceding.Contains("/img/"))
				{
					show = expr;
				}
				else if (preceding.Contains("inr="))
				{
					inr = expr;
				}
				else if (preceding.Contains("sub="))
				{
					sub = expr;
				}
			}

			show ??= holes[0].Expr;
			inr ??= holes.Count > 1 ? holes[1].Expr : holes[0].Expr;

			if (sub != null)
			{
				return CreateMediaUrlsCall("Poster",
					SyntaxFactory.Argument(show),
					SyntaxFactory.Argument(inr),
					SyntaxFactory.Argument(SyntaxFactory.NameColon("sub"), default, sub));
			}
			else
			{
				return CreateMediaUrlsCall("Poster",
					SyntaxFactory.Argument(show),
					SyntaxFactory.Argument(inr));
			}
		}

		// Default: Image
		// Format: /img/{tenant}/{tree}/{id}.svg -> MediaUrls.Image(tree, id, sub: tenant)
		// Format: /img/{show}/{inr} -> MediaUrls.Image(show, inr)
		if (holes.Count >= 3)
		{
			var sub = holes[0].Expr;
			var tree = holes[1].Expr;
			var id = holes[2].Expr;

			return CreateMediaUrlsCall("Image",
				SyntaxFactory.Argument(tree),
				SyntaxFactory.Argument(id),
				SyntaxFactory.Argument(SyntaxFactory.NameColon("sub"), default, sub));
		}
		else if (holes.Count == 2)
		{
			var arg1 = holes[0].Expr;
			var arg2 = holes[1].Expr;

			return CreateMediaUrlsCall("Image",
				SyntaxFactory.Argument(arg1),
				SyntaxFactory.Argument(arg2));
		}
		else
		{
			return CreateMediaUrlsCall("Image",
				SyntaxFactory.Argument(holes[0].Expr));
		}
	}

	private static InvocationExpressionSyntax CreateMediaUrlsCall(string method, params ArgumentSyntax[] args)
	{
		var memberAccess = SyntaxFactory.MemberAccessExpression(
			SyntaxKind.SimpleMemberAccessExpression,
			SyntaxFactory.IdentifierName("MediaUrls"),
			SyntaxFactory.IdentifierName(method));

		return SyntaxFactory.InvocationExpression(
			memberAccess,
			SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(args)));
	}
}
