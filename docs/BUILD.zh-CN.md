# 从源码构建 Novara

<p align="center"><a href="BUILD.md">English</a> · <strong>简体中文</strong></p>

如何从源码构建、发布并打包 Novara。

---

## 前置要求

| 工具 | 版本 | 用途 |
|------|------|------|
| .NET SDK | **8.0.402**（由 `global.json` 锁定） | 构建所有 C# 工程 |
| Windows SDK | 10.0.26100.0 | WinUI 3 目标（见下方说明） |
| Node.js + npm | 任意近期 LTS | 构建 `DiaryEditorJs` 编辑器 bundle |
| Inno Setup | 6+ | 编译安装脚本（`Installer/setup.iss`） |

> **Windows SDK 路径说明：** `AppxMSBuildToolsPath` 指向 WinUI 生成 PRI 步骤所用的 Appx MSBuild 任务。本仓库**不包含**该属性——它属于**本机构建设置**而非工程设置——因此构建会回落到 .NET SDK 自带的路径。若你的 SDK 没有 `Microsoft.Build.Packaging.Pri.Tasks.dll`，会报 `MSB4062 ... ExpandPriContent`；此时可在命令行传入该属性（或写成环境变量），指向你本机 Visual Studio 的 `AppxPackage` 目录。

## 工程结构

| 工程 | 产物 | 说明 |
|------|------|------|
| `Novara.csproj` | `Novara.exe` | 主 WinUI 3 应用（非打包、自包含） |
| `StickNoteHost/StickNoteHost.csproj` | `StickNoteHost.exe` | 桌面便签宿主进程 |
| `NovaraMCP/NovaraMCP.csproj` | `NovaraMCP.exe` | MCP 服务器（单文件） |
| `Novara.Core/Novara.Core.csproj` | 类库 | 纯逻辑（被应用与测试引用） |
| `Novara.Tests/Novara.Tests.csproj` | xUnit 测试 | 单元测试 |
| `Novara.Server/Novara.Server.csproj` | `NovaraSync.exe` | 自托管同步服务端宿主（单文件自包含） |
| `Novara.Sync.Server/Novara.Sync.Server.csproj` | 类库 | 同步服务端逻辑（被 `Novara.Server` 与测试引用） |

## 步骤

### 1. 还原

```powershell
dotnet restore Novara.slnx
```

### 2. 构建编辑器 bundle（DiaryEditorJs）

```powershell
cd DiaryEditorJs
npm install
npm run build   # esbuild → dist/tiptap.bundle.js 和 dist/md.bundle.js
```

两个 bundle 由 `Novara.csproj` 复制进应用输出目录。

### 3. 发布主应用

```powershell
dotnet publish Novara.csproj -c Release -r win-x64 --self-contained
```

### 4. 补上 `Novara.pri`（必需！）

非打包的 WinUI 3 应用需要 `Novara.pri` 与可执行文件同目录；缺少它，应用启动即崩溃（`0xc000027b`）。把构建输出中的 `Novara.pri` 复制到发布目录。

### 5. 发布辅助进程

```powershell
dotnet publish StickNoteHost/StickNoteHost.csproj -c Release -r win-x64 --self-contained
dotnet publish NovaraMCP/NovaraMCP.csproj -c Release -r win-x64 --self-contained
```

`NovaraMCP.exe` 是单文件自包含 exe——直接复制到 `Novara.exe` 同目录。桌面便签宿主则要把 **publish 全部产物**（exe、DLL、`deps.json`、`runtimeconfig.json`、原生库）复制进发布目录的 `Host\` 子目录。只带裸 `StickNoteHost.exe`（apphost）在全新机器上即死（事件 1023）。`resources.pri` 位于 `Host\` 内无害，出现在发布目录根则致命。

同时核验 `Microsoft.Graphics.Canvas.dll` 与 `Microsoft.Graphics.Canvas.Interop.dll` 在发布目录中——模糊特效依赖它们，缺失时静默降级。

随包自带的同步服务端（8.0 起）以「单文件 + 压缩 + 自解压」发布，因此运行它的那台机器无需安装 .NET 运行时：

```powershell
dotnet publish Novara.Server/Novara.Server.csproj -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

把 `NovaraSync.exe` 与它在 `/web` 托管的 `Novara.Web\` 目录一并复制进发布目录的 `Sync\` 子目录。

### 6. 用 Inno Setup 打包

先清空发布目录：`dotnet publish` 从不删除残留文件，旧版本残留的 `Novara.pri` 混合新 DLL 会让所有改过页面的启动即崩。然后编译 `Installer/setup.iss`。最终安装包包含：

- `Novara.exe` + `Novara.pri`（publish 产物不含 pri，需手动补齐）
- `Host\` 子目录（StickNoteHost 完整自包含包）
- `NovaraMCP.exe`
- `snapshot-viewer.html` —— Novara Snapshot 查看器模板，导出流程加载（7.0 起）；它必须引用 `viewer.js` 与 `viewer.css`，导出时二者会被内联
- `Sync\` 子目录 —— `NovaraSync.exe` 与 `Novara.Web\` 阅读器站点（8.0 起）
- `Assets\128.ico`

## 测试

```powershell
dotnet test Novara.Tests/Novara.Tests.csproj
```

测试工程覆盖纯逻辑核心（`Novara.Core`）：存储、加密、MCP 逻辑与 API 探测。

## 产物汇总

| 产物 | 位置 |
|------|------|
| 安装包 | `Novara_Setup_x.x.x.exe`（来自 `setup.iss`） |
| 发布目录 | `Novara.exe`、`Novara.pri`、`NovaraMCP.exe`、`snapshot-viewer.html`、`Host\StickNoteHost.exe`、`Sync\NovaraSync.exe`、`Sync\Novara.Web\`、`Assets\` |
