# HeatRecord: The Master 7-Chapter Curriculum

A comprehensive master tutorial demonstrating **JsonPit's** open-world sparse attribute model, cloud-substrate resolution with **Amafu**, and compile-time guardrail enforcement with **RaiGuard** and the **`rail`** CLI.

---

## Executive Summary

Modern cloud-synced applications require two architectural superpowers:
1. **Open-World Data Modeling**: The ability to record evolving multi-year temporal observations (such as 10 years of daily heat extremes) without rigid schema migrations or sparse SQL null columns.
2. **Framework Physics Enforcement**: Strict compile-time and analyzer guardrails that prevent LLMs and developers from backsliding into legacy Base Class Library (BCL) primitives (`System.IO.Path.Combine`, `Directory.CreateDirectory`, `File.WriteAllText`, space indentation).

This tutorial builds the **`HeatRecord`** sample application (`samples/HeatRecord`) to illustrate both superpowers across seven end-to-end chapters.

```text
HeatRecord/
├── HeatRecord.csproj         # .NET 10 console application referencing JsonPit & OsLib
├── Program.cs                # 100% idiomatic, tab-indented, multi-city climate ingestion engine
├── Program.Naive.cs          # Intentional honeypot fixture exhibiting BCL anti-patterns
└── TUTORIAL.md               # Master curriculum (this document)
```

---

## Chapter 1: Developer Prerequisites & Toolchain Setup

### 1.1 Repository Checkout

Clone the synchronized **RAIkeep** workspace containing `RaiGuard`, `JsonPit`, and `OsLib`:

```bash
gh repo clone Burkhardt/RAIkeep
cd RAIkeep/RaiGuard
```

