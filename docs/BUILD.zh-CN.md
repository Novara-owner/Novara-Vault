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

> **Windows SDK 路径说明：** `Novara.csproj` 把 `AppxMSBuildToolsPath` 固定为某台机器的 Visual Studio 路径。若在其他机器构建，请更新或删除该属性，以匹配本机的 Visual Studio / Windows SDK 目录。

## 工程结构

| 工程 | 产物 | 说明 |
|------|------|------|
| `Novara.csproj` | `Novara.exe` | 主 WinUI 3 应用（非打包、自包含） |
| `StickNoteHost/StickNoteHost.csproj` | `StickNoteHost.exe` | 桌面便签宿主进程 |
| `NovaraMCP/NovaraMCP.csproj` | `NovaraMCP.exe` | MCP 服务器（单文件） |
| `Novara.Core/Novara.Core.csproj` | 类库 | 纯逻辑（被应用与测试引用） |
| `Novara.Tests/Novara.Tests.csproj` | xUnit 测试 | 单元测试 |

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

把 `StickNoteHost.exe` 与 `NovaraMCP.exe` 部署到 `Novara.exe` 同目录。

### 6. 用 Inno Setup 打包

用 Inno Setup 编译器编译 `Installer/setup.iss`。最终安装包包含：

- `Novara.exe` + `Novara.pri`
- `StickNoteHost.exe`
- `NovaraMCP.exe`
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
| 发布目录 | `Novara.exe`、`Novara.pri`、`StickNoteHost.exe`、`NovaraMCP.exe`、`Assets\` |
