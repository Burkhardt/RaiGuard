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