Ensure your environment has the [.NET 10 SDK](https://dotnet.microsoft.com/) and Python 3.11+ installed:

```bash
dotnet --version   # 10.0.x
python3 --version  # 3.11+
```

### 1.2 Toolchain Installation

The RAIkeep ecosystem relies on specialized command-line tools for cloud management, pit seeding, inspection, and Roslyn linting:

| Tool | Purpose | Installation / Build |
|------|---------|----------------------|
| **`amafu`** | Cloud substrate detector and symlink reconciler | Pre-installed or built from `Amafu/` |
| **`pits`** | AfricaStage Pit Seeder & Quartet maintenance CLI | Pre-installed in `~/.local/bin/pits` |
| **`jpit`** | High-performance Python JsonPit inspection CLI | `pip install jsonpit` or homebrew binary |
| **`rail`** | RaiGuard opinionated Roslyn guardrails & refactoring CLI | `dotnet tool install -g RaiGuard` or `rail/rail.csproj` |

To install `rail` globally from local sources:

```bash
dotnet pack rail/rail.csproj -c Release -o ./artifacts/nuget
dotnet tool install --global --add-source ./artifacts/nuget RaiGuard
rail --version
```

### 1.3 Developer Experience: VS Code C# Dev Kit vs CLI

RaiGuard operates simultaneously at two layers of the developer feedback loop:
1. **In-Editor Real-Time Squiggles**: Through the VS Code **C# Dev Kit** (or OmniSharp), `RaiGuard.Core` analyzers run during active keystrokes, flagging prohibited API usage with red and yellow squiggles right inside the editor before code is ever committed.
2. **Pure .NET CLI & CI/CD**: In automated pipelines, pre-commit hooks, and terminal workflows, `dotnet build RaiGuard.slnx` and `rail check` inspect syntax trees and enforce non-negotiable repository standards.

---

## Chapter 2: Cloud Substrate Preparation with Amafu

### 2.1 The Problem with Hardcoded Cloud Paths

When syncing distributed data containers (such as JsonPits) across operating systems, hardcoded user paths cause immediate failures:
- macOS CloudStorage: `/Users/username/Library/CloudStorage/OneDrive-Company/`
- Windows OneDrive: `C:\Users\username\OneDrive\`
- Linux sync roots: `/home/username/.local/share/rclone/`

Furthermore, developers often work on machines with multiple active accounts (e.g., personal vs enterprise OneDrive).

### 2.2 Substrate Detection with `amafu detect`

**Amafu** probes the local operating system, discovers registered cloud storage sync engines, and maps their exact local sync directories:

```bash
amafu detect
```

Example Output:
```text
─── Amafu Cloud Substrate Detector ───
✔ Discovered OneDrive:   /Users/RSB/Library/CloudStorage/OneDrive/OneDriveData
✔ Discovered Dropbox:    /Users/RSB/Library/CloudStorage/Dropbox/DropboxData
✔ Discovered GoogleDrive: /Users/RSB/Library/CloudStorage/GoogleDrive-user@domain.com/My Drive/GDriveData
✔ Discovered ICloudDrive: /Users/RSB/Library/Mobile Documents/com~apple~CloudDocs/ICloudDriveData
```

### 2.3 Symlink Standardization with `amafu reconcile`

Run `amafu reconcile` to establish machine-agnostic symlinks under `~/.CloudStorage/`:

```bash
amafu reconcile
```

This creates canonical symlinks:
```text
~/.CloudStorage/
├── OneDrive    -> /Users/RSB/Library/CloudStorage/OneDrive/OneDriveData
├── Dropbox     -> /Users/RSB/Library/CloudStorage/Dropbox/DropboxData
├── GoogleDrive -> /Users/RSB/Library/CloudStorage/GoogleDrive-user@domain.com/...
└── ICloudDrive -> /Users/RSB/Library/Mobile Documents/com~apple~CloudDocs/...
```

### 2.4 Shortcut-First Resolution in OsLib

Applications written with `OsLib` resolve cloud paths via `new RaiPath("~") / ".CloudStorage" / provider / tenant`. This enables `HeatRecord` to discover `~/.CloudStorage/OneDrive/Weather` transparently on any operating system without hardcoding local directories.

---

## Chapter 3: The Naive Prototype (`Program.Naive.cs`)

When developers or LLMs are tasked with writing a weather fetch and caching tool, they almost invariably produce code relying on standard .NET BCL APIs.

Examine `samples/HeatRecord/Program.Naive.cs`:

```csharp
using System;
using System.IO;

namespace HeatRecord
{
    public static class ProgramNaive
    {
        public static void Main(string[] args)
        {
            // Naive weather fetch and caching workflow with BCL anti-patterns
            string city = args.Length > 0 ? args[0] : "San Diego";
            string citySlug = city.Replace(" ", "");

            // Anti-pattern 1: Using System.IO.Path.Combine (RAI003)
            string weatherDir = Path.Combine(Directory.GetCurrentDirectory(), "Weather");
            string dailyHeatDir = Path.Combine(weatherDir, "DailyHeat");

            // Anti-pattern 2: Using System.IO.Directory.CreateDirectory (RAI001)
            Directory.CreateDirectory(dailyHeatDir);

            // Anti-pattern 3: Using System.IO.File.WriteAllText for offline cache (RAI002)
            string cacheFile = Path.Combine(weatherDir, "DailyHeat-" + citySlug + "-OfflineSample.json");
            File.WriteAllText(cacheFile, "{\"city\":\"" + city + "\",\"status\":\"offline-cache\",\"oct08\":{\"2024\":31.5,\"2025\":33.2,\"2026\":34.8}}");

            // Anti-pattern 4: Using System.IO.File.ReadAllLines to read cached data (RAI002)
            string[] cachedLines = File.ReadAllLines(cacheFile);
            Console.WriteLine($"Read {cachedLines.Length} lines from cached dataset.");

            // Anti-pattern 3 & 4 again: Writing and reading manifest via File
            string manifestPath = Path.Combine(weatherDir, "DailyHeat" + "-Manifest.txt");
            File.WriteAllText(manifestPath, "DailyHeat Ingestion Manifest\nCity: " + city + "\nStatus: Complete");
            string[] manifestLines = File.ReadAllLines(manifestPath);
            Console.WriteLine($"Manifest lines: {manifestLines.Length}");
        }
    }
}
```

### Why are these considered anti-patterns in RAIkeep?

1. **`Path.Combine`**: Prone to path separator mismatches across Windows/POSIX and lacks operator overloading. In RAIkeep, `RaiPath` operator `/` provides safe path algebra.
2. **`Directory.CreateDirectory`**: Bypasses cloud sync collision guards and path normalization.
3. **`File.WriteAllText` / `File.ReadAllLines`**: Bypasses `TextFile`'s atomic POSIX-compliant writes, in-memory change tracking, and dirty-checking semantics.
4. **4-Space Indentation**: Violates repository-wide tab indentation invariants (`\t`), leading to git diff noise and formatting churn.

---

## Chapter 4: The Compiler Awakening (`rail check`)

Run `rail check` against `Program.Naive.cs`:

```bash
rail check samples/HeatRecord/Program.Naive.cs
```

### 4.1 Diagnostic Output

```text
 ───────────────────────────────────────────────────────────────────────
rail - RaiGuard Roslyn Guardrails & Analyzer for the RAIkeep Framework
 ───────────────────────────────────────────────────────────────────────
 Program.Naive.cs(15,38): warning RAI003: 'System.IO.Path.Combine' is prohibited; use 'RaiPath' or 'RaiRelPath' with operator '/' instead
 Program.Naive.cs(15,56): warning RAI001: 'System.IO.Directory.GetCurrentDirectory' is prohibited; use 'RaiPath' methods instead (e.g., RaiPath.EnumerateDirectories or RaiPath.EnumerateFiles)
 Program.Naive.cs(16,40): warning RAI003: 'System.IO.Path.Combine' is prohibited; use 'RaiPath' or 'RaiRelPath' with operator '/' instead
 Program.Naive.cs(19,23): warning RAI001: 'System.IO.Directory.CreateDirectory' is prohibited; use 'RaiPath' methods instead (e.g., RaiPath.EnumerateDirectories or RaiPath.EnumerateFiles)
 Program.Naive.cs(22,37): warning RAI003: 'System.IO.Path.Combine' is prohibited; use 'RaiPath' or 'RaiRelPath' with operator '/' instead
 Program.Naive.cs(23,18): warning RAI002: 'System.IO.File.WriteAllText' is prohibited; use 'RaiFile' or 'TextFile' methods instead (e.g., new RaiFile(path).Exists(), new RaiFile(path).rm(), new TextFile(path).Read(), new TextFile(path, content))
 Program.Naive.cs(26,41): warning RAI002: 'System.IO.File.ReadAllLines' is prohibited; use 'RaiFile' or 'TextFile' methods instead (e.g., new RaiFile(path).Exists(), new RaiFile(path).rm(), new TextFile(path).Read(), new TextFile(path, content))
 Program.Naive.cs(30,40): warning RAI003: 'System.IO.Path.Combine' is prohibited; use 'RaiPath' or 'RaiRelPath' with operator '/' instead
 Program.Naive.cs(31,18): warning RAI002: 'System.IO.File.WriteAllText' is prohibited; use 'RaiFile' or 'TextFile' methods instead (e.g., new RaiFile(path).Exists(), new RaiFile(path).rm(), new TextFile(path).Read(), new TextFile(path, content))
 Program.Naive.cs(32,43): warning RAI002: 'System.IO.File.ReadAllLines' is prohibited; use 'RaiFile' or 'TextFile' methods instead (e.g., new RaiFile(path).Exists(), new RaiFile(path).rm(), new TextFile(path).Read(), new TextFile(path, content))
 Program.Naive.cs(6,1): warning RAI010: Indentation must use tabs ('\t'), not spaces. Please reformat using an approved formatter.
... (space indentation warnings on lines 7-35)

 Found 35 violation(s) across 1 file(s).
```

### 4.2 In-Editor VS Code Feedback

Because `RaiGuard.Core` analyzers are referenced by the solution, opening `Program.Naive.cs` in VS Code highlights all 35 violations immediately with red and yellow squiggles. Hovering over `Path.Combine` displays:

> *warning RAI003: 'System.IO.Path.Combine' is prohibited; use 'RaiPath' or 'RaiRelPath' with operator '/' instead*

---

## Chapter 5: Automated AST Remediation (`rail fix`)

Instead of manually rewriting dozens of lines of code, `rail fix` leverages Roslyn's Abstract Syntax Tree (AST) rewriter to perform automated code migrations.

### 5.1 Dry-Run Simulation

Inspect prospective code fixes without touching files on disk:

```bash
rail fix samples/HeatRecord/Program.Naive.cs --dry-run
```

Output:
```text
 ───────────────────────────────────────────────────────────────────────
rail - RaiGuard Roslyn Guardrails & Analyzer for the RAIkeep Framework
 ───────────────────────────────────────────────────────────────────────
 [DryRun] Would fix 9 violation(s) in samples/HeatRecord/Program.Naive.cs
 Dry run complete. 9 fix(es) candidate(s) detected.
```

### 5.2 Executing the Automated Fix

Execute `rail fix`:

```bash
rail fix samples/HeatRecord/Program.Naive.cs
```

### 5.3 What `rail fix` Performs Behind the Scenes

1. **Path Syntax Rewriting**:
   - `Path.Combine(a, b)` $\rightarrow$ `new RaiPath(a) / b`
   - Nested combinations chain naturally: `new RaiPath(a) / b / c`
2. **File I/O Rewriting**:
   - `File.WriteAllText(path, text)` $\rightarrow$ `new TextFile(path, text)`
   - `File.ReadAllLines(path)` $\rightarrow$ `new TextFile(path).Read()`
3. **Using Directive Injection**:
   - Inspects the compilation unit and automatically injects `using OsLib;` if not already present.
4. **Indentation Normalization**:
   - Rewrites leading space indentation into tabs (`\t`).

### 5.4 Before vs After Comparison

```csharp
// BEFORE (Naive BCL anti-patterns)
string weatherDir = Path.Combine(Directory.GetCurrentDirectory(), "Weather");
string dailyHeatDir = Path.Combine(weatherDir, "DailyHeat");
string cacheFile = Path.Combine(weatherDir, "DailyHeat-" + citySlug + "-OfflineSample.json");
File.WriteAllText(cacheFile, "{...}");
string[] cachedLines = File.ReadAllLines(cacheFile);

// AFTER (Automated AST refactoring by rail fix)
using OsLib;
...
string weatherDir = new RaiPath(Directory.GetCurrentDirectory()) / "Weather";
string dailyHeatDir = new RaiPath(weatherDir) / ("DailyHeat");
string cacheFile = new RaiPath(weatherDir) / ("DailyHeat-" + citySlug + "-OfflineSample.json");
new TextFile(cacheFile, "{...}");
string[] cachedLines = new TextFile(cacheFile).Read();
```

---

## Chapter 6: One Coherent JsonPit Dataset (`Program.cs`)

`HeatRecord` is deliberately a small, readable teaching sample. Its storage design nevertheless follows the JsonPit model: observations that belong to one coherent dataset belong in **one pit**, with each observation represented by a `PitItem`.

The sample uses the `Weather` tenant and one `DailyHeat` pit. It does not create a pit per city.

### 6.1 Cities and Ingestion Scope

The sample knows three representative locations:

| City | Item-id prefix | Latitude | Longitude |
|------|----------------|----------|-----------|
| **San Diego** | `SanDiego` | `32.7157` | `-117.1611` |
| **Lisbon** | `Lisbon` | `38.7223` | `-9.1393` |
| **Johannesburg** | `Johannesburg` | `-26.2041` | `28.0473` |

With no city argument, the program ingests all three into `Weather/DailyHeat/`. A city argument is useful when demonstrating a focused update:

```bash
dotnet run --project samples/HeatRecord/HeatRecord.csproj -- Lisbon
```

### 6.2 One Pit, Many Items, One Batch Boundary

The program determines the current month and records its days across the current year plus the preceding nine years. It fetches or creates the small per-city observation grids, then opens `DailyHeat` once and adds every item before this process's final `Save()`.

That final save is the batch boundary for this sample process; it is not the only persistence lifecycle. `pits` and `jpit` each complete their own commit when their execution ends. When a pit is opened, its state is read through the cloud-synchronized storage shared by the participating machines.

Each item id combines city and calendar day, so items share one namespace without collisions:

```csharp
var item = new PitItem($"{city.Slug}-{dayId}")
{
	Note = $"Daily heat observation record for {dayId} in {city.Name}"
};

item["City"] = city.Name;
item["Latitude"] = city.Latitude;
item["Longitude"] = city.Longitude;
item["2024"] = 31.5;
item["Highest"] = 34.8;
item["Unit"] = "C";
pit.Add(item);
```

For example, `SanDiego-10-08`, `Lisbon-10-08`, and `Johannesburg-10-08` are separate items in the same pit. A future ingestion adds more items to the same dataset; it does not fragment the dataset into city-sized pits.

### 6.3 Open-World Attributes

`PitItem` inherits from `JObject`. Annual measurements are ordinary open-world properties, so a new year can be recorded without a database migration or a new DTO property:

```csharp
item["2027"] = temperature;
```

The city, coordinates, unit, yearly readings, and derived high all travel with the observation they describe. This keeps reads direct and avoids joins just to interpret a single daily record.

### 6.4 Resilient Data Source

The application queries the Open-Meteo Archive API for its instructional workload. If the service is unavailable, it writes and reads a small per-city offline cache through `TextFile`, then continues with a deterministic sample dataset. The cache is an input aid; `DailyHeat` remains the single JsonPit dataset.

---

## Chapter 7: Cross-CLI Verification

### 7.1 Run the Complete Sample

Run without a city argument to ingest the three example locations into one pit:

```bash
dotnet run --project samples/HeatRecord/HeatRecord.csproj
```

Example output (the calendar month and values vary):

```text
[HeatRecord] Cities: Johannesburg, Lisbon, San Diego
[HeatRecord] Target Pit: .../Weather/DailyHeat/
[HeatRecord] Ingesting month 10 across 2017..2026 (31 calendar days)
[HeatRecord] Johannesburg-10-08 -> 2024=..., 2025=..., 2026=..., Highest=..., Unit=C
[HeatRecord] Lisbon-10-08 -> 2024=..., 2025=..., 2026=..., Highest=..., Unit=C
[HeatRecord] SanDiego-10-08 -> 2024=31.5, 2025=33.2, 2026=34.8, Highest=34.8, Unit=C
[HeatRecord] Persisted 93 daily items to .../Weather/DailyHeat/DailyHeat.pit
```

### 7.2 Discover the Dataset

```bash
jpit list -r Weather
```

```text
Found 1 pit(s) in directory 'Weather/':
  DailyHeat
```

### 7.3 Read One Observation

```bash
jpit get DailyHeat SanDiego-10-08 -r Weather
```

```json
{
  "City": "San Diego",
  "Latitude": 32.7157,
  "Longitude": -117.1611,
  "2024": 31.5,
  "2025": 33.2,
  "2026": 34.8,
  "Highest": 34.8,
  "Unit": "C",
  "Id": "SanDiego-10-08"
}
```

`DailyHeat` stays separate from the WWWA quartet because `Weather` is its own tenant. Within that tenant, one pit holds the coherent body of temperature observations.

---

## Chapter 8: Verification Checklist

| Check | Command | Expected Result |
|-------|---------|-----------------|
| **Honeypot Diagnostics** | `rail check samples/HeatRecord/Program.Naive.cs` | 35 violations across `RAI001`, `RAI002`, `RAI003`, `RAI010` |
| **Clean Implementation** | `rail check samples/HeatRecord/Program.cs` | 0 violations (`All guardrails passed`) |
| **Group Set Discovery** | `rail groups` | Lists 6 overlapping group sets (`OsLib`, `CloudSync`, `JsonPit`, `CliCommand`, `RaiMedia`, `Formatting`) |
| **Solution Build** | `dotnet build RaiGuard.slnx` | `Build succeeded. 0 Warning(s) 0 Error(s)` |
| **Unit & CLI Tests** | `dotnet test RaiGuard.slnx` | `Passed! 55/55 tests` (34 `RaiGuard.Tests`, 21 `rail.Tests`) |

---

## Summary of Guardrail Rules (`RAI001`–`RAI017`)

| Rule | Groups | Description | Required Remediation |
|------|--------|-------------|----------------------|
| **`RAI001`** | `OsLib`, `CloudSync` | `System.IO.Directory` and `DirectoryInfo` are prohibited | Use `RaiPath` methods (`mkdir()`, `EnumerateDirectories()`, `EnumerateFiles()`, `rmdir()`) |
| **`RAI002`** | `OsLib`, `CloudSync` | `System.IO.File`, `FileInfo`, file streams, and `ZipFile` are prohibited | Use `RaiFile`, `TextFile`, `JsonFile`, `CsvFile`, or `TmpFile` (`new TextFile(path, content)`, `Read()`, `rm()`, `Zip()`, `Unzip()`) |
| **`RAI003`** | `OsLib` | `System.IO.Path` methods, method groups, and separator fields are prohibited | Use `RaiPath` / `RaiRelPath` operator `/` (`root / "subDir"`), `RaiFile.Name`, or `RaiFile.NameWithoutExtension` |
| **`RAI004`** | `OsLib`, `CloudSync` | `new RaiPath("...")` string literal missing trailing `/` strips last segment as filename | Terminate directory string literal with `/` (`new RaiPath("/tmp/dir/")`) or compose with operator `/` |
| **`RAI005`** | `OsLib` | String concatenation with `Os.DIR` or `"/"` on `RaiPath` produces `//` | Compose directories with `dir / "sub"` and construct files via `new RaiFile(dir, name, ext)` or `new TextFile(dir, name, ext)` |
| **`RAI006`** | `OsLib`, `JsonPit`, `CliCommand`, `RaiMedia` | Bypassing typed `OsLib` `CliCommand` wrappers via `Process` or raw `new RaiSystem(...)` | Use `PitsCommand`, `IorgCommand`, `RaidCommand`, `AmafuCommand`, or `CliCommand` |
| **`RAI007`** | `OsLib`, `CloudSync` | Bypassing sacred `Os.Config` via `Environment.*` or `AppDomain` | Read configuration from `Os.Config`, `Os.UserHomeDir`, `Os.TempDir`, or `Os.AppRoot` |
| **`RAI008`** | `OsLib`, `CloudSync`, `JsonPit`, `RaiMedia` | Cloud-storage in-place write invariant (`CR022`) violated | Mutate and call `.Save()` or `dest.cp(source)` in-place; for temp staging use `dest.cp(tmp); tmp.rm();` |
| **`RAI009`** | `OsLib` | Inverted `OsLib` `cp` / `mv` receiver order (`source.cp(dest)`) | Call `dest.cp(source)` or `dest.mv(source)` where the **destination** `RaiFile` is `this` |
| **`RAI010`** | `Formatting` | Space indentation and consecutive spaces prohibited | Indent exclusively with tabs (`\t`), single space between tokens outside strings (`rail fix --rule RAI010`) |
| **`RAI011`** | `JsonPit`, `CloudSync` | `Pit` full-snapshot read-modify-write (`CR040`) overwrites concurrent cloud fields | Construct a sparse delta `var delta = new PitItem(id); delta.ExtendWith(patch); pit.Add(delta);` or mutate live properties on `pit.Get(id)` |
| **`RAI012`** | `JsonPit` | Direct mutation of protected `PitItem` lifecycle attributes (`Id`, `Modified`, `Deleted`) | Use `new PitItem(id)`, `item.Delete()`, `item.Undel()`, and strip `Id`/`Modified`/`Deleted` from external `ExtendWith` payloads |
| **`RAI013`** | `JsonPit`, `CloudSync`, `CliCommand` | `pit.PitDir.Exists()` triggers `mkdir()` on disk; `pit.Reload()` calls `Save()` | Use `pit.JsonFile.Path.Exists()` for side-effect-free checks and `pit.Load()` to refresh external seeds |
| **`RAI014`** | `JsonPit`, `CloudSync` | Undisposed `Pit` instance leaks `ActiveCanonicalPitPaths` reservation (`CR033`) | Wrap `new Pit(...)` in `using var pit = ...` or dispose `_pit` in `IDisposable.Dispose()` |
| **`RAI015`** | `JsonPit` | Unexpanded `{...}`/`<...>` placeholders in `PitItem` IDs (`CR049`) or unsafe `JObject.Parse` | Validate IDs before `new PitItem(id)` and parse JSON via `JsonFile` or `JsonReader` with `DateParseHandling.None` |
| **`RAI016`** | `RaiMedia` | Obsolete `RaiImage.WordCase` (`CR019`), non-alphanumeric `DiagramBuilder` `baseItemId` (`CR025`), or non-`Structured` `ImageNamingConvention` | Use `RaiUtils.WordCase`, alphanumeric `baseItemId` without `_UCD`/`_CD` suffixes, and `ImageNamingConvention.Structured` |
| **`RAI017`** | `RaiMedia` | Ad-hoc `/img/` media URL string composition or concatenation | Use canonical `MediaUrls.Video(...)`, `MediaUrls.Poster(...)`, or `MediaUrls.Image(...)` instead of manual formatting |
