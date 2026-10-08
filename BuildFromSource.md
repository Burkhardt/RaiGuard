# Build From Source

This document describes how to build and publish `rail` from source as a .NET Tool and self-contained command-line binary.

RaiGuard lives inside the `RAIkeep` repository at `RAIkeep/RaiGuard`. When working from the monorepo, local sibling projects (`JsonPit`, `OsLib`, `RaiUtils`, `RaiImage`, `RaiDiagram`) can be used directly without waiting for NuGet publication.

## Prerequisites

- .NET SDK 10.x installed
- Git installed
- Access to the package source used by the project (`https://api.nuget.org/v3/index.json`)

Clone the `RAIkeep` repository and move into the `RaiGuard` directory:

```bash
git clone <raikeep-repo-url>
cd RAIkeep/RaiGuard
```

## Project Layout

The CLI executable project is:

```text
rail/rail.csproj
```

The Roslyn analyzer engine and packaging projects are:

```text
src/RaiGuard.Core/RaiGuard.Core.csproj
src/RaiGuard.CodeFixes/RaiGuard.CodeFixes.csproj
src/RaiGuard.Package/RaiGuard.Package.csproj
```

The unit test projects are:

```text
rail.Tests/rail.Tests.csproj
tests/RaiGuard.Tests/RaiGuard.Tests.csproj
```

## Restore And Build

Restore packages:

```bash
dotnet restore rail/rail.csproj
```

Inside the `RAIkeep` workspace, local source references are resolved automatically.
To force local-source restore explicitly, append:

```bash
/p:UseLocalRAIkeepSources=true /p:RAIkeepRoot=/path/to/RAIkeep
```

Build the CLI project:

```bash
dotnet build rail/rail.csproj -c Release
```

Run tests across the solution:

```bash
dotnet test RaiGuard.sln
```

## Install As A .NET Tool Anywhere .NET Is Installed

This project is configured as a .NET Tool (`PackAsTool=true`, `ToolCommandName=rail`).

To pack the tool locally:

```bash
dotnet pack rail/rail.csproj -c Release
```

To install the tool globally from local artifacts:

```bash
dotnet tool install --global --add-source ./artifacts/nuget RaiGuard
```

Once installed, invoke `rail` anywhere:

```bash
rail --help
rail --version
rail check .
rail fix .
```

To update or uninstall:

```bash
dotnet tool update --global --add-source ./artifacts/nuget RaiGuard
dotnet tool uninstall --global RaiGuard
```

## Publish Self-Contained Binary

For deployment without a pre-installed .NET SDK:

```bash
# macOS ARM64
dotnet publish rail/rail.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true

# Linux x64
dotnet publish rail/rail.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true

# Windows x64
dotnet publish rail/rail.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
