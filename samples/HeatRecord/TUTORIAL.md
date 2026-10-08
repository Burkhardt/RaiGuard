# HeatRecord: The Master 7-Chapter Curriculum

A comprehensive master tutorial demonstrating **JsonPit's** open-world sparse attribute model, cloud-substrate resolution with **Amafu**, and compile-time guardrail enforcement with **RaiGuard** and the **`rail`** CLI.

---

## Executive Summary

Modern cloud-synced applications require two architectural superpowers:
1. **Open-World Data Modeling**: The ability to record evolving multi-year temporal observations (such as 10 years of daily heat extremes) without rigid schema migrations or sparse SQL null columns.
2. **Framework Physics Enforcement**: Strict compile-time and linter guardrails that prevent LLMs and developers from backsliding into legacy Base Class Library (BCL) primitives (`System.IO.Path.Combine`, `Directory.CreateDirectory`, `File.WriteAllText`, space indentation).

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
| **`rail`** | RaiGuard opinionated Roslyn guardrails & refactoring CLI | `dotnet tool install -g RaiGuard.Cli` or `rail/rail.csproj` |

To install `rail` globally from local sources:

```bash
dotnet pack rail/rail.csproj -c Release -o ./artifacts/nuget
dotnet tool install --global --add-source ./artifacts/nuget RaiGuard.Cli
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
            string dailyHeatDir = Path.Combine(weatherDir, "DailyHeat-" + citySlug);

            // Anti-pattern 2: Using System.IO.Directory.CreateDirectory (RAI001)
            Directory.CreateDirectory(dailyHeatDir);

            // Anti-pattern 3: Using System.IO.File.WriteAllText for offline cache (RAI002)
            string cacheFile = Path.Combine(weatherDir, "DailyHeat-" + citySlug + "-OfflineSample.json");
            File.WriteAllText(cacheFile, "{\"city\":\"" + city + "\",\"status\":\"offline-cache\",\"oct08\":{\"2024\":31.5,\"2025\":33.2,\"2026\":34.8}}");

            // Anti-pattern 4: Using System.IO.File.ReadAllLines to read cached data (RAI002)
            string[] cachedLines = File.ReadAllLines(cacheFile);
            Console.WriteLine($"Read {cachedLines.Length} lines from cached dataset.");

            // Anti-pattern 3 & 4 again: Writing and reading manifest via File
            string manifestPath = Path.Combine(weatherDir, "DailyHeat-" + citySlug + "-Manifest.txt");
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
 ──────────────────────────────────────────
rail - RaiGuard Roslyn Guardrails & Linter
 ──────────────────────────────────────────
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
 ──────────────────────────────────────────
rail - RaiGuard Roslyn Guardrails & Linter
 ──────────────────────────────────────────
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
string dailyHeatDir = Path.Combine(weatherDir, "DailyHeat-" + citySlug);
string cacheFile = Path.Combine(weatherDir, "DailyHeat-" + citySlug + "-OfflineSample.json");
File.WriteAllText(cacheFile, "{...}");
string[] cachedLines = File.ReadAllLines(cacheFile);

// AFTER (Automated AST refactoring by rail fix)
using OsLib;
...
string weatherDir = new RaiPath(Directory.GetCurrentDirectory()) / "Weather";
string dailyHeatDir = new RaiPath(weatherDir) / ("DailyHeat-" + citySlug);
string cacheFile = new RaiPath(weatherDir) / ("DailyHeat-" + citySlug + "-OfflineSample.json");
new TextFile(cacheFile, "{...}");
string[] cachedLines = new TextFile(cacheFile).Read();
```

---

## Chapter 6: The JsonPit Multi-Year Architecture (`Program.cs`)

Now let's examine the production-grade, 100% idiomatic `Program.cs`.

### 6.1 Multi-City Support & Coordinates

`HeatRecord` accepts an optional city parameter supporting three representative climatic regions:

