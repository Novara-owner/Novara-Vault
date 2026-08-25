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

Deploy `StickNoteHost.exe` and `NovaraMCP.exe` alongside `Novara.exe`.

### 6. Package with Inno Setup

Compile `Installer/setup.iss` with the Inno Setup compiler. The final installer bundles:

- `Novara.exe` + `Novara.pri`
- `StickNoteHost.exe`
- `NovaraMCP.exe`
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
| Publish folder | `Novara.exe`, `Novara.pri`, `StickNoteHost.exe`, `NovaraMCP.exe`, `Assets\` |
