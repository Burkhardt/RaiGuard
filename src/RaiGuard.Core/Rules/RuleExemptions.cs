using System;
using Microsoft.CodeAnalysis;

namespace RaiGuard.Core.Rules;

/// <summary>Semantic, rule-specific declarations shared by compiler analyzers and rail.</summary>
internal static class RuleExemptions
{
	internal static bool IsExempt(SemanticModel? model, int position, string ruleId)
	{
		if (model == null) return false;
		var type = model.Compilation.GetTypeByMetadataName("OsLib.RaiRuleExemptionAttribute");
		if (type == null) return false;
		for (var symbol = model.GetEnclosingSymbol(position); symbol != null; symbol = symbol.ContainingSymbol)
		{
			if (symbol is not IMethodSymbol && symbol is not INamedTypeSymbol) continue;
			foreach (var attribute in symbol.GetAttributes())
			{
				if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, type) || attribute.ConstructorArguments.Length != 2) continue;
				if (attribute.ConstructorArguments[0].Value is string id &&
					attribute.ConstructorArguments[1].Value is string reason && !string.IsNullOrWhiteSpace(reason) &&
					string.Equals(id.Trim(), ruleId, StringComparison.OrdinalIgnoreCase)) return true;
			}
		}
		return false;
	}
}