| City | Slug | Latitude | Longitude | Climate Zone |
|------|------|----------|-----------|--------------|
| **San Diego** | `SanDiego` | `32.7157` | `-117.1611` | Mediterranean Coastal (Warm) |
| **Lisbon** | `Lisbon` | `38.7223` | `-9.1393` | Atlantic Mediterranean |
| **Johannesburg** | `Johannesburg` | `-26.2041` | `28.0473` | Subtropical Highland (Southern Hemisphere) |

Default city: **San Diego**.

### 6.2 Timeless Temporal Scope

Rather than hardcoding static dates, `HeatRecord` evaluates the current calendar month dynamically:
- Ingests all days of the current month (e.g. October 1 to 31).
- Spans 10 continuous years: the current year and the 9 preceding years (e.g. `2017..2026`).

### 6.3 Resilient Open-Meteo Integration with Offline Fallback

The application queries the free **Open-Meteo Archive API**:
```text
https://archive-api.open-meteo.com/v1/archive?latitude=32.7157&longitude=-117.1611&start_date=2020-10-01&end_date=2025-10-05&daily=temperature_2m_max&timezone=auto
```

If the workstation is offline, DNS fails, or the API is unreachable, `HeatRecord` catches the exception and falls back to a bundled offline dataset using `TextFile`:

```csharp
var fallbackFile = weatherRoot / $"DailyHeat-{city.Slug}-OfflineSample.json";
if (!fallbackFile.Exists())
{
	var sampleJson = $"{{\"city\":\"{city.Name}\",\"month\":{month},\"baseTemp\":{baseTemp}}}";
	new TextFile(fallbackFile.FullPath, sampleJson);
}
var fallbackLines = new TextFile(fallbackFile.FullPath).Read();
```

### 6.4 Open-World Sparse Attribute Model in JsonPit

In traditional relational databases or static C# DTOs, recording temperatures across years requires:
```csharp
public class DailyRecord
{
    public double Temp2017 { get; set; }
    public double Temp2018 { get; set; }
    // When 2027 arrives: DB migration required! Code redeploy required!
}
```

In **JsonPit**, `PitItem` inherits directly from `JObject`:
- **Item ID**: Represents the calendar day within the month: `MM-DD` (e.g. `10-08`).
- **Dynamic Annual Attributes**: Years are open-world dictionary keys:
  ```csharp
  item["2017"] = 25.5;
  item["2024"] = 31.5;
  item["2025"] = 33.2;
  item["2026"] = 34.8;
  ```
- **Derived Metric**:
  ```csharp
  item["Highest"] = 34.8;
  ```
- **Unit Metadata**:
  ```csharp
  item["Unit"] = "C";
  ```

When a new year (2027) arrives, `item["2027"] = temp` is recorded dynamically without altering any schemas, tables, or existing records.

---

## Chapter 7: Cross-CLI Verification & Parity

### 7.1 Running the Ingestion Engine

Run `HeatRecord` for San Diego:

```bash
dotnet run --project samples/HeatRecord/HeatRecord.csproj -- "San Diego"
```

Console Output:
```text
[HeatRecord] City: San Diego (Lat: 32.7157, Lon: -117.1611)
[HeatRecord] Target Pit: .../Weather/DailyHeat-SanDiego/
[HeatRecord] Ingesting month 10 across 2017..2026 (31 calendar days)
[HeatRecord] Successfully queried Open-Meteo Archive API.
[HeatRecord] Loaded bundled offline dataset via TextFile (1 lines).
[HeatRecord] Day 10-01 -> 2024=28, 2025=29.1, 2026=29.1, Highest=29.1, Unit=C
[HeatRecord] Day 10-08 -> 2024=31.5, 2025=33.2, 2026=34.8, Highest=34.8, Unit=C
[HeatRecord] Day 10-31 -> 2024=27.3, 2025=28.4, 2026=28.4, Highest=28.4, Unit=C
[HeatRecord] Persisted 31 daily items to .../Weather/DailyHeat-SanDiego/DailyHeat-SanDiego.pit
[HeatRecord] Manifest verified via TextFile (12 lines).
```

