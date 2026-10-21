# RaiGuard Architecture Guardrails & Empirical Audit Reference

This document provides the deep-dive engineering rationale behind all **16 RaiGuard rules (`RAI001`–`RAI016`)**, their organization into **6 overlapping Rule Group Sets**, why mainstream .NET and LLM-trained patterns conflict with the cloud-replicated, object-oriented physics of the **RAIkeep** framework (`OsLib`, `JsonPit`, `RaiUtils`, `RaiImage`, `RaiDiagram`), and the empirical audit findings across `AIA`, `AfricaStage`, and `RAIkeep`.

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

### `RAI006` — Prohibit Bypassing Typed `OsLib` `CliCommand` Wrappers
- **Groups**: `OsLib`, `JsonPit`, `CliCommand`, `RaiMedia`
- **Why Mainstream LLMs Fail**: LLMs spawn external tools with `System.Diagnostics.Process` / `ProcessStartInfo`, or instantiate raw `new RaiSystem("pits", args)` directly.
- **Why It Breaks RAIkeep**: `OsLib` provides typed wrappers (`PitsCommand`, `IorgCommand`, `RaidCommand`, `AmafuCommand`, `CliCommand`). Critically, `PitsCommand` enforces per-root/per-pit `SemaphoreSlim` target gates (`PitsCommand.TargetGates`) so concurrent API requests never corrupt the same `.pit` file during external CLI execution. Calling `new RaiSystem("pits", ...)` bypasses those gates entirely.
- **Before (Prohibited)**:
  ```csharp
  var sys = new RaiSystem("pits", $"seed Person --source {src} -r {tenant} -n");
  sys.Exec(out string output);
  ```
- **After (Idiomatic RAIkeep)**:
  ```csharp
  var cmd = new PitsCommand($"seed Person --source {src} -r {tenant} -n");
  cmd.Exec(out string output);
  ```

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

### `RAI011` — Prohibit `Pit` Full-Snapshot Read-Modify-Write (`CR040`)
- **Groups**: `JsonPit`, `CloudSync`
- **Why Mainstream LLMs Fail**: LLMs treat `pit.Get(id)` or `pit[id]` like a standard document-store CRUD cycle: read the existing item, call `existing.ExtendWith(patch)`, and pass the entire projected item back to `pit.Add(existing)`.
- **Why It Breaks RAIkeep**: In `JsonPit`, `pit.Get(id)` returns a projected snapshot of all historical properties. Calling `existing.ExtendWith(patch)` and `pit.Add(existing)` writes **every** old property back into a new timestamped delta entry—stamping a newer `Modified` timestamp on untouched fields and silently overwriting concurrent edits synced from other cloud servers (`CR040`).
- **Before (Prohibited)**:
  ```csharp
  var existing = pit.Get(id);
  existing.ExtendWith(patch);
  pit.Add(existing); // Stamps ALL old properties of 'existing' as newly modified!
  pit.Save();
  ```
- **After (Idiomatic RAIkeep)**:
  ```csharp
  // Option A: Sparse delta write (only patched fields are recorded in the delta)
  var delta = new PitItem(id);
  delta.ExtendWith(patch);
  pit.Add(delta);
  pit.Save();

  // Or via a one-line sparse helper that strips protected envelope keys (e.g. AIA's SparsePayload.Item):
  pit.Add(SparsePayload.Item(id, patch));

  // Option B: Live reference property mutation (CR033)
  var item = pit.Get(id);
  item["Status"] = "Confirmed";
  pit.Save();
  ```

---

### `RAI012` — Prohibit Direct Mutation of Protected `PitItem` Lifecycle Attributes (`Id`, `Modified`, `Deleted`)
- **Groups**: `JsonPit`
- **Why Mainstream LLMs Fail**: LLMs set `item["Id"] = id`, `item["Deleted"] = true`, `item["Modified"] = DateTimeOffset.UtcNow`, or pass a `JObject` containing `"Id"`, `"Modified"`, or `"Deleted"` into `item.ExtendWith(payload)`.
- **Why It Breaks RAIkeep**: `Id`, `Modified`, and `Deleted` are protected temporal-merge lifecycle attributes managed exclusively by `PitItem` (`JsonPit/PitItem.cs:27-33`). Direct indexer mutation or `ExtendWith` injection throws `InvalidOperationException` at runtime or corrupts tombstone history.
- **Before (Prohibited)**:
  ```csharp
  item["Deleted"] = true;
  item.ExtendWith(new JObject { ["Id"] = id, ["Title"] = title });
  ```
- **After (Idiomatic RAIkeep)**:
  ```csharp
  item.Delete(); // Or item.Undel()
  item.ExtendWith(new JObject { ["Title"] = title });
  ```

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
| **`RAI011`** | `JsonPit`, `CloudSync` | 34 | 44 | `AIA`: `ExternalAcceptance.cs:198,219,244`, `ObjectInstantiator.cs:262`, `OpmConditionMigrator.cs:122`, `ConversationalAuthoring.cs:399`; `AfricaStage`: 14 in `ActivityController.cs`, `ObjectController.cs:815`, `SystemMethodExecutor.cs:197` (`existing.ExtendWith(patch); pit.Add(existing);` full-snapshot overwrites!) |
| **`RAI012`** | `JsonPit` | 15 | 36 | `AfricaStage`: `OpmReasoner.cs:272` (`["Id"] = id` inside `ExtendWith`), `ChatTranscript.cs:75` (`new JObject { ["Id"] = ... }` passed to `ExtendWith`) |
| **`RAI013`** | `JsonPit`, `CloudSync`, `CliCommand` | 0 | 0 | Preventative guard for `pit.PitDir.Exists()` `mkdir()` side-effect and `pit.Reload()` overwrite hazard (see `AfricaStage/Services/PitReloadService.cs:50-57`) |
| **`RAI014`** | `JsonPit`, `CloudSync` | 122 | 31 | 153 undisposed `new Pit(...)` instances across test suites and helper services leaking `ActiveCanonicalPitPaths` reservations |
| **`RAI015`** | `JsonPit` | 86 | 58 | 144 calls to `JObject.Parse` / `JArray.Parse` / `JToken.Parse` with default `DateParseHandling.DateTime` |
| **`RAI016`** | `RaiMedia` | 0 | 0 | Preventative guard for `RaiUtils.WordCase` (`CR019`), `DiagramBuilder` `baseItemId` (`CR025`), and `ImageNamingConvention.Structured` |
| **Total** | **6 Group Sets** | **875** | **7,768** | Across 108 files in `AIA` and 147 files in `AfricaStage` |
