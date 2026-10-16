using Microsoft.CodeAnalysis;

namespace RaiGuard.Core.Helpers;

public static class SemanticExtensions
{
	public static bool IsSystemIoDirectory(this ISymbol? symbol)
	{
		if (symbol is null)
		{
			return false;
		}

		var containingType = symbol as INamedTypeSymbol ?? symbol.ContainingType;
		if (containingType is null)
		{
			return false;
		}

		return (containingType.Name == "Directory" || containingType.Name == "DirectoryInfo") &&
			containingType.ContainingNamespace is { Name: "IO", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } };
	}

	public static bool IsSystemIoFile(this ISymbol? symbol)
	{
		if (symbol is null)
		{
			return false;
		}

		var containingType = symbol as INamedTypeSymbol ?? symbol.ContainingType;
		if (containingType is null)
		{
			return false;
		}

		if ((containingType.Name == "File" ||
			containingType.Name == "FileInfo" ||
			containingType.Name == "FileStream" ||
			containingType.Name == "StreamReader" ||
			containingType.Name == "StreamWriter") &&
			containingType.ContainingNamespace is { Name: "IO", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } })
		{
			return true;
		}

		if (containingType.Name == "ZipFile" &&
			containingType.ContainingNamespace is { Name: "Compression", ContainingNamespace: { Name: "IO", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } })
		{
			return true;
		}

		return false;
	}

	public static bool IsSystemIoPath(this ISymbol? symbol)
	{
		if (symbol is null)
		{
			return false;
		}

		var containingType = symbol as INamedTypeSymbol ?? symbol.ContainingType;
		if (containingType is null)
		{
			return false;
		}

		return containingType.Name == "Path" &&
			containingType.ContainingNamespace is { Name: "IO", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } };
	}
}