### 7.2 Discovering Tenant Roots with `pits`

Query the `pits` CLI to verify the canonical tenant root location:

```bash
pits -h -r Weather
```

Output:
```text
 ────────────────────────────
 AfricaStage Pit Seeder CLI
 ────────────────────────────
-r, --pitroot    /Users/RSB/Projects/GitHub/RAIkeep/Weather/
  Person         /Users/RSB/Projects/GitHub/RAIkeep/Weather/Person/Person.pit
  Object         /Users/RSB/Projects/GitHub/RAIkeep/Weather/Object/Object.pit
  Place          /Users/RSB/Projects/GitHub/RAIkeep/Weather/Place/Place.pit
  Activity       /Users/RSB/Projects/GitHub/RAIkeep/Weather/Activity/Activity.pit
```

### 7.3 Discovering Pits with `jpit list`

Use `jpit list` to inspect active pits under the `Weather` tenant root:

```bash
jpit list -r Weather
```

Output:
```text
Found 1 pit(s) in directory 'Weather/':
  DailyHeat-SanDiego
```

*(When Lisbon and Johannesburg are ingested, all three pits appear cleanly under `Weather/`)*

### 7.4 Inspecting Landmark Item `10-08` with `jpit get`

Query the exact calendar day `10-08` from `DailyHeat-SanDiego`:

```bash
jpit get DailyHeat-SanDiego 10-08 -r Weather
```

Output:
```json
{
  "2017": 25.5,
  "2018": 25.8,
  "2019": 26.1,
  "2020": 26.4,
  "2021": 26.7,
  "2022": 27.0,
  "2023": 27.3,
  "2024": 31.5,
  "2025": 33.2,
  "2026": 34.8,
  "Highest": 34.8,
  "Unit": "C",
  "Id": "10-08",
  "Modified": "2026-10-08T17:19:39.0068120Z",
  "Deleted": false,
  "Note": "Daily heat observation record for 10-08 in San Diego"
}
```

### 7.5 Isolation from WWWA Quartet Clutter

Notice that:
- The pit name is `DailyHeat-SanDiego`.
- It lives under the dedicated domain tenant `Weather`.
- It is completely clean and independent of the WWWA quartet pits (`Person`, `Object`, `Place`, `Activity`), demonstrating domain tenant modularity.

---

## Chapter 8: Verification Checklist

| Check | Command | Expected Result |
|-------|---------|-----------------|
| **Honeypot Diagnostics** | `rail check samples/HeatRecord/Program.Naive.cs` | 35 violations across `RAI001`, `RAI002`, `RAI003`, `RAI010` |
| **Clean Implementation** | `rail check samples/HeatRecord/Program.cs` | 0 violations (`All guardrails passed`) |
| **Solution Build** | `dotnet build RaiGuard.slnx` | `Build succeeded. 0 Warning(s) 0 Error(s)` |
| **Unit & CLI Tests** | `dotnet test RaiGuard.slnx` | `Passed! 32/32 tests` (17 `RaiGuard.Tests`, 15 `rail.Tests`) |

---

## Summary of Core Guardrail Rules

| Rule | Category | Description | Required Remediation |
|------|----------|-------------|----------------------|
| **`RAI001`** | Reliability | `System.IO.Directory` is prohibited | Use `RaiPath` methods (`mkdir()`, `EnumerateDirectories()`, `EnumerateFiles()`) |
| **`RAI002`** | Reliability | `System.IO.File` is prohibited | Use `RaiFile` or `TextFile` (`new TextFile(path, content)`, `new TextFile(path).Read()`, `rm()`) |
| **`RAI003`** | Reliability | `System.IO.Path` is prohibited | Use `RaiPath` or `RaiRelPath` operator `/` (`root / "subDir"`) |
| **`RAI010`** | Formatting | Space indentation and consecutive spaces prohibited | Indent exclusively with tabs (`\t`), single space between tokens outside strings |
