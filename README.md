# RaiGuard

Opinionated Roslyn analyzer, code-fix provider, and `rail` command-line tool enforcing framework physics and conventions across RAIkeep solutions.

RaiGuard prevents LLMs and developers from backsliding into standard .NET Base Class Library (BCL) boilerplate (such as `System.IO.Directory`, `System.IO.File`, `Path.Combine`, or space indentation) when custom RAIkeep platform abstractions (`RaiPath`, `TextFile`, `JsonPit`, tabs) are required.

It operates as two complementary deliverables:
1. **[`RaiGuard.Analyzer`](https://www.nuget.org/packages/RaiGuard.Analyzer)**: Roslyn analyzer and code-fix provider invoked during `dotnet build`, IDE keystrokes (VS Code C# Dev Kit), and CI workflows to emit live warnings and automated quick fixes.
2. **[`RaiGuard`](https://www.nuget.org/packages/RaiGuard)**: Cross-platform .NET Global Tool CLI (`ToolCommandName=rail`) running anywhere .NET is installed to audit, verify, and automatically refactor source files across repositories.

## Latest Version 4.5.10

Latest release notes: [RaiGuard 4.5.10 release notes](https://github.com/Burkhardt/RAIkeep/blob/main/doc/releases/v4.x/RaiGuard_RELEASE_NOTES_4.x.md#4510).

## Previous Versions

For release notes and changelogs of earlier releases, see the [RaiGuard 4.x Release Notes](https://github.com/Burkhardt/RAIkeep/blob/main/doc/releases/v4.x/RaiGuard_RELEASE_NOTES_4.x.md).


---

## Installation

### .NET global tool

```bash
dotnet tool install --global RaiGuard
```

To update:

```bash
dotnet tool update --global RaiGuard
```

*(Requires `~/.dotnet/tools` on your PATH.)*

### .NET tool in `/usr/local/bin` (Recommended for macOS & Linux)

```bash
sudo dotnet tool install RaiGuard --tool-path /usr/local/bin
```

To update:

```bash
sudo dotnet tool update RaiGuard --tool-path /usr/local/bin
```

`sudo` is needed to write to `/usr/local/bin` during installation and updates. Running `rail` does not require it.

### Roslyn analyzer package

To enforce guardrails during `dotnet build` and in IDEs, add the analyzer package to your `.csproj` or repository `Directory.Build.props`:

```xml
<PackageReference Include="RaiGuard.Analyzer" Version="4.5.10">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

See [INTEGRATION_GUIDE.md](https://github.com/Burkhardt/RaiGuard/blob/main/INTEGRATION_GUIDE.md) for repository-wide protection, IDE diagnostics, and CI pipelines.

---

## Real-World Example: HeatRecord Weather App (Before & After)

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

---

## CLI Commands & Reference (`rail`)

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

Check only a specific rule or rule group set:
```bash
rail check . --rule RAI003
rail check . --group CloudSync
rail check . --group JsonPit,CliCommand
```

List all rule group sets and their member `RAIxxx` rules:
```bash
rail groups
rail rules --group JsonPit
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

---

## Guardrail Rules & Platform Conventions

### Rule Group Sets

Rules are numbered continuously (`RAI001`–`RAI016`) and organized into overlapping **Rule Group Sets** (`rail groups`). A rule may belong to multiple groups, and group members do not require consecutive numbers:

| Group Set | Member Rules (`RAIxxx`) | Scope |
|-----------|-------------------------|-------|
| **`OsLib`** | `{ RAI001, RAI002, RAI003, RAI004, RAI005, RAI006, RAI007, RAI008, RAI009 }` | Path algebra (`RaiPath`/`RaiRelPath`), file/directory objects (`RaiFile`/`TextFile`), typed `CliCommand` execution, and sacred `Os.Config` |
| **`CloudSync`** | `{ RAI001, RAI002, RAI004, RAI007, RAI008, RAI011, RAI013, RAI014 }` | Cloud-drive replication invariants (`CR022` in-place writes, `CR040` sparse deltas, `CR033` canonical path reservation, and reload safety) |
| **`JsonPit`** | `{ RAI006, RAI008, RAI011, RAI012, RAI013, RAI014, RAI015 }` | Open-world sparse `Pit` and `PitItem` persistence, temporal merge, protected lifecycle attributes, `PitsCommand` gates, and `DateParseHandling.None` |
| **`CliCommand`** | `{ RAI006, RAI013 }` | Typed `OsLib` CLI command execution (`PitsCommand`, `IorgCommand`, `RaidCommand`, `AmafuCommand`) and external CLI seed reload safety |
| **`RaiMedia`** | `{ RAI006, RAI008, RAI016 }` | `RaiUtils.WordCase` (`CR019`), `RaiImage` (`ImageNamingConvention.Structured`, `IorgCommand`), and `RaiDiagram` (`DiagramBuilder` `baseItemId` per `CR025`, `RaidCommand`) |
| **`Formatting`** | `{ RAI010 }` | Tab indentation (`\t`) and single-space token separation invariants |

### Rule Catalog (`RAI001`–`RAI016`)

| Rule ID | Groups | Severity | Description | Remedy |
|---------|--------|----------|-------------|--------|
| `RAI001` | `OsLib`, `CloudSync` | Warning | Direct usage of `System.IO.Directory` or `DirectoryInfo` is prohibited | Use `RaiPath` methods (`pitDir.mkdir()`, `new RaiPath(path).EnumerateDirectories()`, `new RaiPath(path).EnumerateFiles()`) |
| `RAI002` | `OsLib`, `CloudSync` | Warning | Direct usage of `System.IO.File`, `FileInfo`, file streams, or `ZipFile` is prohibited | Use `RaiFile`, `TextFile`, `JsonFile`, `CsvFile`, or `TmpFile` (`Exists()`, `rm()`, `Read()`, `ReadAllText()`, `Zip()`, `Unzip()`) |
| `RAI003` | `OsLib` | Warning | Direct usage of `System.IO.Path` methods, method groups, or separator fields is prohibited | Use `RaiPath` / `RaiRelPath` with operator `/` (`root / "subDir"`), `RaiFile.Name`, or `RaiFile.NameWithoutExtension` |
| `RAI004` | `OsLib`, `CloudSync` | Warning | `new RaiPath("...")` string literal missing trailing `/` strips last segment as filename | Terminate directory literal with `/` (`new RaiPath("/tmp/dir/")`) or compose segments with `/` (`new RaiPath("/tmp/") / "dir"`) |
| `RAI005` | `OsLib` | Warning | String concatenation with `Os.DIR` or `"/"` on `RaiPath` produces `//` and bypasses typed constructors | Compose directories with `dir / "sub"` and construct files via `new RaiFile(dir, name, ext)` or `new TextFile(dir, name, ext)` |
| `RAI006` | `OsLib`, `JsonPit`, `CliCommand`, `RaiMedia` | Warning | Bypassing typed `OsLib` `CliCommand` wrappers via `System.Diagnostics.Process` or raw `new RaiSystem("pits"\|"iorg"\|"raid"\|"amafu", ...)` | Use `PitsCommand`, `IorgCommand`, `RaidCommand`, `AmafuCommand`, or `CliCommand` (preserving `PitsCommand.TargetGates` semaphore serialization) |
| `RAI007` | `OsLib`, `CloudSync` | Warning | Bypassing sacred `Os.Config` via `Environment.*`, `AppDomain.CurrentDomain.BaseDirectory`, or `Directory.GetCurrentDirectory()` | Read configuration from `Os.Config`, `Os.UserHomeDir`, `Os.TempDir`, or `Os.AppRoot` |
| `RAI008` | `OsLib`, `CloudSync`, `JsonPit`, `RaiMedia` | Warning | Cloud-storage in-place write invariant (`CR022`) violated (`rm()` before `Save()`/`cp()` or `.mv(tmp)` from temp storage) | Mutate and call `.Save()` or `dest.cp(source)` in-place; for temp staging use `dest.cp(tmp); tmp.rm();` |
| `RAI009` | `OsLib` | Warning | Inverted `OsLib` `cp` / `mv` receiver order (`source.cp(dest)` instead of `dest.cp(source)`) | Call `dest.cp(source)` or `dest.mv(source)` where the **destination** `RaiFile` is `this` |
| `RAI010` | `Formatting` | Warning | Tab indentation required and consecutive spaces prohibited | Indent code with tabs (`\t`) and use single spaces between tokens outside string and character literals (`rail fix --rule RAI010`) |
| `RAI011` | `JsonPit`, `CloudSync` | Warning | `Pit` full-snapshot read-modify-write (`CR040`) overwrites concurrent cloud-synced fields | Construct a sparse delta `var delta = new PitItem(id); delta.ExtendWith(patch); pit.Add(delta);` or mutate live properties on `pit.Get(id)` |
| `RAI012` | `JsonPit` | Warning | Direct mutation of protected `PitItem` lifecycle attributes (`Id`, `Modified`, `Deleted`) | Use `new PitItem(id)`, `item.Delete()`, `item.Undel()`, and strip `Id`/`Modified`/`Deleted` from external `ExtendWith` payloads |
| `RAI013` | `JsonPit`, `CloudSync`, `CliCommand` | Warning | `pit.PitDir.Exists()` triggers `mkdir()` on disk; `pit.Reload()` calls `Save()` and overwrites external seeds | Use `pit.JsonFile.Path.Exists()` for side-effect-free checks and `pit.Load()` to refresh external seeds |
| `RAI014` | `JsonPit`, `CloudSync` | Warning | Undisposed `Pit` instance leaks `ActiveCanonicalPitPaths` reservation (`CR033`) | Wrap `new Pit(...)` in `using var pit = ...` or dispose the `_pit` field in `IDisposable.Dispose()` |
| `RAI015` | `JsonPit` | Warning | Unexpanded `{...}`/`<...>` template placeholders in `PitItem` IDs (`CR049`) or `JObject.Parse` without `DateParseHandling.None` | Validate IDs before `new PitItem(id)` and parse JSON via `JsonFile` or `JsonReader` with `DateParseHandling.None` |
| `RAI016` | `RaiMedia` | Warning | Obsolete `RaiImage.WordCase` (`CR019`), non-alphanumeric `DiagramBuilder` `baseItemId` (`CR025`), or non-`Structured` `ImageNamingConvention` | Use `RaiUtils.WordCase`, alphanumeric `baseItemId` without `_UCD`/`_CD` suffixes, and `ImageNamingConvention.Structured` |

For detailed *Before vs. After* code comparisons, architectural rationale (`CR019`, `CR022`, `CR025`, `CR033`, `CR040`, `CR049`), and the cross-repository empirical audit report, see [`ARCHITECTURE_RULES.md`](ARCHITECTURE_RULES.md).

### Idiomatic Platform Patterns

- **Path Composition (Operator `/`)**:
  ```csharp
  var myPath = new RaiPath("~/") / ".config" / "app";
  var tenantDir = weatherRoot / $"DailyHeat-{city.Slug}";
  // Never use System.IO.Path.Combine or omit the trailing slash on new RaiPath("...")
  ```
- **Directory Operations**:
  ```csharp
  tenantDir.mkdir();
  var subdirs = tenantDir.EnumerateDirectories();
  var files = tenantDir.EnumerateFiles("*.json");
  ```
- **Writing / Appending to Files (Cloud-Safe In-Place)**:
  ```csharp
  new TextFile(filePath, "line one\nline two"); // Writes in-place to preserve cloud file ID
  ```
- **Sparse Pit Delta Writes (`CR040`)**:
  ```csharp
  using var pit = new Pit(pitPath, readOnly: false, autoload: true);
  var delta = new PitItem(id);
  delta.ExtendWith(patch);
  pit.Add(delta);
  pit.Save();
  ```
- **Reading Files**:
  ```csharp
  var lines = new TextFile(filePath).Read(); // Reads into List<string>
  ```
- **In-Memory File Editing & Saving**:
  ```csharp
  var tf = new TextFile(filePath).Read();
  tf.Delete(0);             // Marks Changed = true
  tf.DeleteAll().Save();    // Commits empty file to disk in-place
  ```
- **File Existence & Removal**:
  ```csharp
  bool exists = new RaiFile(filePath).Exists();
  new RaiFile(filePath).rm(); // POSIX rm() — only for true deletion, never before Save()
  ```

---

## Solution Architecture & Codebase Structure

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
│   │   ├── Rules/RaiKeepArchitectureAnalyzer.cs
│   │   └── Helpers/SemanticExtensions.cs
│   │
│   ├── RaiGuard.CodeFixes/               # 4. Automated Code Fix Providers (netstandard2.0)
│   │   ├── RaiGuard.CodeFixes.csproj
│   │   ├── Fixes/SystemIoDirectoryCodeFix.cs
│   │   ├── Fixes/SystemIoFileCodeFix.cs
│   │   ├── Fixes/SystemIoPathCodeFix.cs
│   │   └── Fixes/RaiKeepArchitectureCodeFix.cs
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
│       ├── WhitespaceFormattingAnalyzerTests.cs
│       └── RaiKeepArchitectureAnalyzerTests.cs
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

---

## Terminal Font & Visual Phenotype

> **Font note:** The `rail` help screen and console diagnostics use glyph icons from Nerd Fonts. Most
> Nerd Font-patched fonts render correctly in most terminal environments. Blink
> on iPadOS showed clipping and character-width problems with some choices; the
> tested solution was Blink's
> [Jet Brains Mono Nerd Font stylesheet](https://github.com/blinksh/patched-fonts/blob/main/Jet%20Brains%20Mono%20Nerd%20Font.css).
> See the RAIkeep
> [terminal font guide](https://github.com/Burkhardt/RAIkeep/blob/main/doc/TERMINAL_FONTS.md)
> for Blink, macOS, and Ubuntu setup.

---

## Building from Source

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
