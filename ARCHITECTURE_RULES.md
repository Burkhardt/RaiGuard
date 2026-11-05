# RaiGuard Architecture Guardrails & Empirical Audit Reference

This document provides the deep-dive engineering rationale behind all **17 RaiGuard rules (`RAI001`–`RAI017`)**, their organization into **6 overlapping Rule Group Sets**, why mainstream .NET and LLM-trained patterns conflict with the cloud-replicated, object-oriented physics of the **RAIkeep** framework (`OsLib`, `JsonPit`, `RaiUtils`, `RaiImage`, `RaiDiagram`), and the empirical audit findings across `AIA`, `AfricaStage`, and `RAIkeep`.

---

## Rule Group Sets (`rail groups`)

Rules are numbered continuously (`RAI001`–`RAI016`) with no gaps, while **Rule Groups** are modeled as mathematical sets (`RuleGroup`). A rule may belong to multiple groups, and the rules within a group do not need consecutive numbers:

| Group Set | Member Set (`RAIxxx`) | Description |
| :--- | :--- | :--- |
| **`OsLib`** | $\{ \text{RAI001, RAI002, RAI003, RAI004, RAI005, RAI006, RAI007, RAI008, RAI009} \}$ | Path algebra (`RaiPath`/`RaiRelPath`), file/directory objects (`RaiFile`/`TextFile`), typed `CliCommand` execution, and sacred `Os.Config` |
| **`CloudSync`** | $\{ \text{RAI001, RAI002, RAI004, RAI007, RAI008, RAI011, RAI013, RAI014} \}$ | Cloud-drive replication invariants (`CR022` in-place writes, `CR040` sparse deltas, `CR033` canonical path reservation, and reload safety) |
| **`JsonPit`** | $\{ \text{RAI006, RAI008, RAI011, RAI012, RAI013, RAI014, RAI015} \}$ | Open-world sparse `Pit` and `PitItem` persistence, temporal merge, protected lifecycle attributes, `PitsCommand` gates, and `DateParseHandling.None` |
| **`CliCommand`** | $\{ \text{RAI006, RAI013} \}$ | Typed `OsLib` CLI command execution (`PitsCommand`, `IorgCommand`, `RaidCommand`, `AmafuCommand`) and external CLI seed reload safety |
| **`RaiMedia`** | $\{ \text{RAI006, RAI008, RAI016} \}$ | `RaiUtils.WordCase` (`CR019`), `RaiImage` (`ImageNamingConvention.Structured`, `IorgCommand`), and `RaiDiagram` (`DiagramBuilder` `baseItemId` per `CR025`, `RaidCommand`) |
| **`Formatting`** | $\{ \text{RAI010} \}$ | Tab indentation (`\t`) and single-space token separation invariants |

---

## Part I: Complete Rule Reference (`RAI001`–`RAI016`)

### `RAI001` — Prohibit Direct Usage of `System.IO.Directory` and `DirectoryInfo`
- **Groups**: `OsLib`, `CloudSync`
- **Why Mainstream LLMs Fail**: LLMs reach for `Directory.CreateDirectory`, `Directory.GetDirectories`, `Directory.GetFiles`, `Directory.Delete`, or `new DirectoryInfo(path)` from standard BCL training corpora.
- **Why It Breaks RAIkeep**: Bypasses `RaiPath` canonical trailing-slash normalization, tilde (`~/`) expansion, cloud-storage root detection (`~/.CloudStorage/`), and `OsLib` backup/enumerate semantics.
- **Before (Prohibited)**:
  ```csharp
  Directory.CreateDirectory(dirPath);
  var subdirs = Directory.GetDirectories(dirPath);
  var info = new DirectoryInfo(dirPath);
  ```
- **After (Idiomatic RAIkeep)**:
  ```csharp
  var dir = new RaiPath(dirPath);
  dir.mkdir();
  var subdirs = dir.EnumerateDirectories();
  ```

---

