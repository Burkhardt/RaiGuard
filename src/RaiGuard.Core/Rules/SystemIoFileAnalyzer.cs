using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using RaiGuard.Core.Diagnostics;
using RaiGuard.Core.Helpers;

namespace RaiGuard.Core.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SystemIoFileAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.ProhibitSystemIoFile);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol methodSymbol)
        {
            return;
        }

        if (methodSymbol.IsSystemIoFile())
        {
            Location location;
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                location = memberAccess.Name.GetLocation();
            }
            else
            {
                location = invocation.GetLocation();
            }

            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.ProhibitSystemIoFile,
                location,
                methodSymbol.Name);

            context.ReportDiagnostic(diagnostic);
        }
    }
}
