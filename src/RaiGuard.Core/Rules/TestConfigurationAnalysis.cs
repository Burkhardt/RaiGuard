using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RaiGuard.Core.Rules;

internal static class TestConfigurationAnalysis
{
	private static readonly HashSet<string> ConfigurationEnvironment = new(StringComparer.OrdinalIgnoreCase)
	{
		"HOME", "USERPROFILE", "HOMEDRIVE", "HOMEPATH", "APPDATA", "LOCALAPPDATA",
		"XDG_CONFIG_HOME", "RAIKEEP_CONFIG", "JSONPIT_CONFIG",
		"OneDrive", "OneDriveCommercial", "OneDriveConsumer"
	};

	internal static void Analyze(SyntaxTree tree, SyntaxNode root, SemanticModel? model, Action<Location, string> report)
	{
		var path = tree.FilePath.Replace('\\', '/');
		if (!path.Split('/').Any(part => part.Equals("tests", StringComparison.OrdinalIgnoreCase)
			|| part.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
			&& !root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>()
				.Any(ns => ns.Name.ToString().EndsWith(".Tests", StringComparison.Ordinal))
			&& !root.DescendantNodes().OfType<ClassDeclarationSyntax>()
				.Any(type => type.Identifier.ValueText.EndsWith("Tests", StringComparison.Ordinal)))
			return;

		string? Constant(ExpressionSyntax expression)
		{
			if (model != null)
			{
				var constant = model.GetConstantValue(expression);
				if (constant.HasValue && constant.Value is string value) return value;
			}
			return expression is LiteralExpressionSyntax literal ? literal.Token.ValueText : null;
		}

		bool ConfigurationPath(ExpressionSyntax expression, HashSet<string> visited)
		{
			if (expression.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>()
				.Any(literal => literal.Token.ValueText.EndsWith("RAIkeep.json5", StringComparison.OrdinalIgnoreCase)))
				return true;
			if (expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>()
				.Any(member => member.Name.Identifier.ValueText == "ConfigurationFile"))
				return true;
			var scope = expression.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>();
			if (scope == null) return false;
			foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
			{
				var name = identifier.Identifier.ValueText;
				if (!visited.Add(name)) continue;
				var declaration = scope.DescendantNodes().OfType<VariableDeclaratorSyntax>()
					.FirstOrDefault(variable => variable.Identifier.ValueText == name);
				if (declaration?.Initializer?.Value is ExpressionSyntax initializer
					&& ConfigurationPath(initializer, visited)) return true;
			}
			return false;
		}

		bool LoadedConfiguration(ExpressionSyntax expression, HashSet<string> visited)
		{
			if (expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>()
				.Any(member => member.ToString() == "Os.Config")) return true;
			var scope = expression.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>();
			if (scope == null) return false;
			foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
			{
				var name = identifier.Identifier.ValueText;
				if (!visited.Add(name)) continue;
				var declaration = scope.DescendantNodes().OfType<VariableDeclaratorSyntax>()
					.FirstOrDefault(variable => variable.Identifier.ValueText == name);
				if (declaration?.Initializer?.Value is ExpressionSyntax initializer
					&& LoadedConfiguration(initializer, visited)) return true;
			}
			return false;
		}

		bool IsRoleParameter(LiteralExpressionSyntax literal)
		{
			if (!literal.Token.ValueText.Equals("cloud", StringComparison.OrdinalIgnoreCase) ||
				literal.Parent?.Parent?.Parent is not ImplicitElementAccessSyntax index ||
				index.Parent is not AssignmentExpressionSyntax assignment ||
				assignment.Right is not MemberAccessExpressionSyntax role ||
				role.Name.Identifier.ValueText is not ("TestCloud" or "LiveCloud")) return false;
			var symbol = model?.GetSymbolInfo(role).Symbol;
			return symbol != null ? symbol.ContainingType?.ToDisplayString() == "OsLib.Os"
				: role.Expression.ToString() is "Os" or "OsLib.Os" or "global::OsLib.Os";
		}

		bool HasCloudConfiguration(SyntaxNode node) =>
			node.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>()
				.Any(literal => literal.Token.ValueText.IndexOf("Cloud", StringComparison.OrdinalIgnoreCase) >= 0 && !IsRoleParameter(literal));

		foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			var name = invocation.Expression switch
			{
				MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
				IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
				_ => ""
			};
			var arguments = invocation.ArgumentList.Arguments;
			if (name == "SetEnvironmentVariable" && arguments.Count > 0
				&& Constant(arguments[0].Expression) is string key && ConfigurationEnvironment.Contains(key))
				report(invocation.GetLocation(), $"Do not redirect configuration by setting '{key}' in a test.");

			if (invocation.Expression is MemberAccessExpressionSyntax access
				&& access.Expression.ToString() == "TestRuntime" && name == "Create" && arguments.Count > 0)
				report(invocation.GetLocation(), "Review the configuration home passed to TestRuntime.Create; tests must use the real home.");

			if (invocation.Expression is MemberAccessExpressionSyntax parser
				&& parser.Expression.ToString() == "TestJson" && name == "ParseObject"
				&& arguments.Any(argument => HasCloudConfiguration(argument.Expression)))
				report(invocation.GetLocation(), "Review synthetic cloud configuration; tests must not replace the real configuration.");

			if ((name.StartsWith("WriteAll", StringComparison.Ordinal)
				|| name.StartsWith("AppendAll", StringComparison.Ordinal)
				|| name is "Delete" or "Create" or "Copy" or "Move" or "Replace")
				&& arguments.Any(argument => ConfigurationPath(argument.Expression, new HashSet<string>(StringComparer.Ordinal))))
				report(invocation.GetLocation(), "Do not create, replace or mutate a RAIkeep configuration file in a test.");
		}

		foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
		{
			if ((creation.Type.ToString() is "ConfigurationDocument" or "JObject")
				&& HasCloudConfiguration(creation))
				report(creation.GetLocation(), "Review synthetic cloud configuration; tests must not replace the real configuration.");
			else if ((creation.Type is QualifiedNameSyntax qualified ? qualified.Right : creation.Type)
				is GenericNameSyntax { Identifier.ValueText: "Dictionary" } && creation.Initializer != null)
			{
				foreach (var entry in creation.Initializer.Expressions.OfType<AssignmentExpressionSyntax>())
					if (entry.Left is ImplicitElementAccessSyntax index && index.ArgumentList.Arguments.Count == 1 &&
						index.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax key &&
						key.Token.ValueText == "cloud" && !IsRoleParameter(key))
						report(entry.GetLocation(), "Review the cloud provider supplied by this test fixture.");
			}
		}

		foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
		{
			if (assignment.Left is ElementAccessExpressionSyntax element
				&& element.ArgumentList.Arguments.Count == 1
				&& Constant(element.ArgumentList.Arguments[0].Expression) is string key
				&& ConfigurationEnvironment.Contains(key)
				&& (element.Expression.ToString().IndexOf("environment", StringComparison.OrdinalIgnoreCase) >= 0
					|| element.Expression.ToString().Equals("env", StringComparison.OrdinalIgnoreCase)))
				report(assignment.GetLocation(), $"Do not redirect configuration by setting '{key}' in a test environment.");
			if (assignment.Left is not IdentifierNameSyntax
				&& LoadedConfiguration(assignment.Left, new HashSet<string>(StringComparer.Ordinal)))
				report(assignment.GetLocation(), "Do not change the loaded Os.Config or its cloud mappings in a test.");
		}
	}
}