### `RAI002` — Prohibit Direct Usage of `System.IO.File`, `FileInfo`, File Streams, and `ZipFile`
- **Groups**: `OsLib`, `CloudSync`
- **Why Mainstream LLMs Fail**: LLMs emit `File.ReadAllText`, `File.WriteAllText`, `new FileInfo(path).Length`, `new FileStream(...)`, `new StreamReader(...)`, `new StreamWriter(...)`, or `ZipFile.ExtractToDirectory(...)`.
- **Why It Breaks RAIkeep**: Bypasses `RaiFile`, `TextFile`, `JsonFile`, `CsvFile`, and `TmpFile` in-place write semantics, dirty-tracking (`Changed`), cloud-storage sync resilience, and built-in `RaiFile.Zip()` / `RaiFile.Unzip()`.
- **Before (Prohibited)**:
  ```csharp
  if (File.Exists(filePath))
  {
  	var text = File.ReadAllText(filePath);
  	File.WriteAllText(filePath, updatedText);
  }
  ```
- **After (Idiomatic RAIkeep)**:
  ```csharp
  var file = new TextFile(filePath);
  if (file.Exists())
  {
  	var text = file.ReadAllText();
  	new TextFile(filePath, updatedText);
  }
  ```

- **File size**: `RaiFile.Length` returns the physical size in bytes as a `long`, without reading the file contents. RAI002 reports a size-specific replacement for `FileInfo.Length` during compilation and for inline `new FileInfo(path).Length` in `rail check`.
  ```csharp
  // Before
  var size = new FileInfo(master.FullName).Length;
  // After, when master is already a RaiFile (or a derived type)
  var size = master.Length;
  // For a string path
  var otherSize = new RaiFile(path).Length;
  ```
  The Roslyn code fix **Replace with 'RaiFile.Length'** handles inline construction, including fully qualified names, aliases and parentheses. It reuses a verified existing `RaiFile` receiver when possible; otherwise it creates a fully qualified `OsLib.RaiFile`. The fix is offered only when the referenced OsLib exposes the required public API. Existing `FileInfo` variables receive guidance for manual refactoring; their declarations and other uses are not rewritten automatically. Compilation reports diagnostics and does not edit source files. The syntax-only `rail fix` does not apply this semantic code fix.

---

### `RAI003` — Prohibit Direct Usage of `System.IO.Path` Methods, Method Groups & Separator Fields
- **Groups**: `OsLib`
- **Why Mainstream LLMs Fail**: LLMs compose paths via `Path.Combine(a, b)`, extract filenames via `Path.GetFileName(p)` or LINQ method groups `.Select(Path.GetFileName)`, or concatenate `Path.DirectorySeparatorChar`.
- **Why It Breaks RAIkeep**: `RaiPath` and `RaiRelPath` overload operator `/` for algebraic path composition, and `RaiFile` exposes `.Name`, `.NameWithoutExtension`, `.Ext`, and `.Path` directly as object properties.
- **Before (Prohibited)**:
  ```csharp
  var fullPath = Path.Combine(rootDir, "Tenant", "Person.pit");
  var names = paths.Select(Path.GetFileName).ToList();
  ```
- **After (Idiomatic RAIkeep)**:
  ```csharp
  var fullPath = new RaiPath(rootDir) / "Tenant" / "Person.pit";
  var names = files.Select(f => f.Name).ToList();
  ```

---

