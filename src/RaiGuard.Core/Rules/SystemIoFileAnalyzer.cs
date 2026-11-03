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
		ImmutableArray.Create(DiagnosticDescriptors.ProhibitSystemIoFile, DiagnosticDescriptors.ProhibitFileInfoLength);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
		context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
		context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
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
			Location location = invocation.Expression is MemberAccessExpressionSyntax memberAccess
				? memberAccess.Name.GetLocation()
				: invocation.GetLocation();

			var diagnostic = Diagnostic.Create(
				DiagnosticDescriptors.ProhibitSystemIoFile,
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
		if (symbol is IPropertySymbol { Name: "Length", ContainingType.Name: "FileInfo" } && symbol.IsSystemIoFile())
		{
			// The inline constructor reports this access once, at the original RAI002 location.
			if (FileInfoLengthAccess.InlineCreation(memberAccess) is not null) return;
			context.ReportDiagnostic(Diagnostic.Create(
				DiagnosticDescriptors.ProhibitFileInfoLength, memberAccess.Name.GetLocation()));
			return;
		}

		if (symbol is IMethodSymbol or IPropertySymbol or IFieldSymbol && symbol.IsSystemIoFile())
		{
			var diagnostic = Diagnostic.Create(
				DiagnosticDescriptors.ProhibitSystemIoFile,
				memberAccess.Name.GetLocation(),
				symbol.Name);

			context.ReportDiagnostic(diagnostic);
		}
	}

	private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
	{
		var creation = (ObjectCreationExpressionSyntax)context.Node;

		if (context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken).Symbol is not IMethodSymbol ctorSymbol)
		{
			return;
		}

		if (ctorSymbol.IsSystemIoFile())
		{
			if (ctorSymbol.ContainingType.Name == "FileInfo" && FileInfoLengthAccess.FromCreation(creation) is not null)
			{
				context.ReportDiagnostic(Diagnostic.Create(
					DiagnosticDescriptors.ProhibitFileInfoLength, creation.Type.GetLocation()));
				return;
			}
			var diagnostic = Diagnostic.Create(
				DiagnosticDescriptors.ProhibitSystemIoFile,
				creation.Type.GetLocation(),
				ctorSymbol.ContainingType.Name);

			context.ReportDiagnostic(diagnostic);
		}
	}
}
