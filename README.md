# RaiGuard

Opinionated Roslyn analyzer, code-fix provider, and `rail` command-line tool enforcing framework physics and conventions across RAIkeep solutions.

## 4.5.5

Initial release of `rail` CLI and RaiGuard Roslyn guardrail engine. Public behavior is aligned with the synchronized RAIkeep platform (4.5.5 dependency line).

## Terminal font

> **Font note:** The `rail` help screen and console diagnostics use glyph icons from Nerd Fonts. Most
> Nerd Font-patched fonts render correctly in most terminal environments. Blink
> on iPadOS showed clipping and character-width problems with some choices; the
> tested solution was Blink's
> [Jet Brains Mono Nerd Font stylesheet](https://github.com/blinksh/patched-fonts/blob/main/Jet%20Brains%20Mono%20Nerd%20Font.css).
> See the RAIkeep
> [terminal font guide](https://github.com/Burkhardt/RAIkeep/blob/main/doc/TERMINAL_FONTS.md)
> for Blink, macOS, and Ubuntu setup.

## Overview

RaiGuard prevents LLMs and developers from backsliding into standard .NET boilerplate (such as `System.IO.Directory` or default serializers) when custom abstractions (`RaiPath`, `JsonPit`, etc.) are required.

It operates as two complementary NuGet packages:
1. **[`RaiGuard.Analyzer`](https://www.nuget.org/packages/RaiGuard.Analyzer)**: Roslyn analyzer and code-fix package invoked directly during `dotnet build`, IDE compilation, and CI pipelines to emit compiler warnings/errors and provide automated quick fixes.
2. **[`RaiGuard`](https://www.nuget.org/packages/RaiGuard)**: Official .NET Global Tool CLI (`ToolCommandName=rail`) running anywhere .NET is installed to audit, verify, and automatically refactor source files across repositories.

## Quick Start

### Install the Global CLI Tool (`rail`)

```bash
dotnet tool install --global RaiGuard
rail --version
```

### Install the Roslyn Analyzer Package

```xml
<PackageReference Include="RaiGuard.Analyzer" Version="4.5.5">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

> 📖 **Comprehensive Adoption Guide**: See [INTEGRATION_GUIDE.md](INTEGRATION_GUIDE.md) for repository-wide protection via `Directory.Build.props`, VS Code C# Dev Kit live squiggles, `.editorconfig` severity overrides, and CI pipelines.

## CLI Commands & Options

```text
Commands: check, fix, rules
  rail check [<path>] [--rule <id>] [--json]
  rail fix [<path>] [--rule <id>] [--dry-run]
  rail rules [--json]

Options:
  -h, --help      print out all options
  -v, --version   print version info
  -n, --nologo    do not display the banner
  -b, --debug     enable debug output
  -p, --path      target file or directory (defaults to current directory)
  --rule          filter by rule ID (e.g. RAI001)
  --json          output diagnostics in JSON format
  --dry-run       simulate code fixes without modifying source files on disk
```

### Examples

Check current directory for framework violations:
```bash
rail check
```

Automatically refactor prohibited patterns to framework equivalents:
```bash
rail fix
```

Inspect active guardrail definitions:
```bash
rail rules
```

## Guardrail Rules

| Rule ID | Severity | Description | Remedy |
|---------|----------|-------------|--------|
| `RAI001` | Warning | Direct usage of `System.IO.Directory` methods is prohibited | Use `RaiPath` methods (e.g. `RaiPath.EnumerateDirectories`, `RaiPath.EnumerateFiles`) |
| `RAI002` | Warning | Direct usage of `System.IO.File` methods is prohibited | Use `RaiFile` or `TextFile` methods (e.g. `new RaiFile(filename).Exists()`, `new RaiFile(filename).rm()`, `new TextFile(filename).Read()`, `new TextFile(filename, "first line\nsecond line")`) |
| `RAI003` | Warning | Direct usage of `System.IO.Path` methods is prohibited | Use `RaiPath` or `RaiRelPath` with operator `/` (e.g. `new RaiPath(a) / b`) |
| `RAI010` | Warning | Tab indentation required and consecutive spaces prohibited | Indent code with tabs (`\t`) and use single spaces between tokens outside string and character literals |

### Idiomatic Platform Patterns

- **Writing / Creating**: `new TextFile(filename, "first line\nsecond line");` (constructor appends and writes to disk immediately)
- **Reading**: `var tf = new TextFile(filename).Read();` (reads directly into memory as `List<string>`)
- **In-Memory Manipulation & Save**:
  - `tf.Delete(0);` (removes line 0 in memory and marks `Changed = true`; updates disk on next `.Save()`)
  - `tf.DeleteAll().Save();` (clears all lines in memory and commits the empty file to disk)
- **Physical File Removal**: `new RaiFile(filename).rm();` or `tf.rm();` (removes the file from disk via POSIX `rm()`)
- **Existence**: `new RaiFile(filename).Exists();`
- **Path Composition (Operator `/`)**:
  - `var myPath = new RaiPath("~") / ".config";`
  - `workspace ??= Os.TempDir / "RAIkeep" / "image-import" / Guid.NewGuid().ToString("N");`
  - *(Never use `System.IO.Path.Combine`)*



## Solution Structure

```text
RaiGuard/
├── rail/                                 # 1. The rail CLI tool (executable anywhere dotnet is installed)
│   ├── rail.csproj                       # ToolCommandName=rail, references all RAIkeep packages
│   └── Program.cs                        # CLI parsing, Nerd Font phenotype, and analysis/refactoring engine
│
├── rail.Tests/                           # 2. CLI End-to-End Tests
│   ├── rail.Tests.csproj
│   └── RailCliTests.cs
│
├── src/
│   ├── RaiGuard.Core/                    # 3. Roslyn Analyzer & Diagnostic Engine (netstandard2.0)
│   │   ├── RaiGuard.Core.csproj
│   │   ├── Diagnostics/DiagnosticDescriptors.cs
│   │   ├── Rules/SystemIoDirectoryAnalyzer.cs
│   │   ├── Rules/SystemIoFileAnalyzer.cs
│   │   ├── Rules/SystemIoPathAnalyzer.cs
│   │   ├── Rules/WhitespaceFormattingAnalyzer.cs
│   │   └── Helpers/SemanticExtensions.cs
│   │
│   ├── RaiGuard.CodeFixes/               # 4. Automated Code Fix Providers (netstandard2.0)
│   │   ├── RaiGuard.CodeFixes.csproj
│   │   ├── Fixes/SystemIoDirectoryCodeFix.cs
│   │   ├── Fixes/SystemIoFileCodeFix.cs
│   │   └── Fixes/SystemIoPathCodeFix.cs
│   │
│   └── RaiGuard.Package/                 # 5. Analyzer NuGet Packaging Project
│       └── RaiGuard.Package.csproj       # Packages analyzers into analyzers/dotnet/cs
│
├── tests/
│   └── RaiGuard.Tests/                   # 6. Unit Tests for Analyzers & Code Fixes
│       ├── RaiGuard.Tests.csproj
│       ├── SystemIoDirectoryAnalyzerTests.cs
│       ├── SystemIoFileAnalyzerTests.cs
│       ├── SystemIoPathAnalyzerTests.cs
│       └── WhitespaceFormattingAnalyzerTests.cs
│
├── samples/
│   └── HeatRecord/                       # 7. Sample App & Tutorial (Sparse Pit + Guardrails)
│       ├── HeatRecord.csproj
│       ├── Program.cs
│       ├── Program.Naive.cs
│       └── TUTORIAL.md
│
├── RaiGuard.slnx                         # Solution (slnx format)
└── RaiGuard.sln                          # Solution (classic sln format)
```

## Building & Installation

See [BuildFromSource.md](BuildFromSource.md) for detailed build and deployment instructions.