### `RAI004` — Require Trailing `/` on `new RaiPath("...")` Directory String Literals
- **Groups**: `OsLib`, `CloudSync`
- **Why Mainstream LLMs Fail**: LLMs assume `new RaiPath("/Users/alice/Workspace")` represents the directory `/Users/alice/Workspace/`.
- **Why It Breaks RAIkeep**: In `OsLib/RaiPath.cs` (`SplitPathFileAndPosition`), `new RaiPath(string)` treats any path string **not** ending with `/` (or `\`) as a full file path and strips the final segment (`"Workspace"`) as a filename—leaving `Path` pointing at the parent `/Users/alice/`!
- **Before (Prohibited)**:
  ```csharp
  var workspace = new RaiPath("/tmp/test-workspace"); // Resolves to "/tmp/", stripping "test-workspace"!
  ```
- **After (Idiomatic RAIkeep — Auto-Fixed by `rail fix`)**:
  ```csharp
  var workspace = new RaiPath("/tmp/test-workspace/");
  // Or compose segments with operator /, which appends trailing '/' automatically:
  var workspace2 = new RaiPath("/tmp/") / "test-workspace";
  ```

---

### `RAI005` — Prohibit `+ Os.DIR +` or `+ "/" +` String Concatenation on `RaiPath`
- **Groups**: `OsLib`
- **Why Mainstream LLMs Fail**: LLMs concatenate `dir.Path + Os.DIR + fileName` or `dir + "/" + subDir` to construct file or directory paths.
- **Why It Breaks RAIkeep**: `RaiPath.Path` and `RaiPath.ToString()` **already** end with a trailing separator (`/`), so `+ Os.DIR +` produces double slashes (`//`) and bypasses `RaiPath` operator `/` and typed `RaiFile(RaiPath dir, string name, string ext)` constructors.
- **Before (Prohibited)**:
  ```csharp
  var tf = new TextFile(seedDir.Path + Os.DIR + "prompt.md");
  ```
- **After (Idiomatic RAIkeep)**:
  ```csharp
  var tf = new TextFile(seedDir, "prompt", "md");
  ```

---

### `RAI006` — Use Typed `OsLib` `CliCommand` Wrappers

- **Groups**: `OsLib`, `JsonPit`, `CliCommand`, `RaiMedia`
- Typed wrappers preserve argument tokens. `PitsCommand` also coordinates calls
  for each execution host, cloud, tenant or filesystem root, and pit within the
  calling process. Cross-process replication remains the responsibility of the
  JsonPit storage protocol.
- Create requests with a public factory; attach options with a `with` expression:

  ```csharp
  var request = PitsSeedRequest.ForPit("Person", sourceFile) with
  {
      Options = new PitsCommandOptions
      {
          Tenant = "SampleTenant",
          CloudProvider = "OneDrive",
          NoLogo = true
      }
  };
  var result = new PitsCommand().Seed(request);
  ```

- `Tenant` stays a bare name. `PitRoot` is a typed filesystem root; it cannot be
  combined with `Tenant`. Tenant-scoped mutations require an explicit cloud.
- The RAI006 code fix handles immediate `new RaiSystem("pits", ...).ExecResult()`
  seed calls with recoverable constant tokens. It supports constant token arrays
  and unquoted constant strings, preserves supported flags, and checks that the
  consuming OsLib contract includes `Tenant`.
- Dynamic/interpolated command strings, host-dependent quoted strings, buffered
  stdin, unknown flags, deferred process execution, and other verbs require
  manual migration. The rule help links to the
  [CR069 contract](https://github.com/Burkhardt/RAIkeep/blob/main/doc/CR069_AIA_to_RAIkeep_PitsCommand-Tenant-Scoping-and-Public-Contracts.md).

---

### `RAI007` — Prohibit Bypassing Sacred `Os.Config` via `Environment.*` or `AppDomain`
- **Groups**: `OsLib`, `CloudSync`
- **Why Mainstream LLMs Fail**: LLMs read or mutate configuration through `Environment.GetEnvironmentVariable(...)`, `Environment.SetEnvironmentVariable(...)`, `Environment.GetFolderPath(...)`, or `AppDomain.CurrentDomain.BaseDirectory`.
- **Why It Breaks RAIkeep**: Per `MANIFESTO.md`, `~/.config/RAIkeep.json5` hydrated via `Os.Config` (`Os.UserHomeDir`, `Os.TempDir`, `Os.AppRoot`, `Os.CloudRoot`) is the single sacred source of runtime configuration. Process-wide environment variable mutation causes cross-test and multi-tenant race conditions.
- **Before (Prohibited)**:
  ```csharp
  var root = Environment.GetEnvironmentVariable("CLOUD_ROOT");
  var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
  ```
- **After (Idiomatic RAIkeep)**:
  ```csharp
  var root = Os.CloudRoot;
  var home = Os.UserHomeDir;
  ```

---

### `RAI008` — Enforce Cloud-Storage In-Place Write Invariant (`CR022`)
- **Groups**: `OsLib`, `CloudSync`, `JsonPit`, `RaiMedia`
- **Why Mainstream LLMs Fail**: LLMs implement "clean write" or "atomic temp-file swap" patterns: deleting a file before writing (`file.rm(); file.Save();`) or writing to a `TmpFile` and moving it into a cloud directory (`destFile.mv(tmpFile)`).
- **Why It Breaks RAIkeep**:
  1. Calling `rm()` before `Save()` or `cp()` destroys the cloud sync provider's (OneDrive/Dropbox/GoogleDrive) tracked file identity and creates conflicted copy ghosts across replicas (`CR022`).
  2. Moving a `TmpFile` (from local `Os.TempDir`) into a cloud-backed `RaiPath` via `destFile.mv(tmpFile)` is explicitly rejected at runtime with `RaiCloudStorageException` (`OsLib/RaiFile.cs:544-552`).
- **Before (Prohibited)**:
  ```csharp
  outFile.rm();
  outFile.Save();

  using var tmp = new TmpFile("svg");
  destFile.mv(tmp); // Throws RaiCloudStorageException when destFile is under CloudStorage!
  ```
- **After (Idiomatic RAIkeep)**:
  ```csharp
  outFile.Save(); // Overwrites in-place, preserving cloud provider file identity

  using var tmp = new TmpFile("svg");
  destFile.cp(tmp); // Copies content in-place into destFile
  tmp.rm();
  ```

---

### `RAI009` — Prevent Inverted `OsLib` `cp` / `mv` Receiver Order
- **Groups**: `OsLib`
- **Why Mainstream LLMs Fail**: In `System.IO.FileInfo`, `sourceFile.CopyTo(destFile)` and `sourceFile.MoveTo(destFile)` have the **source** as `this` and the **destination** as the parameter.
- **Why It Breaks RAIkeep**: In `OsLib`, `destFile.cp(sourceFile)` and `destFile.mv(sourceFile)` are object-hydration operations: the **destination** `RaiFile` is `this` (the receiver being hydrated), and the **source** is the first parameter (`OsLib/RaiFile.cs:535-620`). Inverting them overwrites the source file with an empty destination!
- **Before (Prohibited)**:
  ```csharp
  sourceFile.cp(destFile); // WRONG: copies destFile over sourceFile!
  ```
- **After (Idiomatic RAIkeep)**:
  ```csharp
  destFile.cp(sourceFile); // Hydrates destFile from sourceFile
  ```

---

### `RAI010` — Enforce Tab Indentation and Prohibit Consecutive Interior Spaces
- **Groups**: `Formatting`
- **Why Mainstream LLMs Fail**: Default C# LLM completions use 4-space indentation.
- **Why It Breaks RAIkeep**: All RAIkeep repositories mandate tab indentation (`\t`) and single spaces between tokens outside string and character literals.
- **Remediation**: Run `rail fix --rule RAI010` (or `rail fix --group Formatting`) to automatically normalize leading indentation to tabs and collapse consecutive interior spaces.

---

<a id="rai011"></a>
### `RAI011` — Unsafe detached or foreign Pit writes (CR077)

Groups: `JsonPit`, `CloudSync`.

The current indexer and ordinary `Get` return tracked live handles. Indexing,
SetProperty, Merge and ExtendWith on those handles emit sparse history; re-adding
a same-owner live handle is a supported boundary flush. It is not evidence of a
full-snapshot overwrite. Fresh explicit deltas and ordinary List/HashSet/JArray
operations are valid too.

A detached historical projection is different:

```csharp
var snapshot = pit.GetAt(id, timestamp);
pit.Add(snapshot); // RAI011: historical metadata and unrelated fields are not a client delta
```

Use the ordinary object model:

```csharp
var item = pit[id];
item["Status"] = "Confirmed";
item.SetProperty(new { Location = "Main stage", Notes = "Ready" });
item.DeleteProperty("TemporaryNote");
pit.Save();
```

The semantic analyzer follows aliases, casts, chronological assignments, scoped
symbols, collections, and bounded source-helper summaries. It reports proven
unsafe detached writes, cross-owner live writes when distinct owners are proven,
and retired handle writes. Unknown provenance does not establish a violation.
A later assignment cannot retroactively change an earlier write. It never infers
Pit types from variable names. Historical replay remains explicit and is not an
application workaround for protected metadata errors.

Source-helper summaries are bounded to four calls within one syntax tree. Loops
use bounded analysis and joins; arbitrary reflection, callbacks and unresolved
external helpers are not a complete proof of safety. `rail check` shares the
engine with the compiler analyzer and reports incomplete reference resolution on
stderr. Restore/build the actual project and use compiler diagnostics for its
full reference context.

<a id="rai012"></a>
### `RAI012` — Protected PitItem lifecycle attributes

Group: `JsonPit`.

Resolve the actual item type or tracked provenance before diagnosing direct
assignments and explicit mutation/deletion payloads. Id, Modified and Deleted are
engine-owned. A matching Id assertion in a patch is supported; an unknown Id
relationship is not proof of a mismatch. Do not flag unrelated JSON transforms.

```csharp
var item = pit[id];
item["Modified"] = DateTimeOffset.UtcNow; // prohibited
item.ExtendWith(new JObject { ["Modified"] = "manual", ["Title"] = title }); // rejected atomically
```

Update the business fields instead. Use `RenameId`, `Pit.Delete` or the live
item's `Delete` for lifecycle transitions. `Remove`/DeleteProperty on ordinary
tracked properties is valid and emits the appropriate tombstone; there is no
blanket prohibition on removal. Genuine historical replay uses AddHistorical,
not as a way around client validation.

---

### `RAI013` — Prohibit `pit.PitDir.Exists()` Side-Effect and `pit.Reload()` Overwrite Hazard
- **Groups**: `JsonPit`, `CloudSync`, `CliCommand`
- **Why Mainstream LLMs Fail**: LLMs write `if (pit.PitDir.Exists())` assuming a property getter is side-effect-free, and call `pit.Reload()` assuming it means "read latest changes from disk".
- **Why It Breaks RAIkeep**:
  1. `Pit.PitDir` (`JsonPit/Pit.cs:54-60`) executes `JsonFile.Path.mkdir()` inside the property getter—so checking `pit.PitDir.Exists()` **creates the directory on disk** before checking!
  2. `Pit.Reload()` (`JsonPit/Pit.cs:528-550`) calls `Save()` on two of its three code paths, overwriting fresh external CLI (`pits` / `jpit`) seeds on disk with stale in-memory state.
- **Before (Prohibited)**:
  ```csharp
  if (pit.PitDir.Exists())
  {
  	pit.Reload();
  }
  ```
- **After (Idiomatic RAIkeep — Auto-Fixed by `rail fix`)**:
  ```csharp
  if (pit.JsonFile.Path.Exists())
  {
  	pit.Load();
  }
  ```

---

### `RAI014` — Require `Pit` Disposal (`CR033`)
- **Groups**: `JsonPit`, `CloudSync`
- **Why Mainstream LLMs Fail**: LLMs instantiate `var pit = new Pit(...)` without a `using` statement or store `_pit = new Pit(...)` in a class field without disposing it in `IDisposable.Dispose()`.
- **Why It Breaks RAIkeep**: `Pit` registers its canonical path in the process-wide `ActiveCanonicalPitPaths` reservation table (`CR033`) to prevent dual-instance split-brain writes, and `Pit.Dispose()` performs the safety-net `Save()` for live-mutated items. Undisposed `Pit` instances leak canonical path reservations for the life of the process.
- **Before (Prohibited)**:
  ```csharp
  var pit = new Pit(pitPath, readOnly: true);
  return pit.Get(id);
  ```
- **After (Idiomatic RAIkeep)**:
  ```csharp
  using var pit = new Pit(pitPath, readOnly: true);
  return pit.Get(id);
  ```

---

### `RAI015` — Prohibit Unexpanded Template Placeholders in `PitItem` IDs (`CR049`) and Unsafe `JObject.Parse`
- **Groups**: `JsonPit`
- **Why Mainstream LLMs Fail**: LLMs pass unvalidated template strings (`"{subscriberId}"`, `"<id>"`) into `new PitItem(...)`, or parse raw JSON strings with `JObject.Parse(json)` / `JArray.Parse(json)` / `JToken.Parse(json)`.
- **Why It Breaks RAIkeep**:
  1. Unexpanded `{...}` or `<...>` placeholders pollute `Pit` canon files with literal template IDs (`CR049`).
  2. `JObject.Parse` defaults to `DateParseHandling.DateTime` in Newtonsoft.Json, silently converting ISO-8601 timestamp strings into `DateTime` values shifted to the server's local timezone.
- **Before (Prohibited)**:
  ```csharp
  var item = new PitItem("{templateId}");
  var payload = JObject.Parse(rawJson);
  ```
- **After (Idiomatic RAIkeep)**:
  ```csharp
  var item = new PitItem(validatedId);
  var payload = new JsonFile(jsonPath).Read(); // Or JsonSerializerSettings { DateParseHandling = DateParseHandling.None }
  ```

---

### `RAI016` — Enforce `RaiUtils.WordCase` (`CR019`), Alphanumeric `DiagramBuilder` `BaseItemId` (`CR025`), and `ImageNamingConvention.Structured`
- **Groups**: `RaiMedia`
- **Why Mainstream LLMs Fail**:
  1. Resolving obsolete `RaiImage.WordCase` instead of canonical `RaiUtils.WordCase` (`CR019`).
  2. Passing diagram type suffixes (`"Person_UCD"`, `"Order_CD"`) or hyphenated strings (`"Order-Flow"`) as `baseItemId` to `DiagramBuilder` (`CR025` — `DiagramBuilder` automatically appends `_UCD`, `_CD`, `_OD`, `_SD` and throws `ArgumentException` on non-alphanumeric `baseItemId`).
  3. Selecting legacy `ImageNamingConvention` values instead of `ImageNamingConvention.Structured`.
- **Before (Prohibited)**:
  ```csharp
  var wc = RaiImage.WordCase.PascalCase;
  var builder = new DiagramBuilder(pit, "Subscriber_UCD");
  ```
- **After (Idiomatic RAIkeep)**:
  ```csharp
  var wc = RaiUtils.WordCase.PascalCase;
  var builder = new DiagramBuilder(pit, "Subscriber");
  ```

---

### `RAI017` — Prohibit Ad-Hoc `/img/` Media URL Composition
- **Groups**: `RaiMedia`
- **Why Mainstream LLMs Fail**: LLMs routinely compose `/img/` endpoints using manual string interpolation (`$"/img/{tenant}/{show}_{inr}.mp4"`) or concatenation (`"https://api.aia.software/img/" + show + ...`).
- **Why It Breaks AIA / RAIkeep**: Manual `/img/` composition leaks internal filesystem structures (`ItemIdTree8x2`), introduces URL drift, bypasses media presets (`Stage`, `Poster`), hardcodes extensions (`.mp4`, `.svg`), and creates fragile dependencies on raw routing shapes (ADR-0011, ADR-0012).
- **Target Patterns**:
  Any `InterpolatedStringExpressionSyntax` or binary addition (`+`) containing `"/img/"` or `"api.aia.software/img/"` outside of:
  - `MediaUrls` (the canonical helper definition)
  - `ImageController` (the raw routing endpoint definitions)
  - Unit test assertion invocations (e.g. `Assert.Equal(...)`, `.Should()....`)
- **Before (Prohibited)**:
  ```csharp
  // Manual string interpolation
  var url = $"/img/{tenant}/{show}_{inr}.mp4";
  var poster = $"https://api.aia.software/img/{show}?inr={inr}&tmp=Poster&sub={sub}";
  ```
- **After (Idiomatic AIA / RAIkeep)**:
  ```csharp
  using AIA.Core.Services.Media;

  var url = MediaUrls.Video(show, inr, preset: "Stage", sub: tenant);
  var poster = MediaUrls.Poster(show, inr, sub: tenant);
  ```
- **Remediation**: Use `rail fix --rule RAI017` or apply the Roslyn CodeFix `RAI017CodeFixProvider` (*"Replace with MediaUrls.Video / MediaUrls.Image"*).

---

## Part II: Empirical Cross-Repository Audit (`AIA` & `AfricaStage`)

Running `rail check` across the two internal production repositories (`../../AIA` and `../../AfricaStage`) and the `RAIkeep` sibling repositories surfaced the following real-world violations:

| Rule ID | Groups | `AIA` Count | `AfricaStage` Count | High-Priority Production Citations |
| :--- | :--- | :---: | :---: | :--- |
| **`RAI001`** | `OsLib`, `CloudSync` | 1 | 26 | `AIA`: `DomainQuartets.cs:276`; `AfricaStage`: `OpmReasonerTests.cs:62-97`, `ActivityImageControllerTests.cs:169`; `Amafu`: `CloudStorageDetector.cs:71` |
| **`RAI002`** | `OsLib`, `CloudSync` | 1 | 31 | `AfricaStage`: `FlyersController.cs:129,141` (`new FileInfo`, `File.Create`), `DiagramRenderer.cs:122,132` (`File.ReadAllBytesAsync`), 6 prompt services (`File.Exists`/`ReadAllText`); `RaiImage`: `ImageMagick.cs:64,335` |
| **`RAI003`** | `OsLib` | 19 | 38 | `AIA`: `DomainQuartets.cs:277` (`.Select(Path.GetFileName)` method group), `DiagramArtifactWriter.cs:318` (`Path.GetFileNameWithoutExtension`); `AfricaStage`: `FlyersController.cs:120` |
| **`RAI004`** | `OsLib`, `CloudSync` | 0 | 2 | `AfricaStage.Tests/Controllers/CheckTests.cs:205,213` (`new RaiPath("/tmp/...")` without trailing `/`, stripping the last folder segment!) |
| **`RAI005`** | `OsLib` | 3 | 7 | `AIA`: `OpmConditionAuthor.cs:43`, `OpmExecutionInterpreter.cs:38` (`seedDir.Path + Os.DIR + ...`); `AfricaStage`: `SubscriberController.cs:524,553`, `FlyersController.cs:122-123` |
| **`RAI006`** | `OsLib`, `JsonPit`, `CliCommand`, `RaiMedia` | 5 | 0 | `AIA`: `TenantProvisioningService.cs:205`, `LiveDiagramStorageTests.cs:52`, `CredentialMintingTool.cs:89`, `LiveTenantConformanceTests.cs:79`, `LiveWorkbenchBoardTests.cs:72` (`new RaiSystem("pits", ...)` bypassing `PitsCommand.TargetGates`) |
| **`RAI007`** | `OsLib`, `CloudSync` | 14 | 15 | `AIA`: `AiaApiFactory.cs:96-108`; `AfricaStage`: `AfricaStageAppFactory.cs:82-107` (`Environment.SetEnvironmentVariable` mutating process state) |
| **`RAI008`** | `OsLib`, `CloudSync`, `JsonPit`, `RaiMedia` | 0 | 0 | Enforced preventatively (`CR022` cloud-storage in-place write invariant) |
| **`RAI009`** | `OsLib` | 0 | 0 | Enforced preventatively (`dest.cp(source)` / `dest.mv(source)` receiver order) |
| **`RAI010`** | `Formatting` | 575 | 7,482 | Space indentation across `AIA` and `AfricaStage` (auto-fixable via `rail fix --rule RAI010` or `rail fix --group Formatting`) |
| **`RAI011`** | `JsonPit`, `CloudSync` | 34 | 44 | `AIA`: `ExternalAcceptance.cs:198,219,244`, `ObjectInstantiator.cs:262`, `OpmConditionMigrator.cs:122`, `ConversationalAuthoring.cs:399`; `AfricaStage`: 14 in `ActivityController.cs`, `ObjectController.cs:815`, `SystemMethodExecutor.cs:197` (historical analyzer observations; CR077 reclassifies same-owner tracked live operations as supported) |
| **`RAI012`** | `JsonPit` | 15 | 36 | `AfricaStage`: `OpmReasoner.cs:272` (`["Id"] = id` inside `ExtendWith`), `ChatTranscript.cs:75` (`new JObject { ["Id"] = ... }` passed to `ExtendWith`) |
| **`RAI013`** | `JsonPit`, `CloudSync`, `CliCommand` | 0 | 0 | Preventative guard for `pit.PitDir.Exists()` `mkdir()` side-effect and `pit.Reload()` overwrite hazard (see `AfricaStage/Services/PitReloadService.cs:50-57`) |
| **`RAI014`** | `JsonPit`, `CloudSync` | 122 | 31 | 153 undisposed `new Pit(...)` instances across test suites and helper services leaking `ActiveCanonicalPitPaths` reservations |
| **`RAI015`** | `JsonPit` | 86 | 58 | 144 calls to `JObject.Parse` / `JArray.Parse` / `JToken.Parse` with default `DateParseHandling.DateTime` |
| **`RAI016`** | `RaiMedia` | 0 | 0 | Preventative guard for `RaiUtils.WordCase` (`CR019`), `DiagramBuilder` `baseItemId` (`CR025`), and `ImageNamingConvention.Structured` |
| **`RAI017`** | `RaiMedia` | 5 | 0 | `AIA`: `ActivityController.cs:294,400,494`, `BlueprintController.cs:154,272` (ad-hoc `/img/` string interpolation outside `MediaUrls`) |
| **Total** | **6 Group Sets** | **880** | **7,768** | Across 108 files in `AIA` and 147 files in `AfricaStage` |
