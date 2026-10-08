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

        return containingType.Name == "Directory" &&
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

        return containingType.Name == "File" &&
               containingType.ContainingNamespace is { Name: "IO", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } };
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
