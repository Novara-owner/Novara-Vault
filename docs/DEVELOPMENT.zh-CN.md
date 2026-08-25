# Novara 开发手册

<p align="center"><a href="DEVELOPMENT.md">English</a> · <strong>简体中文</strong></p>

> 本文档面向开发者与深度用户，完整介绍 Novara 的架构设计、核心机制与每一页的具体实现。
> 官网：<https://novara.xin> · 仓库：Novara-owner/Novara-Vault

---

## 目录

1. [项目简介](#1-项目简介)
2. [技术栈](#2-技术栈)
3. [解决方案结构](#3-解决方案结构)
4. [存储与数据模型](#4-存储与数据模型)
5. [安全与加密](#5-安全与加密)
6. [隐私锁与锁屏页](#6-隐私锁与锁屏页)
7. [欢迎页](#7-欢迎页)
8. [备忘页（BasicMemoPage）](#8-备忘页basicmemopage)
9. [路径备份页（FilePathPage）](#9-路径备份页filepathpage)
10. [计划页（PlanPage）](#10-计划页planpage)
11. [记录页与编辑器（DiaryPage / DiaryEditorPage）](#11-记录页与编辑器diarypage--diaryeditorpage)
12. [设置页（SettingsPage）](#12-设置页settingspage)
13. [全局搜索（SearchPage）](#13-全局搜索searchpage)
14. [回收站（TrashPage）](#14-回收站trashpage)
15. [桌面便签（StickNoteHost）](#15-桌面便签sticknotehost)
16. [MCP Agent 接口](#16-mcp-agent-接口)
17. [主题与国际化](#17-主题与国际化)
18. [提醒系统](#18-提醒系统)
19. [导入与导出](#19-导入与导出)
20. [API 连通性检测](#20-api-连通性检测)
21. [窗口、托盘与单实例](#21-窗口托盘与单实例)
22. [底层服务详解](#22-底层服务详解)
23. [构建与发布](#23-构建与发布)
24. [测试](#24-测试)
25. [目录结构](#25-目录结构)
26. [版本历史](#26-版本历史)

---

## 1. 项目简介

Novara 是一款**本地优先的个人知识管理四合一工具**（Windows 桌面应用，基于 WinUI 3）。

四大标签页 + 设置页 + 可选隐私锁：

| 标签页 | 功能 |
|--------|------|
| **备忘** | 分组管理账号 / 密码 / API Key / 邮箱 / 网站 / 银行卡 / WiFi / 证件 / 自定义等条目；双击复制、星标置顶、API 连通检测 |
| **路径备份** | 登记本地文件 / 文件夹路径，一键检测存在性 / 有效性（绿 / 红状态）、复制 / 打开 |
| **计划** | 待办 + 便签卡片（星标 / 置顶 / 排序 / 展开）；「发送到桌面」独立便签；时间提醒 |
| **记录** | 富文本日记（HTML）+ Markdown 文档双格式编辑器、时间线回顾、筛选 |

核心设计理念：

- **本地优先**：所有数据存本地单文件（`%LocalAppData%\Novara\data.novadb`），无云同步、无遥测。
- **可选加密**：AES-256-GCM + PBKDF2，隐私锁开启时整库加密。
- **隐私锁**：可选，锁屏密码 + 5 次失败锁定时长递增；支持 Windows Hello 解锁。
- **五语言**：中文简体 / 中文繁体 / English / 한국어 / 日本語。
- **主题**：浅色 / 深色 / 跟随系统。
- **桌面便签**：独立进程 `StickNoteHost`，主程序「发送到桌面」联动。
- **MCP 接口**：对外提供 14 个工具的本地 MCP 服务，供 AI Agent 读写数据。
- **单文件分发**：.NET 自包含 + Inno Setup 安装向导。

---

## 2. 技术栈

| 层 | 技术 |
|----|------|
| UI 框架 | WinUI 3（Microsoft.WindowsAppSDK 1.6） |
| 运行时 | .NET 8（`net8.0-windows10.0.26100.0`，最低 Windows 10 2004） |
| 语言 | C# |
| 富文本编辑器 | WebView2（Tiptap 富文本 + markdown-it Markdown） |
| 托盘 | H.NotifyIcon.WinUI |
| 存储 | 单文件 JSON（`NovaraStore`），可选 AES-GCM 加密 |
| 桌面便签 | 独立进程 `StickNoteHost`（FileSystemWatcher 同步） |
| MCP | `NovaraMCP.exe`（stdio 前端）+ 命名管道 |
| HTML 解析 / 净化 | AngleSharp |
| 测试 | xUnit（`Novara.Tests`，引用纯逻辑库 `Novara.Core`） |
| 构建 | MSBuild / `dotnet publish` 自包含；安装包 Inno Setup |

---

## 3. 解决方案结构

解决方案 `Novara.slnx` 包含五个工程：

```
Novara/
├── Novara.csproj        主程序（WinUI 3 UI 层）
├── Novara.Core/         纯逻辑库（net8.0 无 WinUI，可被 xUnit 引用）
├── Novara.Tests/        xUnit 单元测试
├── StickNoteHost/       桌面便签独立进程（无主窗口、托盘常驻）
└── NovaraMCP/           MCP stdio 前端（纯 net8.0 控制台，单文件发布）
```

**分层原则**：

- `Novara.Core` 是「纯逻辑」层：Models / CryptoService / ApiProbeService / ApiChatClient / ApiDiagnoseService / RelayProbeService / ProbeDataSetLoader / PasswordService / NovaraStore / McpLogic / CsvImportExportService / Loc / CoreEnv。无任何 WinUI 依赖，可独立单测。
- 主工程 `Services/` 是「UI 相关服务」：StartupService / StickySync / ContextMenuService / CrashLogger / AutoBackupService / McpService / WindowsHelloService / ToastService / ReminderScheduler / ChunkedRender / HtmlSanitizer / RelayCommand 等。
- `Pages/` 是九大页面：BasicMemoPage / FilePathPage / PlanPage / DiaryPage / DiaryEditorPage / SettingsPage / LockScreenPage / SearchPage / TrashPage。

**核心解耦方式**：

- **i18n 注入**：`Novara.Core` 通过 `Loc.T` 注入主程序的 `App.GetString`，逻辑库不直接依赖 UI 资源。
- **数据目录注入**：`CoreEnv.DataDirName` 提供数据目录名（Debug 为 `Novara-Dev`，Release 为 `Novara`），实现开发 / 发布环境隔离。

---

## 4. 存储与数据模型

### 4.1 统一单文件

所有用户数据存 `%LocalAppData%\Novara\data.novadb`，内部是固定 JSON 结构，含七大隔离分区：

| 分区 | 内容 | 对应模型 |
|------|------|---------|
| `DiaryItems` | 日记 / Markdown 文档 | `DiaryEntry` |
| `PathBackupItems` | 路径备份条目 | `FilePathEntry` |
| `TodoCards` | 待办卡片 | `TodoCard` |
| `NoteCards` | 便签卡片 | `NoteCard` |
| `MemoGroups` | 备忘分组 | `MemoGroup` |
| `MemoEntries` | 备忘条目 | `MemoEntry` |
| `AppSettings` | 应用设置 | `AppSettings` |

**硬约束：禁止跨分区查询**——各页只读写对应分区。

### 4.2 文件头格式

文件头固定 22 字节：

```
[4B "NOVA"] [1B version] [1B 加密标记] [16B MD5]
```

- 数据体明文 = JSON；加密 = GZip + AES。
- 写入后设置 `Hidden | ReadOnly` 属性，独占锁保护。

### 4.3 读写机制

- **启动**：一次性全量加载进内存（`App.Store` 静态单例）。
- **写盘**：每次增删改即时后台异步完整落盘——`SemaphoreSlim` 串行 + 最新快照合并 + 300ms 去抖 + `Task.Run`；写盘用 `tmp + Move` 原子替换 + `Flush(true)`。
- **退出**：强制同步兜底（`SaveSync`）。
- **安全阀**：加密 / 损坏状态下禁止 `Save`（防覆盖）。
- **启动检测**：文件不存在 → 静默新建空白库；损坏 → 弹窗确认后删三文件重建。

### 4.4 数据模型详解

**MemoGroup（备忘分组）**：`Id` / `Name` / `IconKey` / `CreatedAt` / `IsStarred` / `IsPinned` / `PinnedAt` / `IsDeleted` / `DeletedAt` / `Order`。

**MemoEntry（备忘条目）**：`Id` / `GroupId`（外键，null=未分组）/ `Name` / `Type` / `KeyInfo` / `Fields`（`EntryField` 列表）/ `IconKey` / `CreatedAt` / `IsStarred` / `IsPinned` / `IsDeleted` / `Protocol`（API 检测成功状态）。

**EntryField（条目字段）**：`Label` / `Value` / `CanCopy`（是否可一键复制，敏感字段为 true）。

**FilePathEntry（路径条目）**：`Id` / `Name` / `Path` / `Note` / `CreatedAt` / `IsStarred` / `IsPinned` / `IsDeleted` / `Order`。

**TodoCard（待办）**：`Id` / `Title` / `IconKey` / `MainText` / `SubTexts` / `CheckedStates` / `CreatedAt` / `IsStarred` / `IsPinned` / `IsDeleted` / `Order` / `ReminderAt` / `ReminderSetAt`。

**NoteCard（便签）**：`Id` / `Title` / `IconKey` / `Content` / `CreatedAt` / `IsStarred` / `IsPinned` / `IsDeleted` / `Order` / `ReminderAt` / `ReminderSetAt`。

**DiaryEntry（日记/文档）**：`Id` / `Title` / `Content` / `CreatedAt` / `ModifiedAt` / `IsPinned` / `PinnedAt` / `IsStarred` / `IsDeleted` / `DeletedAt` / `Order` / `Format`（"html"=富文本日记默认 / "markdown"=MD 文档，零迁移）。

**AppSettings（设置）**：主题 / 语言 / 自启 / 右键菜单 / 关闭行为 / 可见标签页 / 隐私锁 / 欢迎页 / 备份 / MCP 等（详见 4.6）。

### 4.5 数据约定

- **备忘分区**：扁平列表 + 外键关联（`MemoGroup.Id` / `MemoEntry.GroupId`，null=未分组），禁止嵌套。删除分组时组内条目 `GroupId` 置 null（救援为独立条目）。
- **存储值语言无关**：内部枚举 / 状态值（Type、Theme、CloseBehavior、VisibleTabs）**维持中文字面量落盘**，显示文案由 UI 层翻译。未来版本禁止向存储写入语言相关字面量。
- **软删除**：五类实体（备忘条目 / 路径 / 待办 / 便签 / 日记）均加 `IsDeleted / DeletedAt` 软删，进回收站。
- **排序**：待办 / 便签 / 日记用 `Order` 字段（0 = 未手动排序走时间倒序；>0 = 用户拖拽排定顺序）。
- **新卡片落位**：新建卡片一律插「非置顶区最前」（置顶卡之后、普通卡之前）。

---

## 5. 安全与加密

### 5.1 加密算法

- **加密**：AES-256-GCM（v2）。nonce 12 字节 + tag 16 字节，认证靠 GCM tag，MD5 字段置零不校验。
- **密钥派生**：PBKDF2-SHA256，100000 次迭代。
- **旧版兼容**：2.0 旧数据为 AES-CBC（v1），解锁后自动迁移到 v2 GCM。

### 5.2 版本矩阵

| 版本 | 含义 |
|------|------|
| v1 明文 | 恒为 v1（2.0 兼容），无加密 |
| v1 CBC | 2.0 旧加密，解锁后弹一次迁移确认 → 升级 v2 |
| v2 GCM | 当前加密方案 |

### 5.3 密码哈希独立存储

密码哈希存独立明文小文件 `security.dat`（双盐：哈希盐 + 派生盐，加盐 SHA256），**不随库加密**——因为锁屏阶段无密钥可读。库加密时，密码哈希必须能独立读取以验证解锁。

### 5.4 改密事务

改密是**事务**：先重加密 `data.novadb`（`Reencrypt`）再切 `security.dat`，任一步失败保持双文件密码一致。忘记密码 = 清空全部数据重建空库，无后门。

---

## 6. 隐私锁与锁屏页

### 6.1 启动验证流程

```
读加密标记
  ├─ 明文 → 直接加载 → 进主界面
  └─ 加密 → 读 security.dat → 显示锁屏页（跟随系统主题）
           → 验密 → 派生密钥解密加载 → 读 AppSettings 全局刷新 → 进主界面
```

### 6.2 锁屏页（LockScreenPage）实现

**布局**（垂直居中 StackPanel）：

- 「隐私锁」标题（主题色，跟随主题）。
- 密码输入框（单个 PasswordBox，长密码，自定义模板让文字垂直居中 + Reveal 眼睛 + 占位符）。
- 「Windows Hello 解锁」文字（仅开启 Hello 时显示，品牌色，可点击，呼吸动效）。
- 解锁按钮（圆形品牌蓝按钮，箭头图标）。
- 「忘记密码」文字（页面最底部，距底 30px）。

**核心逻辑**：

- **密码提交**：回车 / 解锁按钮 → `VerifyAsync` → `LoadWithPassword`。
- **自动探测**：输入达 6 位防抖 400ms 自动尝试；每运行上限 64 次；探测失败不计数，显式提交才计数。
- **错误反馈**：错误红闪震动（`ShakeAndFlashAsync`），正确绿框 + 内容飞走动画（`PlayExitAnimation`：标题左移 -240、输入框右移 +240、Windows Hello 文字左移、忘记密码下移）。
- **5 次锁定**：连续 5 次错误 → 红色倒计时呼吸（30 分钟，`lockout.dat` 持久化重启恢复）。
- **窗口失焦清空密码**（防旁观，规则 G13）。
- **Windows Hello 解锁**：点击「Windows Hello 解锁」→ `RequestVerificationAsync` 弹原生 Hello → `PasswordVault` 读回密码 → `LoadWithPassword` 复用解锁链路。失败复用 `ShakeAndFlashAsync`。开启时自动尝试一次（延迟 1s，等入场动画）。
- **忘记密码**：确认弹窗 → 清空三文件 → 重建空白明文库。

---

## 7. 欢迎页

欢迎页是首次启动（或开启「欢迎页展示」时）的全屏覆盖层。

**布局**：

- Logo（品牌图片，居中）。
- 「Novara」标题（双层：阴影文字 + 主文字，Century Gothic 80px）。
- 副标题（品牌宣传语，宽字距）。
- 「点击任意处开始」提示（呼吸动效）。

**动画**：

- 入场动画：logo 淡入、标题左滑入、副标题右滑入、hint 淡入（错峰 BeginTime）。
- hint 呼吸：Opacity 0.3↔0.7 循环。
- 渐出动画：点击后 Overlay 线性淡出 400ms，然后停呼吸动画 → 进入主内容 → 导航栏渐入。

**响应式**：`UpdateWelcomeLayout` 根据窗口高度缩放各元素尺寸 + 用 Transform 定位（`WelcomeLogoTransform.Y = -0.2875 * h` 等）。

**展示逻辑**：

- `WelcomeOnLaunch = true`：每次双击启动都展示。
- `WelcomeOnLaunch = false` + `HasCompletedWelcome = true`：跳过欢迎页直接进主内容。
- 点击欢迎页后写 `HasCompletedWelcome = true` 并同步落盘。

---

## 8. 备忘页（BasicMemoPage）

标签页 1，管理备忘分组与条目。

### 8.1 功能

- 分组管理 + 条目管理 + 搜索 + 弹窗编辑 + API 连通检测。
- 分组 / 条目星标置顶角标（仅对应卡片）；置顶卡排最前。
- 双击条目复制敏感字段。
- 分组删除：组内条目救援为独立条目（GroupId=null）。
- 未分类卡永远置底，分组卡绝不出现在它下面。

### 8.2 条目类型与字段

| 类型 | 必填字段 | 其他字段 |
|------|---------|---------|
| 邮箱 | 地址 | 密码 / 备注 |
| 账户 | 账号 | 密码 / 网址 / 备注 |
| API Key | 名称 | Key / URL / 模型 ID / 备注 |
| 网站 | 网址 | 账号 / 密码 / 备注 |
| 银行卡 | 卡号 | 持卡人 / 有效期 / CVV / 密码 / 备注 |
| WiFi | 网络名 | 密码 / 备注 |
| 证件 | 证件号 | 姓名 / 签发机构 / 有效期 / 备注 |
| 自定义 | 名称 | 自由字段 |

- 表单槽位 `FormField2~7`（7 行）。
- 编辑回显按**字段标签映射**（`FieldSlotFor(type, label)`）而非位置，兼容旧数据。
- 自定义类型未主动选图标时**随机分配**一个 Group 图标（不复用旧拼图默认）。
- 敏感字段 `CanCopy = true`（卡号 / CVV / 密码 / 网络名 / 证件号 / 账号 / 网址等）。

### 8.3 核心数据结构

- `_groupIds` / `_entryIds`：Border → Guid 映射（内存 key 稳定，持久化用 GUID）。
- `_starredCards` / `_pinnedGroupCard` / `_pinnedEntryCard`：星标 / 置顶状态。
- `_standaloneEntries` / `_entriesInGroup`：未分类条目 / 组内条目列表。
- `_targetGroupCard` / `_pendingMoveEntry` / `_editingEntryCard`：弹窗操作标记（Hide 完成 + Unloaded 双重清理）。

### 8.4 交互规则

- 弹窗打开：透明 Scrim 点击空白关闭；弹窗内点击不关闭。
- API Key 卡片：第二行显示 URL；右键含「检测连通性」。
- 移入 / 移出：`MenuFlyoutSubItem` 二级子菜单（分组图标 + 未分类）。
- 拖拽排序：幽灵副本跟手，真卡片不动（稳定版，详见 10.5）。
- 新条目落位：置顶之后、未分类卡之前（组内插最前）。

---

## 9. 路径备份页（FilePathPage）

标签页 2，登记本地文件 / 文件夹路径。

### 9.1 功能

- 路径卡片：名称 / 路径 / 备注三行。
- 路径有效性检测：绿色（`#4CAF50`）/ 红色（`#FF4545`）边框。
- 检测五触发：创建 / 编辑验证、30 分钟定时、启动检测、空白区全量检测、单卡右键检测。
- 复制路径 Toast「已复制」2 秒消失；一键打开 `explorer /select` 高亮。
- 新建 / 编辑弹窗有「选择文件 / 选择文件夹」双按钮（Picker，`InitializeWithWindow` 绑主窗口句柄）。
- 路径输入自动去引号，无效 / 空白拒绝创建。
- 置顶单卡首位；拖拽排序。

### 9.2 核心实现

- `_autoCheckTimer`：30 分钟 DispatcherTimer 全量检测。
- `_cardBaseBorderColor`：卡片基准边框色（hover 恢复用）。
- 卡片 hover：仅该卡边框变色 + 上移（独立画刷副本，不污染其他卡）。
- 拖拽排序：幽灵副本（同计划页稳定版）。

---

## 10. 计划页（PlanPage）

标签页 3，待办 + 便签卡片。

### 10.1 待办卡片

**卡片结构**：标题行（图标 + 标题 + 置顶 + 星标 + 完成徽标 + 展开按钮）+ 主体（主待办行 + 子待办行）。

**勾选联动**（联动触发点唯一：子待办勾选状态变化）：

- 子全勾 → 主自动勾 + 折叠。
- 子全勾后取消任意子 → 主同步取消 + 强制展开。
- 手动取消主 → 不回写子（P5）。
- 完成判定 = 全部条目均勾选。

**折叠规则**：

- 存在未勾选条目 → 强制展开。
- 全部勾选完成 → 默认折叠；手动可展开。
- 无子待办的卡片：不显示展开按钮，勾选即完成即折叠。

**完成徽标**（三色）：全部勾选完成时，标题行显示三色「完成」徽标（浅品牌蓝 `#8C93FF` 旗帜 + 品牌蓝 `#7276FF` 旗帜 + 白色对勾，3 个 Path 原始坐标叠放 + Viewbox 缩放）。

**编辑重建迁移**：创建时间原样保留；星标 / 置顶保留；勾选按逐条内容对比继承（主文本变只重置主、子项变只重置该行、新增默认未勾选）。

### 10.2 便签卡片

- 内容超长（>240 字）或含换行 → 展开按钮；展开状态 `IsExpanded` 持久化。
- 右键菜单顺序：星标 → 置顶 → 发送/取消桌面 → 编辑 → 分隔 → 删除。

### 10.3 时间提醒

待办 / 便签卡右键「设置提醒」→ 卡片边框渐变（绿→红 HSV 插值）+ 系统级提醒 + Toast + 桌面提醒卡（详见 18）。

### 10.4 筛选与排序

- 筛选：混合 / 待办 / 便签（按 Visibility 真过滤，筛选视图禁用拖拽）。
- 拖拽排序：`Order` 字段落盘。
- 新卡片落位：非置顶区最前（`AssignNewCardOrder`）。

### 10.5 卡片拖拽排序（四页复用稳定版）

- 真卡留在列表不动（边框品牌蓝 + Opacity 0.35）。
- 跟手的是幽灵副本（RenderTargetBitmap 截图 Image，覆盖层 Canvas IsHitTestVisible=False）。
- 拖拽动画手动逐帧插值（async Task.Delay），禁用 Storyboard。
- 自动滚动用 `CompositionTarget.Rendering`。
- 落点约束：置顶卡不可拖；原位重置 `_dropIndex`。

---

## 11. 记录页与编辑器（DiaryPage / DiaryEditorPage）

标签页 4（「记录」），承载「HTML 富文本日记」+「Markdown 文档」双格式。

### 11.1 列表页（DiaryPage）

- 排序：置顶优先 + 修改时间倒序。
- 星标 / 置顶切换增量更新（不重建列表）。
- MD 文档卡片加金色「文档」徽标（16px），HTML 日记加「日记」徽标。
- 筛选：混合 / 日记 / 文档（融入「记录」标签箭头，真过滤，筛选视图禁用拖拽）。
- 新建入口：空白右键 → 新建文档 / 新建日记 / 导入文档。
- 导出：右键「导出▸」子菜单——HTML 日记「导出 MD（含图）/（无图）」，MD 文档「导出 Markdown（原文）」。

### 11.2 编辑器（DiaryEditorPage）

**编辑器分流**（都是 WebView2 承载）：

- `format=html` → WebView2 + Tiptap 富文本编辑器（一字不动）。
- `format=markdown` → WebView2 + markdown-it 渲染（`html:false` 防 XSS），GitHub 式 Write ↔ Preview 切换。

**HTML 富文本工具栏**：加粗 / 斜体 / 下划线 / 删除线 / 颜色 / 对齐 / 标题 / 列表 / 引用 / 链接 / 代码块 / 图片插入等（胶囊悬浮工具栏）。

**MD 工具栏**（16 按钮）：标题 H1-H4 / 粗体 / 斜体 / 列表 / 引用 / 链接 / 代码块 / 行内代码 / 分隔线 / 表格 / 清除格式 / 撤销 / 重做 + Write/Preview 切换。

**安全**：

- HTML 保存 / 加载**双净化**（`HtmlSanitizer` 白名单：b/strong/i/em/u/s/span/font/div/br/p/img/ul/ol/li/a/pre/code/hr/blockquote/h1-h6；移除全部 `on*` 事件属性含实体编码变体、`javascript:` / `vbscript:` / 非图片 `data:` 协议）。
- 标题用 textContent 纯文本渲染（防元素化/脚本）；上限 120 字符（去空白，JS keydown/paste 拦截 + C# 兜底）。
- 单篇图片总大小上限 20MB（base64 内嵌存储膨胀防线）。

**保存守卫**：

- `_webViewReady=false` 禁止保存（防内容覆写）。
- JS 执行失败返回 null 放弃保存（防空白覆盖）。
- 退出时保存编辑器脏内容（Closing 先 Cancel 后异步保存再放行）。
- 脏检查：标题 / 正文快照对比，未变则不保存 / 不弹「已修改」。

---

## 12. 设置页（SettingsPage）

设置页是卡片式布局，主要卡片：

| 卡片 | 功能 |
|------|------|
| 数据概览 | 条目 / 分组 / 待办完成率 / 存储占用（默认关，开启后默认展开） |
| MCP 接口 | 总开关 + 授权列表 + 配置（详见 16） |
| 显示模式（主题） | 浅色 / 深色 / 跟随系统（存储 + 重启闭环） |
| 语言 | 五语言切换（存储 + 重启闭环） |
| 自定义选项卡 | 勾选 / 取消四标签页显示（至少保留 1 个） |
| 开机自启 | 注册表 HKCU Run 键（显示真实状态） |
| 全局右键菜单 | 桌面 / 文件右键菜单开关 |
| 窗口退出行为 | 直接退出 / 托盘驻留 |
| 隐私访问锁 | 设密 / 改密 / 关锁 / 警告四弹窗 |
| 数据备份 | 快照 / 恢复 / 自动备份 |
| 数据归档与还原 | 导入 / 导出（原生 / CSV / MD / HTML / PDF） |
| 资料库重置 | 高危确认 → 删三文件重建 |
| 官网 | 打开 novara.xin |

**隐私锁四弹窗**：

- A 设密：6 位两次一致 → 写 security.dat → 内存全量加密即时写盘。
- B 改密：验证原密码 → **改密事务**（先 Reencrypt 再切 security.dat）。
- C 关锁：解密为明文写盘 → 删 security.dat。
- D 警告：红色文案 + 红色三态确认键。

**导入导出**：导出恒明文（含 MD5 头）；有锁时均需密码验证；导入不覆盖 security.dat / lockout.dat；导入后 ReloadPages + 语言/主题变化提示重启生效。

---

## 13. 全局搜索（SearchPage）

全局搜索（Ctrl+K）覆盖备忘 / 路径 / 待办 / 便签 / 日记五类实体。

### 13.1 索引化

打开搜索页时一次性预构建索引（`RebuildIndex`，所有非软删实体的可搜文本小写），后续每次回车只遍历索引匹配，不再现场拼接字符串。

### 13.2 模糊搜索

- query 按空格分词，所有词 AND 命中（`MatchesQuery`）。
- 词内先精确子串，再**子序列模糊**（`ContainsFuzzy`，`memo` 命中 `memorandum`，中文按字匹配）。

### 13.3 排序与渲染

- 相关性排序：标题命中优先，同组时间倒序。
- 分批渲染：`ChunkedRender` 分帧（每批 8 张），避免一次性 reparent + 动画卡顿。
- 来源标签：卡片右上角品牌色小标签（备忘 / 路径 / 计划 / 日记 / 文档）。
- 来源筛选：混合 / 备忘 / 文件 / 计划 / 日记 / 文档。
- 点击结果跳转 + 目标卡闪烁（缩放脉动 + 边框品牌色闪 + 恢复原色）。

---

## 14. 回收站（TrashPage）

- 五类实体加 `IsDeleted / DeletedAt` 软删。
- TrashPage 独立整页：混合排序（按删除时间）/ 恢复 / 永久删除 / 清空。
- 每行卡片右侧「恢复」（品牌蓝边框）/「永久删除」（红边框）两按钮。
- 7 天启动自动清理过期软删（`TrashPage.CleanupExpired`）。
- 分组不进回收站（物理删，条目先救援到未分类）。
- 恢复 / 导出均不包含软删数据。
- 恢复清除星标 / 置顶状态（不继承关系）。

---

## 15. 桌面便签（StickNoteHost）

### 15.1 架构

`StickNoteHost.exe` 是**独立常驻进程**（无主窗口、托盘常驻）。主程序只负责写 `stickies.json` + 唤起 Host。

```
stickies.json（数据目录，避开 data.novadb 独占锁）:
{
  theme, language,
  notes: [{ id, kind, title, content, dueTime, items }]
}
```

- `id` = 数据库 `NoteCard.Id`。
- `kind` = note（便签）/ todo（待办）/ reminder（提醒）。
- `items` = 待办子项（`StickyTodoItem{ label, checked }`，`kind=todo` 时携带，双向同步）。

### 15.2 同步机制

- **主程序 → Host**：增删改 → 写 `stickies.json` → Host `FileSystemWatcher` 监听（回调在工作线程，必须捕获 UI 线程 DispatcherQueue）→ 刷新窗口。
- **内容驱动生命周期**：json 有卡 → 自启弹卡；全空 → 自退。
- **Host → 主程序**（反向）：待办勾选写回 `stickies.json`（300ms 去抖），主程序 watcher 同步更新卡片。

### 15.3 便签交互

- 整卡拖动 + 边缘 8px 缩放（纯 XAML 指针事件 + `SetWindowPos`，**禁用系统拖动循环**——合成器吞消息）。
- 锁定置顶（TOPMOST，Activated 里 SetWindowPos 刷新加固）。
- 无边框（`IsResizable=false`）；Alt+Tab 不可见（`TOOLWINDOW`）。
- 主题 / 语言跟随：`stickies.json.theme/language` 快照 + watcher，Host 永不因主题重启。
- 编辑跳转 IPC：Host 右键「编辑」→ 写 pending-edit.json + 命名事件 `Local\Novara.EditRequest(.Dev)` → 主程序切计划页打开编辑弹窗（未运行则自动启动，冷启动重试 ~4s）。

### 15.4 提醒卡

`kind=reminder + dueTime`；300×180 右下角出现向左堆叠；倒计时红字每秒跳动；到期 `MessageBeep×3` + Toast + 手动脉冲变红 → 自动关闭删数据。

### 15.5 卡片视觉

- 淡描边（主题边框色，1px，与主程序卡片一致）。
- 标题品牌蓝（便签 / 待办卡，保留品牌元素）。
- 待办卡复选框：选中品牌蓝 + 白勾，未选中圆角淡边框。

---

## 16. MCP Agent 接口

Novara 提供本地 MCP 服务，让任何 MCP 客户端（AI Agent）原生发现并调用工具，读写 Novara 数据。

### 16.1 架构

```
MCP 客户端 (stdio)
    ↓ JSON-RPC
NovaraMCP.exe（纯转发 stdio 前端，手写 JSON-RPC：initialize / tools/list / tools/call / ping）
    ↓ 命名管道 Novara.Mcp
主进程 McpService（未解锁门控 → token 鉴权 → 进程白名单 → 脱敏 → CRUD）
```

### 16.2 工具清单（14 个）

| 类别 | 工具 |
|------|------|
| 创建 | create_memo / create_todo / create_note / create_diary / create_path |
| 更新 | update_memo / update_todo / update_note / update_diary / update_path |
| 查询 | list_items / read_item / search_items |
| 删除 | delete_item（软删，默认关闭，需设置页开启） |

### 16.3 安全模型

- **隐私锁门控**：库未解锁时一律拒绝。
- **token 鉴权**：总开关默认关，开启自动生成 token（Base64Url 32B）。
- **进程白名单**：首次连接弹窗确认，路径记入白名单。
- **脱敏**：敏感字段（密码 / API Key）脱敏输出；`update` 不可篡改已有敏感字段、可新增敏感字段。
- **写边界**：`create` / `update` / `delete`（软删）；回收站不暴露；`format` 不可改。

### 16.4 设置页 MCP 卡片

标题 + 折叠 / 配置 / 总开关三按钮 + 授权列表面板（空态居中提示、非空随内容，每行 = 路径 + 红色撤销）。四个弹窗：配置（Key 重置 / JSON 复制 / 删除权限下拉）/ 重置确认 / JSON 选择（JSON / 提示词）/ 删除权限确认。

---

## 17. 主题与国际化

### 17.1 主题

- 三种主题：跟随系统 / 深色 / 浅色。
- 切换走「存储 + 重启」闭环（**不做运行时热切换**）。
- 动态取色必须走 `App.GetBrush(key)`（按程序主题手动选字典），禁止直接索引 `Application.Current.Resources`，禁止缓存 Brush 引用。
- 品牌色统一 `#7276FF`（`AppPrimaryButtonBrush` / `AppAccentBrush` / `AppDialogBorderBrush` 三主题字典同值）。
- 按钮三级体系：主操作蓝（`#7276FF` → hover `#8C93FF` → pressed `#5855FF`）、次操作描边、危险红（`#CCFF4545` → `#FFFF4545` → `#CC3A3A`）。
- 弹窗按钮只三种：取消（描边）/ 确认蓝 / 确认红。
- 跟随系统实时联动：`UISettings.ColorValuesChanged` + 500ms 去抖 + UiQueue 封送。

### 17.2 国际化（i18n）

- **C# 静态字典 `AppResources.cs`**（5 语言 5 本字典，Key 全对齐）。
- `x:Bind` 静态方法绑定；MainWindow（不支持 x:Bind）用中文原文 + `Tag="Key"` 运行时 ApplyLocalizedTexts。
- 切换闭环：设置页语言下拉 → 落盘 → 弹窗确认 → 重启生效（**不做热切换**）。
- 异常兑底：配置损坏 → 系统语言 → 中文；Key 缺失 → 返回 key 占位不崩溃。
- 跟随系统：`AppLanguage` 空 = 跟随系统，读 `GlobalizationPreferences.Languages[0]` 映射（zh-Hant/TW/HK/MO→zh-TW、zh-*→zh-CN、en→en-US、ko→ko-KR、ja→ja-JP、其他→en-US 兜底）。
- 英文布局约束：自动宽度适配、禁固定硬宽、长文 TextWrapping。

---

## 18. 提醒系统

三层提醒：

| 层 | 机制 |
|----|------|
| 应用内 | 待办 / 便签卡右键「设置提醒」，卡片边框渐变（绿 `#00CC22` → 红 `#DD2222`，HSV 连续插值），到期弹窗 |
| 系统级 | `ReminderScheduler` 用 `schtasks` 注册一次性任务（`/sd` 日期 `yyyy/MM/dd`），到点拉起 `Novara.exe --reminder <id>` |
| Toast | `ToastService` 发 `AppNotification`（带系统音效），unpackaged 前置 AUMID 快捷方式 |

数据：`TodoCard` / `NoteCard` 加 `ReminderAt`（截止时间）+ `ReminderSetAt`（设置时刻）。

- 边框渐变：进度 `p = (Now - ReminderSetAt) / (ReminderAt - ReminderSetAt)` 夹 0~1，HSV 连续插值。
- 到期弹窗：卡片内容 + 「知道了」，只弹一次；点击清字段 + 删任务 + 复原。
- 运行中提醒经单实例 Mutex + `ReminderDueRequest`（pending json + 命名事件）转发；未运行则启动并解析参数弹提醒。
- 到期已过才开机 → 静默丢弃（设计如此）。
- 桌面提醒卡也接入 Toast + `MessageBeep×3` + 到期自动关闭删数据。

---

## 19. 导入与导出

| 格式 | 用途 | 说明 |
|------|------|------|
| 原生备份（.novabak） | 完整备份 / 恢复 | 恒明文（含 MD5 头）；有锁需密码验证；导入校验 + 回滚 + Id 去重 |
| CSV | 备忘导入 / 导出 | 兼容 KeePass / Bitwarden 三种方言，自动识别 |
| Markdown | 汇总 / 单篇 / 文档原文 | 只读不可导入 |
| HTML 合集 | 记录页合集 | 品牌 logo + 官网 + 宣传语 + 打印友好 CSS |
| PDF 合集 | 记录页合集 | 离屏 WebView2 `PrintToPdfAsync` 静默转 PDF（A4） |

- 导出恒明文；有锁时均需密码验证。
- 导入不覆盖 `security.dat` / `lockout.dat`。
- 隐私锁改密 / 关锁 / 忘记密码 / 重置 / 导入 / 损坏重建均同步增删 Windows Hello 凭据。
- CSV 导出流程：二次确认弹窗 → FileSavePicker(.csv) → UTF-8 BOM 写出。
- CSV 导入流程：FileOpenPicker(.csv) → 自动识别方言 → 预览弹窗 → 导入（全部扔未分类，不自动建组）。

---

## 20. API 三入口检测

备忘页 API Key 条目右键，从单一「检测连通性」扩展为三入口，核心逻辑全在 `Novara.Core/Services/`（纯逻辑、可单测）：`ApiProbeService`（入口一，原有）/ `ApiChatClient`（共享聊天客户端）/ `ApiDiagnoseService`（入口二）/ `RelayProbeService`（入口三）/ `ProbeDataSetLoader`（探针数据集）。

| 入口 | 服务 | 消耗 | 定位 |
|------|------|------|------|
| 接口连通检测 | ApiProbeService（原有） | 0 | 连通性 / 密钥校验 |
| 接口状态诊断 | ApiDiagnoseService | 1~3 请求 | 可达性 + 有无余额（推断）+ 元数据 + 延迟/TTFT |
| 中转站探针检测 | RelayProbeService | 十余请求 | 8 探针 + 加权评分 + 四级判定 |

- **厂商识别**：按 URL host 关键字分派模板（OpenAI / OpenRouter / Anthropic / Gemini / Azure / Groq / Together / Perplexity / 智谱 / 通义 / DeepSeek / Moonshot / Mistral / SiliconFlow / StepFun / 小米 MiMo / Ollama + generic 兜底）。
- **鉴权方式**：bearer / x-api-key / api-key / query / raw；小米 MiMo 用 `api-key` 头而非标准 Bearer；generic 回退链 Bearer → 裸 key × 多端点。
- **状态 9 态**：成功 / 密钥错 / 无额度 / 限流 / 无接口 / 权限 / 服务端错 / 网络错 / 未知。

### 入口二：接口状态诊断
- 可达性门 + 余额推断（错误码 + message 关键词 `insufficient/quota/billing/balance/credit/欠费/余额/额度`）+ 元数据 + 延迟/TTFT（强制 stream）。
- chat 端点推导（禁止硬编码 `/v1`）：models 端点父目录 + `/chat/completions`，各厂商前缀不同（智谱 `/api/paas/v4`、通义 `/compatible-mode/v1`、Groq `/openai/v1`；Ollama 与 Azure OpenAI 特殊处理）。

### 入口三：中转站探针（8 探针 + 评分）
- 8 探针：身份（响应元数据 `model` 字段，禁止问「你是谁」）/ 能力基准（题集）/ 格式遵从 / token 计费 / 工具调用 / 隐藏注入 / 响应投毒 / 长上下文截断。
- 加权评分（身份 0.20 / 能力 0.20 / 工具 0.15 / 计费 0.15 / 注入 0.10 / 投毒 0.10 / 格式 0.05 / 截断 0.05），skipped 剔除后重新归一化；四级结论：可信 ≥90 / 基本可信 ≥75 / 疑似 ≥50 / 高风险 <50。
- 抗作弊：随机 nonce、token 预算 2000（只算输出 token）、90s 全局超时、单探针容错、URL 白名单（防 SSRF）、小模型识别（能力/工具 FAIL 降 WARN）。
- 数据文件化：探针题集 + 投毒正则放 `ProbeDataSet.json`，替换后重启生效（不做热重载），内置兜底。

### 安全与日志
- `AllowAutoRedirect=false` 防 key 跨域泄漏；key 掩码 + 错误体脱敏；`ConnectTimeout=5s`。
- 诊断日志：`ApiChatClient` 每次 chat 落盘 `%LocalAppData%\Novara\relay-probe.log`（时间戳 / URL / 状态 / usage / 响应体截断）。

---

## 21. 窗口、托盘与单实例

- 默认「直接退出」，托盘驻留可选（设置页下拉）。
- `AppWindow.Closing` 拦截：驻留 → Cancel + Hide；直接退出 → 保存编辑器脏内容 + SaveSync 后放行。
- **单实例互斥**：命名 Mutex（`Local\Novara.SingleInstance(.Dev)`）+ FindWindow 按标题唤醒，第二实例静默退出；MCP 后台模式无窗口则发 `ShowWindowRequest` 唤醒信号。
- 右键菜单（桌面「打开 Novara」+ 文件 / 文件夹「添加到路径备份」）：HKCU\Software\Classes 用户级注册表键。
- 开机自启：HKCU Run 键（三重校验：值存在 + 展开环境变量 + exe 存在；路径带引号）。
- 设置页永久移除：变更存储路径 / 打开存储目录 / 资料库路径迁移（不暴露底层文件入口）。

---

## 22. 底层服务详解

### Novara.Core（纯逻辑）

- **NovaraStore**：`Load` / `LoadWithPassword` / `SaveAsync`（SemaphoreSlim 串行 + 快照合并 + 300ms 去抖）/ `SaveSync` / `EnableEncryption` / `DisableEncryption` / `Reencrypt`（失败回滚）/ `ExportBackup`（恒明文 + 软删过滤）/ `ImportBackup`（校验 + 回滚 + 归一化 + Id 去重）/ `ResetDatabase`；写盘 tmp+Move 原子替换 + Flush(true)。
- **CryptoService**：`Encrypt`（v1 CBC）/ `Decrypt` + `EncryptGcm` / `DecryptGcm`（v2）；PBKDF2-SHA256 100000 迭代。
- **PasswordService**：`security.dat` 双盐 + 加盐 SHA256 固定时间比较；`lockout.dat`（FailCount/Until/Enabled）；`SetBaseDir` 路径注入。
- **ApiProbeService**：入口一，厂商识别 + 协议矩阵 + 错误分类 + 脱敏（见 20）。
- **ApiChatClient**：共享聊天客户端（OpenAI/Anthropic/Gemini 三协议 + 流式 + usage/TTFT + chat 端点推导）。
- **ApiDiagnoseService**：入口二，可达性 + 余额推断 + 元数据 + 延迟（见 20）。
- **RelayProbeService**：入口三，8 探针 + 加权评分 + 四级判定 + 抗作弊（见 20）。
- **ProbeDataSetLoader**：探针数据集加载 + 校验 + 内置兜底。
- **McpLogic**：14 工具纯逻辑（token 鉴权 / 脱敏 / CRUD），可单测。
- **CsvImportExportService**：导出 + 解析（自动识别三种方言表头 + 按类型模板建字段）。
- **Loc**：i18n 注入委托。
- **CoreEnv**：数据目录名（Debug/Release 隔离）。

### Services（UI 相关）

- **StartupService**：注册表 HKCU Run 键的 Enable/Disable/IsEnabled（三重校验）。
- **StickySync**：`stickies.json` 读写（tmp+Move 原子 + 命名互斥锁）；`FindHostExe`（发布=同目录；开发=逐级上溯 bin）；主题 / 语言 / 发送到桌面 / 清空；反向通道 FileSystemWatcher。
- **ContextMenuService**：右键菜单注册（桌面 + 文件/文件夹）。
- **CrashLogger**：三源异常落盘（AppDomain/TaskScheduler/Application → logs\crash-*.txt，滚动 10 份 + 脱敏）。
- **AutoBackupService**：滚动快照 + 恢复 + 周期定时器（字节复制 data.novadb）。
- **McpService**：命名管道 + 鉴权 + 分发 + 未解锁门控。
- **WindowsHelloService**：PasswordVault 存取（resource="Novara"/userName="winhello"）+ UserConsentVerifier 验证。
- **ToastService**：AppNotification Toast（unpackaged 前置 AUMID + Register）。
- **ReminderScheduler**：schtasks 注册一次性任务。
- **ChunkedRender**：分批渲染辅助（DispatcherQueue 低优先级分帧）。
- **HtmlSanitizer**：XSS 白名单净化（从 DiaryEditorPage 提取，供 HTML 导出 / MCP format=html 复用）。
- **RelayCommand**：ICommand 最小实现（托盘命令绑定）。
- **EditRequest / AddPathRequest / ReminderEditRequest / ReminderDueRequest / ShowWindowRequest**：IPC 请求（pending json + 命名事件）。

---

## 23. 构建与发布

发布链路：

```
dotnet publish -r win-x64 --self-contained
  → 手动补 Novara.pri（缺它启动即 0xc000027b 闪退）
  → Inno Setup 编译 Installer\setup.iss
```

关键坑（务必遵守）：

- **Novara.pri**：发布必须手动补，缺它启动即 `0xc000027b`。
- **StickNoteHost 独立发布**：Host 是 `EnableMsixTooling=true`，其 publish 会产出 `resources.pri`（MSIX 资源）。若把 Host 直接 publish 到主程序发布目录，`resources.pri` 会干扰 XAML 加载 → 启动 `0xc000027b`。正确做法：Host publish 到独立临时目录，只复制 `StickNoteHost.exe`。发布目录内绝不能出现 `resources.pri`。
- **NovaraMCP 独立发布**：纯 net8.0 控制台，`PublishSingleFile=true` + `SelfContained=true`，publish 产单个自包含 exe（无 `resources.pri`）。只复制 `NovaraMCP.exe` 到主程序发布目录。
- 卸载器：删除自启注册表项 + 删 `data.novadb` 前先去 Hidden|ReadOnly 属性。
- 数据保留语义：卸载选「保留数据」则 `%LocalAppData%\Novara` 原样保留，重装自动恢复。
- 清 bin/obj 后首次编译先 `dotnet restore`（否则 NETSDK1004）。

---

## 24. 测试

`Novara.Tests`（xUnit）引用 `Novara.Core`，覆盖：

- 存储：零迁移契约（Format 字段）、加密 / 解密往返、损坏检测、导入回滚。
- 安全：密码哈希、锁定计数、改密事务。
- API 探测：厂商识别、协议矩阵、错误分类。
- MCP 逻辑：token 鉴权、CRUD、敏感字段脱敏。
- CSV：三种方言解析、字段映射。

纯逻辑层（`Novara.Core`）无 WinUI 依赖，保证可独立单测。

---

## 25. 目录结构

```
Novara/
├── App.xaml / .cs             入口：MainWindow 单例、GetString/GetBrush/CreateGeometry、单实例、UiQueue
├── MainWindow.xaml / .cs      主窗口：标题栏 + 4 页导航 + 欢迎页；托盘/重启/关闭退出；IPC handler
├── IconData.cs                全部 SVG 图标 path + GroupIconKeysInOrder 选择器顺序
├── AppResources.cs            5 语言静态字典（i18n）
├── Themes/
│   ├── DesignSystem.xaml      颜色/圆角/间距/字体（主题字典）
│   └── Controls.xaml          通用控件样式（按钮 / GlassMenuFlyout 等）
├── Novara.Core/
│   ├── Models/                MemoGroup / MemoEntry / EntryField / FilePathEntry / TodoCard /
│   │                          NoteCard / DiaryEntry / AppSettings / NovaraDatabase
│   └── Services/              NovaraStore / CryptoService / ApiProbeService / ApiChatClient /
│                              ApiDiagnoseService / RelayProbeService / ProbeDataSetLoader /
│                              PasswordService / McpLogic / CsvImportExportService / Loc / CoreEnv
├── Novara.Tests/              xUnit 单元测试
├── Services/                  UI 服务：StartupService / StickySync / ContextMenuService / CrashLogger /
│                              AutoBackupService / McpService / WindowsHelloService / ToastService /
│                              ReminderScheduler / ChunkedRender / HtmlSanitizer / RelayCommand 等
├── Pages/
│   ├── BasicMemoPage         备忘页
│   ├── FilePathPage          路径备份页
│   ├── PlanPage              计划页（待办 + 便签）
│   ├── DiaryPage             记录页列表
│   ├── DiaryEditorPage       记录编辑器（WebView2 富文本 + Markdown）
│   ├── SettingsPage          设置页
│   ├── LockScreenPage        锁屏页
│   ├── SearchPage            全局搜索页
│   └── TrashPage             回收站页
├── StickNoteHost/             桌面便签独立进程
├── NovaraMCP/                 MCP stdio 前端（单文件发布）
├── DiaryEditorJs/             WebView2 编辑器 bundle（Tiptap + markdown-it）
├── Installer/                 Inno Setup 安装脚本
└── Assets/                    图标（ico / png / svg logo）
```

---

## 26. 版本历史

| 版本 | 日期 | 内容 |
|------|------|------|
| 2.0 | 2026-08-07 | 四大标签页 + 设置页 + 隐私锁；加密存储 / 托盘 / 主题 / 双语 |
| 3.0 | 2026-08-12 | 桌面便签、全局搜索（Ctrl+K）、AES-256-GCM 升级 + 长密码、五语言、卡片回收站、系统级提醒、全局右键菜单 |
| 4.0 | 2026-08-18 | 卡片拖拽排序、待办桌面双向同步、日记 MD 导出、API 检测升级、分批渲染 / 崩溃日志 / 数据备份 / 写盘去抖、单元测试工程化 |
| 5.0 | 2026-08-19 | 记录页重定位（Format 字段 + MD 编辑器）、MCP Agent 接口（14 工具）、Windows Hello 解锁（项目收官） |

---

> Novara 以「本地优先、简洁私密」为设计核心。所有数据属于用户，不离开本机。
