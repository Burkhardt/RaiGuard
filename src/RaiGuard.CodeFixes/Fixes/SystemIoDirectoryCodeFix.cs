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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SystemIoDirectoryCodeFix)), Shared]
public sealed class SystemIoDirectoryCodeFix : CodeFixProvider
{
    private const string DiagnosticId = "RAI001";

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
        var memberAccess = token.Parent?.FirstAncestorOrSelf<MemberAccessExpressionSyntax>()
            ?? root.FindNode(diagnosticSpan).FirstAncestorOrSelf<MemberAccessExpressionSyntax>();

        if (memberAccess is null)
        {
            return;
        }

        var invocation = memberAccess.Parent as InvocationExpressionSyntax
            ?? memberAccess.FirstAncestorOrSelf<InvocationExpressionSyntax>();

        var methodName = memberAccess.Name.Identifier.Text;
        var replacementMethodName = MapMethodName(methodName);

        var title = $"Replace with 'new RaiPath(...).{replacementMethodName}(...)'";
        var codeAction = CodeAction.Create(
            title: title,
            createChangedDocument: ct => ReplaceWithRaiPathAsync(context.Document, root, memberAccess, invocation, replacementMethodName, ct),
            equivalenceKey: title);

        context.RegisterCodeFix(codeAction, diagnostic);
    }

    private static string MapMethodName(string originalMethod) =>
        originalMethod switch
        {
            "GetFiles" => "EnumerateFiles",
            "EnumerateFiles" => "EnumerateFiles",
            "GetDirectories" => "EnumerateDirectories",
            "EnumerateDirectories" => "EnumerateDirectories",
            _ => originalMethod
        };

    private static Task<Document> ReplaceWithRaiPathAsync(
        Document document,
        SyntaxNode root,
        MemberAccessExpressionSyntax memberAccess,
        InvocationExpressionSyntax? invocation,
        string replacementMethodName,
        CancellationToken cancellationToken)
    {
        if (invocation != null &&
            invocation.ArgumentList.Arguments.Count >= 1 &&
            (replacementMethodName is "EnumerateDirectories" or "EnumerateFiles"))
        {
            var pathArg = invocation.ArgumentList.Arguments[0];
            var remainingArgs = invocation.ArgumentList.Arguments.Skip(1);

            var newRaiPath = SyntaxFactory.ObjectCreationExpression(
                SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space),
                SyntaxFactory.IdentifierName("RaiPath"),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(pathArg.Expression))),
                null);

            var newMemberAccess = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                newRaiPath,
                SyntaxFactory.IdentifierName(replacementMethodName));

            var newInvocation = SyntaxFactory.InvocationExpression(
                newMemberAccess,
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(remainingArgs)))
                .WithTriviaFrom(invocation);

            var newRoot = root.ReplaceNode(invocation, newInvocation);
            return Task.FromResult(document.WithSyntaxRoot(newRoot));
        }
        else
        {
            var newMemberAccess = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName("RaiPath"),
                SyntaxFactory.IdentifierName(replacementMethodName))
                .WithTriviaFrom(memberAccess);

            var newRoot = root.ReplaceNode(memberAccess, newMemberAccess);
            return Task.FromResult(document.WithSyntaxRoot(newRoot));
        }
    }
}
