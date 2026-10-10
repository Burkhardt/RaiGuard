# RaiGuard

Opinionated Roslyn analyzer, code-fix provider, and `rail` command-line tool enforcing framework physics and conventions across RAIkeep solutions.

RaiGuard prevents LLMs and developers from backsliding into standard .NET Base Class Library (BCL) boilerplate (such as `System.IO.Directory`, `System.IO.File`, `Path.Combine`, or space indentation) when custom RAIkeep platform abstractions (`RaiPath`, `TextFile`, `JsonPit`, tabs) are required.

It operates as two complementary deliverables:
1. **[`RaiGuard.Analyzer`](https://www.nuget.org/packages/RaiGuard.Analyzer)**: Roslyn analyzer and code-fix provider invoked during `dotnet build`, IDE keystrokes (VS Code C# Dev Kit), and CI workflows to emit live warnings and automated quick fixes.
2. **[`RaiGuard`](https://www.nuget.org/packages/RaiGuard)**: Cross-platform .NET Global Tool CLI (`ToolCommandName=rail`) running anywhere .NET is installed to audit, verify, and automatically refactor source files across repositories.

---

<details>
<summary><b>Release History & Version Notes</b></summary>

### 4.5.8

- **Receiver-Type Isolation**: Fixed AST rewriter in `rail fix` and Roslyn code-fix provider to strictly isolate innermost receiver types (`Directory.*`, `File.*`, `Path.*`), preventing chained fluent LINQ calls (`.Select()`, `.Where()`, `.OrderBy()`, `.ToList()`) from collapsing.
- **Instance Invocation Refactoring**: Rewrote `Directory.GetDirectories` / `EnumerateDirectories` and `Directory.GetFiles` / `EnumerateFiles` to proper `OsLib` instance invocations: `new RaiPath(path).EnumerateDirectories(...)` and `new RaiPath(path).EnumerateFiles(...)`.
- **Test Harmonization**: Harmonized all test suites across the solution to target `.NET 10.0` (`net10.0`).
- **RAIkeep Alignment**: Synchronized all platform package references (`JsonPit`, `OsLib`, `RaiUtils`, `RaiImage`, `RaiDiagram`) to `4.5.8`.

### 4.5.5

- Initial public release of `rail` CLI and RaiGuard Roslyn guardrail engine.
- Implemented core guardrails: `RAI001` (Directory prohibition), `RAI002` (File prohibition), `RAI003` (Path prohibition), and `RAI010` (Tab indentation enforcement).
- Public behavior aligned with the synchronized RAIkeep platform (4.5.5 dependency line).

</details>

---

<details open>
<summary><b>Installation & PATH Setup (Recommended: /usr/local/bin)</b></summary>

### Option A: Install Directly into `/usr/local/bin` (Recommended)

To run `rail` everywhere on macOS or Linux without needing to configure or modify your shell `PATH` variable:

```bash
sudo dotnet tool install RaiGuard --tool-path /usr/local/bin
```

To update an existing installation in `/usr/local/bin`:

```bash
sudo dotnet tool update RaiGuard --tool-path /usr/local/bin
```

### Option B: Symlink from User Global Tools

If you already have global .NET tools installed in `~/.dotnet/tools`, you can link `rail` into `/usr/local/bin`:

```bash
ln -sf ~/.dotnet/tools/rail /usr/local/bin/rail
```

### Option C: Standard Global Tool Install

Install into user-level `~/.dotnet/tools` directory:

```bash
dotnet tool install --global RaiGuard
```

Update to the latest version:

```bash
dotnet tool update --global RaiGuard
```

*(Ensure `~/.dotnet/tools` is added to your shell's `PATH` when using this option)*

### Roslyn Analyzer Package (Project-Level)

To enforce guardrails during `dotnet build` and in IDEs, add the analyzer package to your `.csproj` or repository `Directory.Build.props`:

```xml
<PackageReference Include="RaiGuard.Analyzer" Version="4.5.8">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

> 📖 **Comprehensive Adoption Guide**: See [INTEGRATION_GUIDE.md](INTEGRATION_GUIDE.md) for repository-wide protection via `Directory.Build.props`, VS Code C# Dev Kit live squiggles, `.editorconfig` severity overrides, and CI pipelines.

</details>

---

<details open>
<summary><b>Real-World Example: HeatRecord Weather App (Before & After)</b></summary>

The repository includes a complete reference application under [`samples/HeatRecord/`](samples/HeatRecord/) illustrating how naive code with common BCL anti-patterns is identified and transformed into clean, idiomatic RAIkeep platform code.

### 1. The Naive Source File (`Program.Naive.cs`)

Consider a developer or LLM implementing an offline weather caching routine using traditional .NET BCL primitives:

```csharp
using System;
using System.IO;

namespace HeatRecord
{
    public static class ProgramNaive
    {
        public static void Main(string[] args)
        {
            string city = args.Length > 0 ? args[0] : "San Diego";
            string citySlug = city.Replace(" ", "");

            // ⚠️ Anti-pattern 1: System.IO.Path.Combine (RAI003) & Directory.GetCurrentDirectory (RAI001)
            string weatherDir = Path.Combine(Directory.GetCurrentDirectory(), "Weather");
            string dailyHeatDir = Path.Combine(weatherDir, "DailyHeat-" + citySlug);

            // ⚠️ Anti-pattern 2: System.IO.Directory.CreateDirectory (RAI001)
            Directory.CreateDirectory(dailyHeatDir);

            // ⚠️ Anti-pattern 3: System.IO.File.WriteAllText for offline cache (RAI002)
            string cacheFile = Path.Combine(weatherDir, "DailyHeat-" + citySlug + "-OfflineSample.json");
            File.WriteAllText(cacheFile, "{\"city\":\"" + city + "\",\"status\":\"offline-cache\",\"oct08\":{\"2024\":31.5}}");

            // ⚠️ Anti-pattern 4: System.IO.File.ReadAllLines to read cached data (RAI002)
            string[] cachedLines = File.ReadAllLines(cacheFile);
            Console.WriteLine($"Read {cachedLines.Length} lines from cached dataset.");

            // ⚠️ Anti-patterns 1, 2, 3 again: Manifest file creation
            string manifestPath = Path.Combine(weatherDir, "DailyHeat-" + citySlug + "-Manifest.txt");
            File.WriteAllText(manifestPath, "DailyHeat Ingestion Manifest\nCity: " + city + "\nStatus: Complete");
            string[] manifestLines = File.ReadAllLines(manifestPath);
            Console.WriteLine($"Manifest lines: {manifestLines.Length}");
        }
    }
}
```

### 2. In-Editor Experience (VS Code / Visual Studio / Rider)

When opening `Program.Naive.cs` in an IDE equipped with `RaiGuard.Analyzer`:
- Real-time yellow/red squiggles highlight prohibited calls as you type.
- Hovering over `Path.Combine` displays:
  > `warning RAI003: 'System.IO.Path.Combine' is prohibited; use 'RaiPath' or 'RaiRelPath' with operator '/' instead`
- Pressing `Ctrl+.` / `Cmd+.` triggers the automated Roslyn Quick-Fix action.

### 3. CLI Audit (`rail check`)

Run `rail check` across the file or directory:

```bash
rail check samples/HeatRecord/Program.Naive.cs
```

Terminal output:
```text
 ──────────────────────────────────────────────────────────────────────
rail - RaiGuard Roslyn Guardrails & Analyzer for the RAIkeep Framework
 ──────────────────────────────────────────────────────────────────────
 Program.Naive.cs(15,38): warning RAI003: 'System.IO.Path.Combine' is prohibited; use 'RaiPath' or 'RaiRelPath' with operator '/' instead
 Program.Naive.cs(15,56): warning RAI001: 'System.IO.Directory.GetCurrentDirectory' is prohibited; use 'RaiPath' methods instead
 Program.Naive.cs(16,40): warning RAI003: 'System.IO.Path.Combine' is prohibited; use 'RaiPath' or 'RaiRelPath' with operator '/' instead
 Program.Naive.cs(19,23): warning RAI001: 'System.IO.Directory.CreateDirectory' is prohibited; use 'RaiPath' methods instead
 Program.Naive.cs(22,37): warning RAI003: 'System.IO.Path.Combine' is prohibited; use 'RaiPath' or 'RaiRelPath' with operator '/' instead
 Program.Naive.cs(23,18): warning RAI002: 'System.IO.File.WriteAllText' is prohibited; use 'RaiFile' or 'TextFile' methods instead
 Program.Naive.cs(26,41): warning RAI002: 'System.IO.File.ReadAllLines' is prohibited; use 'RaiFile' or 'TextFile' methods instead
 Program.Naive.cs(30,40): warning RAI003: 'System.IO.Path.Combine' is prohibited; use 'RaiPath' or 'RaiRelPath' with operator '/' instead
 Program.Naive.cs(31,18): warning RAI002: 'System.IO.File.WriteAllText' is prohibited; use 'RaiFile' or 'TextFile' methods instead
 Program.Naive.cs(32,43): warning RAI002: 'System.IO.File.ReadAllLines' is prohibited; use 'RaiFile' or 'TextFile' methods instead
 Program.Naive.cs(6,1): warning RAI010: Indentation must use tabs ('\t'), not spaces.

 Found 35 violation(s) across 1 file(s).
```

### 4. Automated Refactoring (`rail fix`)

Execute `rail fix` to let the AST rewriter automatically refactor the file:

```bash
rail fix samples/HeatRecord/Program.Naive.cs
```

Output:
```text
 ──────────────────────────────────────────────────────────────────────
rail - RaiGuard Roslyn Guardrails & Analyzer for the RAIkeep Framework
 ──────────────────────────────────────────────────────────────────────
 Fixed 8 violation(s) in samples/HeatRecord/Program.Naive.cs
 Applied 8 automatic code fix(es).
```

### 5. The Refactored File (`Program.Naive.cs` after `rail fix`)

Notice how `rail fix`:
1. Injects `using OsLib;` automatically.
2. Replaces `Path.Combine(a, b)` with `new RaiPath(a) / b` operator syntax.
3. Replaces `File.WriteAllText(path, text)` with `new TextFile(path, text)`.
4. Replaces `File.ReadAllLines(path)` with `new TextFile(path).Read()`.

```csharp
using System;
using System.IO;
using OsLib;

namespace HeatRecord
{
	public static class ProgramNaive
	{
		public static void Main(string[] args)
		{
			string city = args.Length > 0 ? args[0] : "San Diego";
			string citySlug = city.Replace(" ", "");

			// ✅ Automated fix: RaiPath with operator /
			string weatherDir = new RaiPath(Directory.GetCurrentDirectory()) / "Weather";
			string dailyHeatDir = new RaiPath(weatherDir) / ("DailyHeat-" + citySlug);

			Directory.CreateDirectory(dailyHeatDir);

			// ✅ Automated fix: TextFile constructor
			string cacheFile = new RaiPath(weatherDir) / ("DailyHeat-" + citySlug + "-OfflineSample.json");
			new TextFile(cacheFile, "{\"city\":\"" + city + "\",\"status\":\"offline-cache\",\"oct08\":{\"2024\":31.5}}");

			// ✅ Automated fix: TextFile.Read()
			string[] cachedLines = new TextFile(cacheFile).Read();
			Console.WriteLine($"Read {cachedLines.Length} lines from cached dataset.");

			// ✅ Automated fix: TextFile constructor & Read()
			string manifestPath = new RaiPath(weatherDir) / ("DailyHeat-" + citySlug + "-Manifest.txt");
			new TextFile(manifestPath, "DailyHeat Ingestion Manifest\nCity: " + city + "\nStatus: Complete");
			string[] manifestLines = new TextFile(manifestPath).Read();
			Console.WriteLine($"Manifest lines: {manifestLines.Length}");
		}
	}
}
```

### 6. The Production Pattern (`samples/HeatRecord/Program.cs`)

For the full production design pattern featuring `pitDir.mkdir()`, dynamic multi-year sparse data modeling with `JsonPit`, and zero warnings, see:
- [`samples/HeatRecord/Program.cs`](samples/HeatRecord/Program.cs)
- [`samples/HeatRecord/TUTORIAL.md`](samples/HeatRecord/TUTORIAL.md)

</details>

---

<details>
<summary><b>CLI Commands & Reference (`rail`)</b></summary>

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

### Usage Examples

Check current directory:
```bash
rail check
```

Check a specific project or source file:
```bash
rail check src/MyProject/Services/Storage.cs
```

Check only a specific rule:
```bash
rail check . --rule RAI003
```

Simulate fixes without modifying disk:
```bash
rail fix --dry-run
```

Automatically apply fixes:
```bash
rail fix
```

Emit JSON diagnostics for CI integration:
```bash
rail check --json
```

</details>

---

<details>
<summary><b>Guardrail Rules & Platform Conventions</b></summary>

### Rule Catalog

| Rule ID | Severity | Description | Remedy |
|---------|----------|-------------|--------|
| `RAI001` | Warning | Direct usage of `System.IO.Directory` methods is prohibited | Use `RaiPath` methods (e.g. `pitDir.mkdir()`, `new RaiPath(path).EnumerateDirectories()`, `new RaiPath(path).EnumerateFiles()`) |
| `RAI002` | Warning | Direct usage of `System.IO.File` methods is prohibited | Use `RaiFile` or `TextFile` methods (e.g. `new RaiFile(path).Exists()`, `new RaiFile(path).rm()`, `new TextFile(path).Read()`, `new TextFile(path, content)`) |
| `RAI003` | Warning | Direct usage of `System.IO.Path` methods is prohibited | Use `RaiPath` or `RaiRelPath` with operator `/` (e.g. `root / "subDir"`) |
| `RAI010` | Warning | Tab indentation required and consecutive spaces prohibited | Indent code with tabs (`\t`) and use single spaces between tokens outside string and character literals |

### Idiomatic Platform Patterns

- **Path Composition (Operator `/`)**:
  ```csharp
  var myPath = new RaiPath("~") / ".config" / "app";
  var tenantDir = weatherRoot / $"DailyHeat-{city.Slug}";
  // Never use System.IO.Path.Combine
  ```
- **Directory Operations**:
  ```csharp
  tenantDir.mkdir();
  var subdirs = new RaiPath(root).EnumerateDirectories();
  var files = new RaiPath(root).EnumerateFiles("*.json");
  ```
- **Writing / Appending to Files**:
  ```csharp
  new TextFile(filePath, "line one\nline two"); // Writes atomically to disk
  ```
- **Reading Files**:
  ```csharp
  var lines = new TextFile(filePath).Read(); // Reads into List<string>
  ```
- **In-Memory File Editing & Saving**:
  ```csharp
  var tf = new TextFile(filePath).Read();
  tf.Delete(0);             // Marks Changed = true
  tf.DeleteAll().Save();    // Commits empty file to disk
  ```
- **File Existence & Removal**:
  ```csharp
  bool exists = new RaiFile(filePath).Exists();
  new RaiFile(filePath).rm(); // POSIX rm()
  ```

</details>

---

<details>
<summary><b>Solution Architecture & Codebase Structure</b></summary>

```text
RaiGuard/
├── rail/                                 # 1. The rail CLI tool (Global tool executable anywhere dotnet is installed)
│   ├── rail.csproj                       # ToolCommandName=rail, references all RAIkeep packages
│   └── Program.cs                        # CLI parsing, Nerd Font phenotype, and analysis/refactoring engine
│
├── rail.Tests/                           # 2. CLI End-to-End Tests (net10.0)
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
│   └── RaiGuard.Tests/                   # 6. Unit Tests for Analyzers & Code Fixes (net10.0)
│       ├── RaiGuard.Tests.csproj
│       ├── SystemIoDirectoryAnalyzerTests.cs
│       ├── SystemIoFileAnalyzerTests.cs
│       ├── SystemIoPathAnalyzerTests.cs
│       └── WhitespaceFormattingAnalyzerTests.cs
│
├── samples/
│   └── HeatRecord/                       # 7. Sample App & Tutorial (Sparse Pit + Guardrails)
│       ├── HeatRecord.csproj
│       ├── Program.cs                    # 100% clean idiomatic implementation
│       ├── Program.Naive.cs              # Educational honeypot with BCL anti-patterns
│       └── TUTORIAL.md                   # 7-Chapter Master Curriculum
│
├── RaiGuard.slnx                         # Solution (slnx format)
└── RaiGuard.sln                          # Solution (classic sln format)
```

</details>

---

<details>
<summary><b>Terminal Font & Visual Phenotype</b></summary>

> **Font note:** The `rail` help screen and console diagnostics use glyph icons from Nerd Fonts. Most
> Nerd Font-patched fonts render correctly in most terminal environments. Blink
> on iPadOS showed clipping and character-width problems with some choices; the
> tested solution was Blink's
> [Jet Brains Mono Nerd Font stylesheet](https://github.com/blinksh/patched-fonts/blob/main/Jet%20Brains%20Mono%20Nerd%20Font.css).
> See the RAIkeep
> [terminal font guide](https://github.com/Burkhardt/RAIkeep/blob/main/doc/TERMINAL_FONTS.md)
> for Blink, macOS, and Ubuntu setup.

</details>

---

<details>
<summary><b>Building from Source</b></summary>

To build the entire solution and run all test suites locally:

```bash
dotnet build RaiGuard.slnx
dotnet test RaiGuard.slnx
```

To pack local NuGet packages:

```bash
dotnet pack rail/rail.csproj -c Release -o ./artifacts/nuget
dotnet pack src/RaiGuard.Package/RaiGuard.Package.csproj -c Release -o ./artifacts/nuget
```

See [BuildFromSource.md](BuildFromSource.md) for full instructions.

</details>
