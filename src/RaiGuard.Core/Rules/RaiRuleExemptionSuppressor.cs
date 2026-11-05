using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using RaiGuard.Core.Diagnostics;

namespace RaiGuard.Core.Rules;

/// <summary>Honors explicit declarations consistently for every RaiGuard compiler analyzer.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RaiRuleExemptionSuppressor : DiagnosticSuppressor
{
	private static readonly ImmutableDictionary<string, SuppressionDescriptor> Descriptors =
		DiagnosticDescriptors.Catalog.ToImmutableDictionary(entry => entry.Id, entry =>
			new SuppressionDescriptor("SP" + entry.Id, entry.Id, "Explicit RaiRuleExemption with a nonblank justification on the containing method or class"));

	public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => Descriptors.Values.ToImmutableArray();

	public override void ReportSuppressions(SuppressionAnalysisContext context)
	{
		foreach (var diagnostic in context.ReportedDiagnostics)
		{
			var tree = diagnostic.Location.SourceTree;
			if (tree != null && Descriptors.TryGetValue(diagnostic.Id, out var descriptor) &&
				RuleExemptions.IsExempt(context.GetSemanticModel(tree), diagnostic.Location.SourceSpan.Start, diagnostic.Id))
				context.ReportSuppression(Suppression.Create(descriptor, diagnostic));
		}
	}
}
