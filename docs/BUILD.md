# Building Novara from Source

<p align="center"><strong>English</strong> · <a href="BUILD.zh-CN.md">简体中文</a></p>

How to build, publish, and package Novara from source.

---

## Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| .NET SDK | **10.0.400** (via `global.json`) | Build all C# projects — desktop projects stay on `net8.0`, the three server-side projects target `net10.0` |
| Windows SDK | 10.0.26100.0 | WinUI 3 target (see note below) |
| Node.js + npm | any recent LTS | Build the `DiaryEditorJs` editor bundles |
| Inno Setup | 6+ | Compile the installer (`Installer/setup.iss`) |

> **Windows SDK path note:** `AppxMSBuildToolsPath` points at the Appx package MSBuild tasks used by the WinUI PRI generation step. This repository does **not** ship the property — it is a build-machine setting, not a project setting — so the build falls back to the .NET SDK's own path. If your SDK lacks `Microsoft.Build.Packaging.Pri.Tasks.dll` you will see `MSB4062 ... ExpandPriContent`; in that case pass the property on the command line (or set it in the environment) to point at your Visual Studio installation's `AppxPackage` folder.

## Reproducible build inputs

Three inputs are pinned so that the same source produces the same bytes:

- **SDK** — `global.json` pins the .NET SDK to an exact version with `rollForward: disable`, so a machine with a newer feature band cannot silently build with it.
- **Dependencies** — every project carries a `packages.lock.json`. Restore against it:

  ```powershell
  dotnet restore Novara.slnx -p:RestoreLockedMode=true
  ```

  This fails if the project files and the lock files disagree. Use `--force-evaluate` only when you have intentionally changed a package reference. Do not pass locked mode to `dotnet publish -r <rid>` (or a single-file publish): that restore legitimately extends the dependency graph and would be rejected.
- **Build paths** — `Directory.Build.props` sets `PathMap` in `Release`, mapping the build-machine absolute path to `/_/` so it does not end up in the binaries.

The packaging job publishes twice in the same run and compares the two trees file by file with SHA-256; any difference fails the build.

## Project layout

| Project | Output | Notes |
|---------|--------|-------|
| `Novara.csproj` | `Novara.exe` | Main WinUI 3 app (unpackaged, self-contained) |
| `StickNoteHost/StickNoteHost.csproj` | `StickNoteHost.exe` | Desktop sticky-note host process |
| `NovaraMCP/NovaraMCP.csproj` | `NovaraMCP.exe` | MCP server (single-file) |
| `Novara.Core/Novara.Core.csproj` | class library | Pure logic (referenced by the app and tests) |
| `Novara.Tests/Novara.Tests.csproj` | xUnit tests | Unit tests |
| `Novara.Server/Novara.Server.csproj` | `NovaraSync.exe` | Self-hosted sync server host (single-file, self-contained) |
| `Novara.Sync.Server/Novara.Sync.Server.csproj` | class library | Sync server logic (referenced by `Novara.Server` and the tests) |

## Steps

### 1. Restore

```powershell
dotnet restore Novara.slnx
```

### 2. Build the editor bundles (DiaryEditorJs)

```powershell
cd DiaryEditorJs
npm install
npm run build   # esbuild → dist/tiptap.bundle.js and dist/md.bundle.js
```

The two bundles are copied into the app output by `Novara.csproj`.

### 3. Publish the main app

```powershell
dotnet publish Novara.csproj -c Release -r win-x64 --self-contained
```

### 4. Add `Novara.pri` (required!)

Unpackaged WinUI 3 apps need `Novara.pri` beside the executable; without it, the app crashes on launch with `0xc000027b`. Copy the generated `Novara.pri` from the build output into the publish folder.

### 5. Publish the helper processes

```powershell
dotnet publish StickNoteHost/StickNoteHost.csproj -c Release -r win-x64 --self-contained
dotnet publish NovaraMCP/NovaraMCP.csproj -c Release -r win-x64 --self-contained
```

`NovaraMCP.exe` is a single self-contained file — copy it next to `Novara.exe`. For the sticky-note host, copy its **entire publish output** into a `Host\` subfolder of the publish directory (exe, DLLs, `deps.json`, `runtimeconfig.json`, native libraries). Shipping the bare `StickNoteHost.exe` apphost alone crashes instantly on clean machines (Event 1023). A `resources.pri` inside `Host\` is harmless; one at the publish root is not.

Also verify `Microsoft.Graphics.Canvas.dll` and `Microsoft.Graphics.Canvas.Interop.dll` are present — the blur effects need them and degrade silently if they are missing.

The bundled sync server (8.0+) is published as a single compressed, self-extracting file, so the machine that runs it needs no .NET runtime:

```powershell
dotnet publish Novara.Server/Novara.Server.csproj -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

Copy `NovaraSync.exe` plus the `Novara.Web\` folder it serves at `/web` into a `Sync\` subfolder of the publish directory.

### 6. Package with Inno Setup

Empty the publish folder first: `dotnet publish` never deletes leftovers, and a stale `Novara.pri` from a previous build combined with new DLLs crashes every modified page. Then compile `Installer/setup.iss` with the Inno Setup compiler. The final installer bundles:

- `Novara.exe` + `Novara.pri` (add the `.pri` manually — the publish output does not include it)
- the `Host\` subfolder (complete StickNoteHost bundle)
- `NovaraMCP.exe`
- `snapshot-viewer.html` — the Novara Snapshot viewer template, loaded by the export flow (7.0+); it must reference `viewer.js` and `viewer.css`, which are inlined on export
- the `Sync\` subfolder — `NovaraSync.exe` and the `Novara.Web\` reader site (8.0+)
- `Assets\128.ico`

## Tests

```powershell
dotnet test Novara.Tests/Novara.Tests.csproj
```

To measure coverage the way CI does:

```powershell
dotnet test Novara.Tests/Novara.Tests.csproj --collect:"XPlat Code Coverage" --results-directory coverage
```

CI reads `line-rate` from the resulting Cobertura report and fails below 80%.

The test project covers the pure-logic core (`Novara.Core`): storage, crypto, MCP logic, and API probing.

## Output summary

| Artifact | Location |
|----------|----------|
| Installer | `Novara_Setup_x.x.x.exe` (from `setup.iss`) |
| Publish folder | `Novara.exe`, `Novara.pri`, `NovaraMCP.exe`, `snapshot-viewer.html`, `Host\StickNoteHost.exe`, `Sync\NovaraSync.exe`, `Sync\Novara.Web\`, `Assets\` |
