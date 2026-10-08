using Microsoft.CodeAnalysis;

namespace RaiGuard.Core.Diagnostics;

public static class DiagnosticDescriptors
{
    private const string Category = "RaiGuard.Reliability";

    public static readonly DiagnosticDescriptor ProhibitSystemIoDirectory = new(
        id: "RAI001",
        title: "Prohibit direct usage of System.IO.Directory",
        messageFormat: "'System.IO.Directory.{0}' is prohibited; use 'RaiPath' methods instead (e.g., RaiPath.EnumerateDirectories or RaiPath.EnumerateFiles)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Enforces framework-specific path physics by requiring RaiPath abstractions instead of System.IO.Directory.");

    public static readonly DiagnosticDescriptor ProhibitSystemIoFile = new(
        id: "RAI002",
        title: "Prohibit direct usage of System.IO.File",
        messageFormat: "'System.IO.File.{0}' is prohibited; use 'RaiFile' or 'TextFile' methods instead (e.g., new RaiFile(path).Exists(), new RaiFile(path).rm(), new TextFile(path).Read(), new TextFile(path, content))",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Enforces framework-specific path physics by requiring RaiFile and TextFile abstractions instead of System.IO.File (e.g., new RaiFile(path).rm(), new TextFile(path).Read(), new TextFile(path, content)).");

    public static readonly DiagnosticDescriptor ProhibitSystemIoPath = new(
        id: "RAI003",
        title: "Prohibit direct usage of System.IO.Path",
        messageFormat: "'System.IO.Path.{0}' is prohibited; use 'RaiPath' or 'RaiRelPath' with operator '/' instead",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Enforces framework-specific path physics by requiring RaiPath abstractions and operator '/' instead of System.IO.Path.");

    public static readonly DiagnosticDescriptor EnforceWhitespaceFormatting = new(
        id: "RAI010",
        title: "Enforce tab indentation and prohibit consecutive spaces",
        messageFormat: "{0}",
        category: "RaiGuard.Formatting",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Enforces codebase formatting invariants: all indentation must use tabs ('\\t'), and tokens outside string/char literals must not have consecutive spaces.");
}
