# Building Novara from Source

<p align="center"><strong>English</strong> · <a href="BUILD.zh-CN.md">简体中文</a></p>

How to build, publish, and package Novara from source.

---

## Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| .NET SDK | **8.0.402** (pinned by `global.json`) | Build all C# projects |
| Windows SDK | 10.0.26100.0 | WinUI 3 target (see note below) |
| Node.js + npm | any recent LTS | Build the `DiaryEditorJs` editor bundles |
| Inno Setup | 6+ | Compile the installer (`Installer/setup.iss`) |

> **Windows SDK path note:** `Novara.csproj` pins `AppxMSBuildToolsPath` to a machine-specific Visual Studio path. If you build on a different machine, update or remove that property to match your local Visual Studio / Windows SDK layout.

## Project layout

| Project | Output | Notes |
|---------|--------|-------|
| `Novara.csproj` | `Novara.exe` | Main WinUI 3 app (unpackaged, self-contained) |
| `StickNoteHost/StickNoteHost.csproj` | `StickNoteHost.exe` | Desktop sticky-note host process |
| `NovaraMCP/NovaraMCP.csproj` | `NovaraMCP.exe` | MCP server (single-file) |
| `Novara.Core/Novara.Core.csproj` | class library | Pure logic (referenced by the app and tests) |
| `Novara.Tests/Novara.Tests.csproj` | xUnit tests | Unit tests |

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

### 6. Package with Inno Setup

Empty the publish folder first: `dotnet publish` never deletes leftovers, and a stale `Novara.pri` from a previous build combined with new DLLs crashes every modified page. Then compile `Installer/setup.iss` with the Inno Setup compiler. The final installer bundles:

- `Novara.exe` + `Novara.pri` (add the `.pri` manually — the publish output does not include it)
- the `Host\` subfolder (complete StickNoteHost bundle)
- `NovaraMCP.exe`
- `snapshot-viewer.html` — the Novara Snapshot viewer template, loaded by the export flow (7.0+)
- `Assets\128.ico`

## Tests

```powershell
dotnet test Novara.Tests/Novara.Tests.csproj
```

The test project covers the pure-logic core (`Novara.Core`): storage, crypto, MCP logic, and API probing.

## Output summary

| Artifact | Location |
|----------|----------|
| Installer | `Novara_Setup_x.x.x.exe` (from `setup.iss`) |
| Publish folder | `Novara.exe`, `Novara.pri`, `NovaraMCP.exe`, `snapshot-viewer.html`, `Host\StickNoteHost.exe`, `Assets\` |
