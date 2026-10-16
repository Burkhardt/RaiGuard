using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using RaiGuard.Core.Diagnostics;
using RaiGuard.Core.Helpers;

namespace RaiGuard.Core.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SystemIoPathAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(DiagnosticDescriptors.ProhibitSystemIoPath);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
		context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
	}

	private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
	{
		var invocation = (InvocationExpressionSyntax)context.Node;

		if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol methodSymbol)
		{
			return;
		}

		if (methodSymbol.IsSystemIoPath())
		{
			Location location = invocation.Expression is MemberAccessExpressionSyntax memberAccess
				? memberAccess.Name.GetLocation()
				: invocation.GetLocation();

			var diagnostic = Diagnostic.Create(
				DiagnosticDescriptors.ProhibitSystemIoPath,
				location,
				methodSymbol.Name);

			context.ReportDiagnostic(diagnostic);
		}
	}

	private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
	{
		var memberAccess = (MemberAccessExpressionSyntax)context.Node;
		if (memberAccess.Parent is InvocationExpressionSyntax inv && inv.Expression == memberAccess)
		{
			return;
		}

		var symbolInfo = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken);
		var symbol = symbolInfo.Symbol ?? (symbolInfo.CandidateSymbols.Length > 0 ? symbolInfo.CandidateSymbols[0] : null);

		if (symbol is IMethodSymbol or IPropertySymbol or IFieldSymbol && symbol.IsSystemIoPath())
		{
			var diagnostic = Diagnostic.Create(
				DiagnosticDescriptors.ProhibitSystemIoPath,
				memberAccess.Name.GetLocation(),
				symbol.Name);

			context.ReportDiagnostic(diagnostic);
		}
	}
}
