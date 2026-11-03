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
using RaiGuard.Core.Helpers;

namespace RaiGuard.CodeFixes.Fixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SystemIoFileCodeFix)), Shared]
public sealed class SystemIoFileCodeFix : CodeFixProvider
{
	private const string DiagnosticId = "RAI002";

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
		var creation = token.Parent?.FirstAncestorOrSelf<ObjectCreationExpressionSyntax>();
		var lengthAccess = creation is not null && creation.Type.Span.Contains(diagnosticSpan)
			? FileInfoLengthAccess.FromCreation(creation) : null;
		if (lengthAccess is not null)
		{
			var replacement = await FileLengthReplacementAsync(context.Document, lengthAccess, context.CancellationToken).ConfigureAwait(false);
			if (replacement is not null)
			{
				const string lengthTitle = "Replace with 'RaiFile.Length'";
				context.RegisterCodeFix(CodeAction.Create(lengthTitle,
					ct => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(lengthAccess, replacement.WithTriviaFrom(lengthAccess)))),
					equivalenceKey: lengthTitle), diagnostic);
			}
			return;
		}

		var invocation = token.Parent?.FirstAncestorOrSelf<InvocationExpressionSyntax>()
			?? root.FindNode(diagnosticSpan).FirstAncestorOrSelf<InvocationExpressionSyntax>();

		if (invocation is null || invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
		{
			return;
		}

		var methodName = memberAccess.Name.Identifier.Text;
		if (!IsSupportedMethod(methodName, invocation.ArgumentList.Arguments.Count))
		{
			return;
		}

		var title = methodName switch
		{
			"Delete" => "Replace with 'RaiFile.rm()'",
			"Exists" => "Replace with 'RaiFile.Exists()'",
			"ReadAllLines" => "Replace with 'TextFile.Read()'",
			"ReadAllText" => "Replace with 'TextFile.ReadAllText()'",
			"WriteAllText" => "Replace with 'new TextFile(...)'",
			_ => "Replace with RaiFile/TextFile"
		};
		var codeAction = CodeAction.Create(
			title: title,
			createChangedDocument: ct => ReplaceWithFrameworkAbstractionAsync(context.Document, root, invocation, memberAccess, methodName, ct),
			equivalenceKey: title);

		context.RegisterCodeFix(codeAction, diagnostic);
	}

	private static async Task<ExpressionSyntax?> FileLengthReplacementAsync(
		Document document, MemberAccessExpressionSyntax access, CancellationToken cancellationToken)
	{
		var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
		if (model?.GetSymbolInfo(access, cancellationToken).Symbol is not IPropertySymbol property
			|| property.Name != "Length" || property.ContainingType.ToDisplayString() != "System.IO.FileInfo")
			return null;
		var creation = FileInfoLengthAccess.InlineCreation(access);
		if (creation?.ArgumentList?.Arguments.Count != 1 || creation.Initializer is not null) return null;
		var raiFile = model.Compilation.GetTypeByMetadataName("OsLib.RaiFile");
		var length = raiFile?.GetMembers("Length").OfType<IPropertySymbol>()
			.FirstOrDefault(p => !p.IsStatic && p.Type.SpecialType == SpecialType.System_Int64
				&& p.GetMethod?.DeclaredAccessibility == Accessibility.Public);
		if (length is null) return null;
		var argument = creation.ArgumentList.Arguments[0].Expression;
		if (FileInfoLengthAccess.Unwrap(argument) is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "FullName" } fullName)
		{
			var fullNameProperty = model.GetSymbolInfo(fullName, cancellationToken).Symbol as IPropertySymbol;
			while (fullNameProperty?.OverriddenProperty is not null) fullNameProperty = fullNameProperty.OverriddenProperty;
			var direct = SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
				fullName.Expression, SyntaxFactory.IdentifierName("Length"));
			var directSymbol = model.GetSpeculativeSymbolInfo(access.SpanStart, direct, SpeculativeBindingOption.BindAsExpression).Symbol;
			if (SymbolEqualityComparer.Default.Equals(fullNameProperty?.ContainingType, raiFile)
				&& SymbolEqualityComparer.Default.Equals(directSymbol, length)) return direct;
		}
		if (!raiFile!.InstanceConstructors.Any(c => c.DeclaredAccessibility == Accessibility.Public
			&& c.Parameters.Length == 1 && c.Parameters[0].Type.SpecialType == SpecialType.System_String)) return null;
		var newFile = SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName("global::OsLib.RaiFile"))
			.WithNewKeyword(SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space))
			.WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(argument))));
		return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, newFile, SyntaxFactory.IdentifierName("Length"));
	}

	private static bool IsSupportedMethod(string methodName, int argCount) =>
		methodName switch
		{
			"Exists" => argCount >= 1,
			"ReadAllLines" => argCount >= 1,
			"ReadAllText" => argCount >= 1,
			"WriteAllText" => argCount >= 2,
			"Delete" => argCount >= 1,
			_ => false
		};

	private static Task<Document> ReplaceWithFrameworkAbstractionAsync(
		Document document,
		SyntaxNode root,
		InvocationExpressionSyntax invocation,
		MemberAccessExpressionSyntax memberAccess,
		string methodName,
		CancellationToken cancellationToken)
	{
		ExpressionSyntax newExpression;
		var pathArg = invocation.ArgumentList.Arguments[0];

		switch (methodName)
		{
			case "Exists":
			{
				var objectCreation = SyntaxFactory.ObjectCreationExpression(
					SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space),
					SyntaxFactory.IdentifierName("RaiFile"),
					SyntaxFactory.ArgumentList(
						SyntaxFactory.SingletonSeparatedList(
							SyntaxFactory.Argument(pathArg.Expression))),
					null);

				var newMemberAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					objectCreation,
					SyntaxFactory.IdentifierName("Exists"));

				newExpression = SyntaxFactory.InvocationExpression(newMemberAccess, SyntaxFactory.ArgumentList())
					.WithTriviaFrom(invocation);
				break;
			}

			case "Delete":
			{
				var objectCreation = SyntaxFactory.ObjectCreationExpression(
					SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space),
					SyntaxFactory.IdentifierName("RaiFile"),
					SyntaxFactory.ArgumentList(
						SyntaxFactory.SingletonSeparatedList(
							SyntaxFactory.Argument(pathArg.Expression))),
					null);

				var newMemberAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					objectCreation,
					SyntaxFactory.IdentifierName("rm"));

				newExpression = SyntaxFactory.InvocationExpression(newMemberAccess, SyntaxFactory.ArgumentList())
					.WithTriviaFrom(invocation);
				break;
			}

			case "ReadAllLines":
			{
				var objectCreation = SyntaxFactory.ObjectCreationExpression(
					SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space),
					SyntaxFactory.IdentifierName("TextFile"),
					SyntaxFactory.ArgumentList(
						SyntaxFactory.SingletonSeparatedList(
							SyntaxFactory.Argument(pathArg.Expression))),
					null);

				var newMemberAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					objectCreation,
					SyntaxFactory.IdentifierName("Read"));

				newExpression = SyntaxFactory.InvocationExpression(newMemberAccess, SyntaxFactory.ArgumentList())
					.WithTriviaFrom(invocation);
				break;
			}

			case "ReadAllText":
			{
				var objectCreation = SyntaxFactory.ObjectCreationExpression(
					SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space),
					SyntaxFactory.IdentifierName("TextFile"),
					SyntaxFactory.ArgumentList(
						SyntaxFactory.SingletonSeparatedList(
							SyntaxFactory.Argument(pathArg.Expression))),
					null);

				var newMemberAccess = SyntaxFactory.MemberAccessExpression(
					SyntaxKind.SimpleMemberAccessExpression,
					objectCreation,
					SyntaxFactory.IdentifierName("ReadAllText"));

				var remainingArgs = SyntaxFactory.ArgumentList(
					SyntaxFactory.SeparatedList(invocation.ArgumentList.Arguments.Skip(1)));

				newExpression = SyntaxFactory.InvocationExpression(newMemberAccess, remainingArgs)
					.WithTriviaFrom(invocation);
				break;
			}

			case "WriteAllText":
			{
				var textArg = invocation.ArgumentList.Arguments[1];
				newExpression = SyntaxFactory.ObjectCreationExpression(
					SyntaxFactory.IdentifierName("TextFile"))
					.WithArgumentList(SyntaxFactory.ArgumentList(
						SyntaxFactory.SeparatedList(new[]
						{
							SyntaxFactory.Argument(pathArg.Expression),
							SyntaxFactory.Argument(textArg.Expression)
						})))
					.NormalizeWhitespace()
					.WithTriviaFrom(invocation);
				break;
			}

			default:
				return Task.FromResult(document);
		}

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
