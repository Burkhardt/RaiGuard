# RaiGuard Integration & Adoption Guide

This guide covers how to adopt, integrate, and configure **RaiGuard** across your repositories, solutions, CI/CD pipelines, and IDE environments.

---

## 1. Package Architecture

RaiGuard is distributed as two complementary NuGet packages:

| Package | Type | Target | Primary Purpose |
|---------|------|--------|-----------------|
| **[`RaiGuard.Analyzer`](https://www.nuget.org/packages/RaiGuard.Analyzer)** | Roslyn Analyzer & Code-Fix Provider | Projects & Solutions | Enforces framework physics during `dotnet build`, emits compiler warnings/errors, and provides in-editor Lightbulb quick-fixes. |
| **[`RaiGuard`](https://www.nuget.org/packages/RaiGuard)** | .NET Global Tool CLI (`rail`) | Developer Workstations & CI Runners | Provides terminal batch inspection (`rail check`), automated AST migrations (`rail fix`), and rules discovery (`rail rules`). |

---

## 2. Installing the Roslyn Analyzer (`RaiGuard.Analyzer`)

### Option A: Single Project Adoption

To protect an individual C# project, add `RaiGuard.Analyzer` as a development dependency in your `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="RaiGuard.Analyzer" Version="4.5.10">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
</ItemGroup>
```

Or via the .NET CLI:

```bash
dotnet add package RaiGuard.Analyzer --version 4.5.10
```

### Option B: Solution-Wide Protection via `Directory.Build.props` (Recommended)

To automatically enforce RaiGuard across all current and future projects in a repository without modifying individual `.csproj` files, create or update `Directory.Build.props` at the root of your repository:

```xml
<Project>
  <!-- Enforce RaiGuard Roslyn guardrails on all projects in this repository -->
  <ItemGroup Condition="'$(MSBuildProjectExtension)' == '.csproj'">
    <PackageReference Include="RaiGuard.Analyzer" Version="4.5.10">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

Once saved, every `dotnet build` or IDE compilation across the entire repository enforces all 16 RaiGuard rules (`RAI001`–`RAI009`, `RAI010`, `RAI020`–`RAI024`, and `RAI030`).

---

## 3. Installing the Global CLI Tool (`rail`)

Install the `rail` command-line utility globally via NuGet:

```bash
dotnet tool install --global RaiGuard
```

On macOS or Linux, a practical option is to install directly into a directory on your PATH:

```bash
sudo dotnet tool install RaiGuard --tool-path /usr/local/bin
```

so that no `$PATH` environment variable change has to happen.

Verify installation:

```bash
rail --version
rail rules
```

To update:

```bash
dotnet tool update --global RaiGuard
```

To update an installation in `/usr/local/bin`:

```bash
sudo dotnet tool update RaiGuard --tool-path /usr/local/bin
```

---

## 4. In-Editor Developer Experience (VS Code, Visual Studio, Rider)

### Visual Studio Code (C# Dev Kit / OmniSharp)

When you open any project referencing `RaiGuard.Analyzer` in VS Code with the official **C# Dev Kit** extension installed:

1. **Real-Time Inline Squiggles**:
   - As you type, prohibited APIs (`System.IO.Path.Combine`, `Directory.CreateDirectory`, `File.WriteAllText`, full-snapshot `Pit` overwrites, or leading space indentation) are instantly flagged with yellow warning squiggles.
2. **Hover Documentation**:
   - Hovering over a squiggle displays the exact rule ID (e.g. `RAI003`) along with the official framework remedy:
     > `'System.IO.Path.Combine' is prohibited; use 'RaiPath' or 'RaiRelPath' with operator '/' instead.`
3. **Lightbulb Quick-Fixes (`Cmd+.` / `Ctrl+.`)**:
   - Pressing `Cmd+.` (macOS) or `Ctrl+.` (Windows/Linux) invokes the automated code-fix provider:
     - Automatically rewrites `Path.Combine(root, "sub")` into `new RaiPath(root) / "sub"`.
     - Appends the required trailing `/` to `new RaiPath("...")` string literals (`RAI004`).
     - Rewrites `pit.PitDir.Exists()` to `pit.JsonFile.Path.Exists()` and `pit.Reload()` to `pit.Load()` (`RAI022`).
     - Rewrites `RaiImage.WordCase` to `RaiUtils.WordCase` (`RAI030`).
     - Automatically adds `using OsLib;` or `using RaiUtils;` to the file header if not already present.

### Visual Studio & JetBrains Rider

`RaiGuard.Analyzer` natively integrates with Visual Studio and JetBrains Rider standard Roslyn analysis pipelines. Diagnostics appear in the **Error List** window and lightbulb / alt-enter refactoring menus.

---

## 5. Configuring Severity & Exemptions via `.editorconfig`

All RaiGuard rules support granular configuration and severity overrides via `.editorconfig`.

### Promoting Guardrails to Hard Build Errors

To prevent prohibited code from compiling under any circumstance, set the rule severity to `error` in your root `.editorconfig`:

```ini
[*.cs]
# Promote framework path & I/O violations to hard compiler errors
dotnet_diagnostic.RAI001.severity = error
dotnet_diagnostic.RAI002.severity = error
dotnet_diagnostic.RAI003.severity = error
dotnet_diagnostic.RAI004.severity = error
dotnet_diagnostic.RAI005.severity = error

# Promote JsonPit cloud-replication invariants to hard compiler errors
dotnet_diagnostic.RAI020.severity = error
dotnet_diagnostic.RAI021.severity = error
dotnet_diagnostic.RAI022.severity = error

# Formatting invariant: enforce tab indentation
indent_style = tab
dotnet_diagnostic.RAI010.severity = error
```

### Exempting Low-Level Substrate Projects

Low-level infrastructure libraries (such as `OsLib` itself, which implements POSIX bindings or platform interop) may legitimately interact with system primitives. You can selectively exempt these directories:

```ini
# Exempt low-level substrate implementation files from RAI001-RAI003
[src/OsLib/**.cs]
dotnet_diagnostic.RAI001.severity = none
dotnet_diagnostic.RAI002.severity = none
dotnet_diagnostic.RAI003.severity = none
```

---

## 6. Terminal & CI/CD Linting Workflows (`rail`)

While the Roslyn analyzer operates during compilation, the `rail` CLI provides flexible batch inspection and automated codebase migration.

### Auditing Source Code (`rail check`)

Audit your entire workspace or a target directory:

```bash
rail check
rail check src/MyProject/
rail check --rule RAI020
rail check --json
```

If any violation exists, `rail check` exits with code `1`, making it ideal for CI lint gates.

### Automated Batch Migration (`rail fix`)

When onboarding an existing repository or refactoring AI-generated code, use `rail fix` to rewrite prohibited calls in bulk:

```bash
# Preview changes safely without modifying disk
rail fix --dry-run

# Apply AST refactorings across the repository
rail fix

# Apply only a specific rule (e.g. tab indentation)
rail fix --rule RAI010
```

`rail fix` performs:
- Path composition rewrites to operator `/` (`RAI003`) and trailing slash fixes on `new RaiPath("...")` (`RAI004`).
- Directory and file operation rewrites to `RaiPath`, `new TextFile(...)`, and `new RaiFile(...)` (`RAI001`, `RAI002`).
- Tab indentation and interior consecutive space normalization (`RAI010`).
- `Pit` side-effect-free existence and load rewrites (`RAI022`) and `RaiUtils.WordCase` canonicalization (`RAI030`).
- Automatic insertion of `using OsLib;`.

---

## 7. Continuous Integration (CI) Integration

### GitHub Actions Lint Gate

Add a `rail check` step to your GitHub Actions pull request workflow:

```yaml
name: Guardrails CI

on: [push, pull_request]

jobs:
  lint:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Install rail CLI
        run: dotnet tool install --global RaiGuard

      - name: Verify Framework Guardrails
        run: rail check
```

---

## Summary of Guardrail Rules

| Rule ID | Severity | Title | Remedy |
|---------|----------|-------|--------|
| **`RAI001`** | Warning | Prohibit direct usage of `System.IO.Directory` and `DirectoryInfo` | Use `RaiPath` methods (`mkdir()`, `EnumerateDirectories()`, `EnumerateFiles()`, `rmdir()`) |
| **`RAI002`** | Warning | Prohibit direct usage of `System.IO.File`, `FileInfo`, file streams, and `ZipFile` | Use `RaiFile`, `TextFile`, `JsonFile`, `CsvFile`, or `TmpFile` (`new TextFile(path, content)`, `Read()`, `rm()`, `Zip()`, `Unzip()`) |
| **`RAI003`** | Warning | Prohibit direct usage of `System.IO.Path` methods, method groups, and separator fields | Use `RaiPath` / `RaiRelPath` operator `/` (`root / "subDir"`), `RaiFile.Name`, or `RaiFile.NameWithoutExtension` |
| **`RAI004`** | Warning | Require trailing `/` on `new RaiPath("...")` string literals | Terminate directory string literal with `/` (`new RaiPath("/tmp/dir/")`) or compose with operator `/` |
| **`RAI005`** | Warning | Prohibit string concatenation with `Os.DIR` or `"/"` on `RaiPath` | Compose paths with `dir / "sub"` and construct files via `new RaiFile(dir, name, ext)` or `new TextFile(dir, name, ext)` |
| **`RAI006`** | Warning | Prohibit bypassing typed `OsLib` `CliCommand` wrappers | Use `PitsCommand`, `IorgCommand`, `RaidCommand`, `AmafuCommand`, or `CliCommand` instead of `Process` or raw `new RaiSystem(...)` |
| **`RAI007`** | Warning | Prohibit bypassing sacred `Os.Config` via `Environment.*` or `AppDomain` | Read configuration from `Os.Config`, `Os.UserHomeDir`, `Os.TempDir`, or `Os.AppRoot` |
| **`RAI008`** | Warning | Enforce cloud-storage in-place write invariant (`CR022`) | Never call `rm()` before `Save()`/`cp()`, and never `.mv(tmp)` from temp storage into cloud roots (use `dest.cp(tmp); tmp.rm();`) |
| **`RAI009`** | Warning | Prevent inverted `OsLib` `cp` / `mv` receiver order | Call `dest.cp(source)` or `dest.mv(source)` where the **destination** `RaiFile` is `this` |
| **`RAI010`** | Warning | Enforce tab indentation and prohibit consecutive spaces | Indent exclusively with tabs (`\t`); single space between tokens outside string literals (`rail fix --rule RAI010`) |
| **`RAI020`** | Warning | Prohibit `Pit` full-snapshot read-modify-write (`CR040`) | Construct a sparse delta `var delta = new PitItem(id); delta.ExtendWith(patch); pit.Add(delta);` or mutate live properties on `pit.Get(id)` |
| **`RAI021`** | Warning | Prohibit direct mutation of protected `PitItem` lifecycle attributes (`Id`, `Modified`, `Deleted`) | Use `new PitItem(id)`, `item.Delete()`, `item.Undel()`, and strip `Id`/`Modified`/`Deleted` from external `ExtendWith` payloads |
| **`RAI022`** | Warning | Prohibit `pit.PitDir.Exists()` side-effect and `pit.Reload()` overwrite hazard | Use `pit.JsonFile.Path.Exists()` for side-effect-free checks and `pit.Load()` to refresh external seeds |
| **`RAI023`** | Warning | Require `Pit` disposal (`CR033`) | Wrap `new Pit(...)` in `using var pit = ...` or dispose `_pit` in `IDisposable.Dispose()` |
| **`RAI024`** | Warning | Prohibit unexpanded `{...}`/`<...>` placeholders in `PitItem` IDs (`CR049`) and unsafe `JObject.Parse` | Validate IDs before `new PitItem(id)` and parse JSON via `JsonFile` or `JsonReader` with `DateParseHandling.None` |
| **`RAI030`** | Warning | Enforce `RaiUtils.WordCase` (`CR019`), alphanumeric `DiagramBuilder` `baseItemId` (`CR025`), and `ImageNamingConvention.Structured` | Use `RaiUtils.WordCase`, alphanumeric `baseItemId` without `_UCD`/`_CD` suffixes, and `ImageNamingConvention.Structured` |
