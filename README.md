# Novara
Novara is a local-first, privacy-focused memo application built with C# and WinUI 3 for Windows. It consolidates memos, file paths, to-do lists, sticky notes, and a rich-text diary into a single encrypted database — all stored entirely on your local machine with no cloud uploads, no telemetry, and no account registration. Your data lives in %LocalAppData%\Novara\ as a portable single-file database, optionally protected by AES-256 encryption with a 6-digit PIN. Whether you are managing daily notes, tracking project tasks, saving frequently used file paths, writing a private diary, or storing API keys with built-in connectivity testing, Novara brings everything together in one native Windows app that feels fast, responsive, and reliably offline. With deep system integration, dark/light theme support, bilingual interface (English/Chinese), and a strong commitment to long-term stability, Novara is designed to be the last note-taking tool you will ever need.

<p align="center">
  <img src="images/English%20+%20Light%20Welcome%20Page.png" alt="English + Light Welcome Page" width="500" />
</p>

## Privacy Lock
Optional 6-digit PIN protection powered by AES-256 encryption. Once enabled, your entire database is encrypted at rest — every memo, path, todo, and diary entry becomes unreadable without the correct PIN. The PIN itself is never stored in plaintext; only a salted SHA256 hash is kept locally. There is no backdoor, no password recovery mechanism, and no cloud dependency. If you forget your PIN, the only way forward is to wipe the database and start over.

Security is further reinforced by a 30-minute lockout after 5 consecutive failed attempts — the counter and lock state persist across app restarts, preventing brute-force bypass attempts. Changing your PIN requires verification of the current PIN, ensuring that no one can alter your security settings without your knowledge. The lock screen follows your system theme and covers the entire application window, leaving no visual clues about its state. As an added layer of protection, the input field clears automatically when the window loses focus, preventing shoulder-surfing attacks in shared environments.

<p align="center">
  <img src="images/Privacy%20Lock.png" alt="Privacy Lock Settings" width="500" />
</p>

## Memo Manager
The home page where all your memos live — the central hub of Novara. Organize entries into custom groups, star or pin important ones for instant access, and search across everything in real time. Each entry supports a title, content, and optional remarks, making it flexible enough for passwords, account credentials, code snippets, API keys, or any other text-based information you need to keep close.

Entries are organized into custom groups — move them between groups via the context menu to structure your data the way you think. The star and pin priority system puts your most important items right at the top, while real-time full-text search ensures you never waste time hunting for that one piece of information. Four built-in entry types — Email, Account, API Key, and Custom — cover the majority of use cases, with the Custom type offering unlimited flexibility for anything else.

<p align="center">
  <img src="images/Backup%20Page.png" alt="Backup Page" width="500" />
</p>

### API Key Management & Connectivity Testing
As AI agents and LLM-powered applications become essential tools in daily workflows, managing API keys securely has never been more important. Novara provides a dedicated API Key entry type that stores your keys in an encrypted database — keeping them safe from plaintext leaks, screenshot accidents, or prying eyes.

But storage is only half the story. An invalid or expired API key can break your workflow at the worst possible moment. That is why every API Key entry comes with a built-in connectivity test: right-click any API Key entry, select "Test Connectivity," and Novara automatically probes the endpoint with a GET /v1/models request — idempotent and incurring no cost. It intelligently attempts Bearer token authentication first; if the server responds with 401 or 403, it retries without Bearer, ensuring seamless compatibility with both OpenAI-standard and domestic Chinese providers.

Test results are displayed with clear visual states: Testing (theme color), Success (green), Auth Failed (red), No Models Endpoint (yellow), or Network Error (red). Advanced configuration options are only revealed after a failed test, allowing you to switch between OpenAI Standard (Bearer), Domestic Compatible (No Bearer), or RAW mode (testing disabled). Successful tests automatically save the working protocol to your entry, while failed tests never alter your saved configuration — your data stays exactly as you left it. Every API Key card also displays its endpoint URL on the second line, so you can identify each key at a glance.

<p align="center">
  <img src="images/API.png" alt="API Key Management" width="500" />
</p>

## File Path Manager
A dedicated page for saving and organizing file and folder paths that you frequently access. Instead of digging through File Explorer every time, store your important locations here — project directories, configuration files, log folders, or any other path you need to reach quickly.

Each path card displays the name, full path, and optional remarks in a clean three-line layout. One click copies the path to your clipboard; another opens the location in Explorer with the file or folder automatically highlighted. No more manual navigation — just instant access.

### Real-Time Path Validity Detection
Paths change. Files get moved, renamed, or deleted. A saved path is only useful if it still points to something that actually exists. Novara continuously monitors every saved path and gives you instant, at-a-glance visual feedback: a green border means the path exists and is accessible; a red border means the file or folder is missing, moved, or inaccessible.

Validity checks run automatically in five scenarios — when you create or edit a path entry, on a 30-minute background timer, during app startup, when you trigger a full scan from the empty area, or via right-click on an individual card. This means you always know, at a glance, whether your saved locations are still valid — so you never waste time clicking into a dead path.

<p align="center">
  <img src="images/File%20Backup.png" alt="File Backup" width="500" />
</p>

## Task Dashboard
A lightweight productivity board that combines to-do lists and sticky notes in one place. Use it for project planning, daily task tracking, or simply jotting down quick thoughts that don't deserve a full memo entry. It's designed to be simple, fast, and friction-free — your second brain for getting things done.

### Todo Cards
Each todo card consists of a main task and optional subtasks. Subtasks are displayed inline beneath the main item, and the card can be collapsed or expanded with a single click.
Subtasks all checked → main task automatically checked → card collapses to save space
Uncheck any subtask → main task unchecks automatically → card expands to show everything
Manually uncheck the main task → subtasks remain unchanged, card expands
No subtasks → expand/collapse button is hidden; checking the main task marks the card complete and collapses it
All checked states are persisted to disk and survive app restarts — your progress is always preserved.

### Sticky Notes
For quick, unstructured notes that do not fit into a todo format. Each note can hold longer text content.
Content exceeds 240 characters or contains line breaks → an expand button appears
Expanded view removes MaxLines restriction to show the full content
Expand/collapse state is persisted across app restarts

<p align="center">
  <img src="images/Plan%20Page.png" alt="Plan Page" width="500" />
</p>

## Rich Text Diary
A full-featured rich-text diary editor built on WebView2 that turns journaling into a polished writing experience. Whether you are keeping a personal journal, drafting meeting notes, or writing detailed project documentation, the editor provides the tools you need to express yourself clearly.

<p align="center">
  <img src="images/Diary%20Page.png" alt="Diary Page" width="500" />
</p>

### Rich-Text Editing Tools
The toolbar floats as a compact capsule above your content, staying accessible without scrolling back to the top. All formatting options are applied instantly with visual feedback:
Bold, Italic, Underline — toggle styles with one click; selected state syncs with cursor position
Font Color — choose any color to highlight or differentiate text
Image Insertion — paste or upload images directly into the editor; base64-encoded and stored within the database
HTML Import — paste formatted content from other sources; automatically sanitized to remove scripts and dangerous attributes

### Auto-Save & Data Safety
The editor automatically preserves your content when you navigate away or close the app. Title and body are saved independently, and the system prevents blank overwrites — if the editor is not ready or content fails to load, saving is blocked to protect existing data. All HTML content is double-sanitized on save and load with a strict whitelist (b, strong, i, em, u, span, font, div, br, p, img, ul, ol, li), stripping any script tags, event handlers, and dangerous protocols to keep your diary safe from XSS.

<p align="center">
  <img src="images/Diary%20Editor%20Page.png" alt="Diary Editor Page" width="500" />
</p>

## Dual-Language UI
Novara ships with full English and Chinese interface support, with more languages planned for future releases. Switching languages is seamless — select your preferred language from the Settings page, confirm the change, and the app restarts with the new language applied across every page, dialog, and menu. No partial translations, no mixed languages — just a complete, consistent interface in your chosen language.

The underlying architecture is built for reliability. A static resource dictionary powers the entire UI — no runtime XAML merging means zero crash risk, no COM exceptions, and instant lookups. The fallback chain is equally robust: stored preference takes priority, followed by system language, with Chinese as the final default. All user data remains entirely language-independent — only UI text is translated, so your memos, todos, and diary entries stay exactly as you wrote them, regardless of which language you are viewing the app in.

<p align="center">
  <img src="images/Chinese%20+%20Light%20Welcome%20Page.png" alt="Chinese + Light Welcome Page" width="500" />
</p>

## Theme System
Novara offers three theme options — Light, Dark, and Follow System — with seamless switching from the Settings page. Every color, border, and surface has been carefully tuned across multiple rounds of refinement to ensure a polished, premium look in both modes. Dark mode is rich and comfortable for late-night use, with deep surfaces and gentle contrast that reduce eye strain; Light mode is crisp, clean, and airy for daytime productivity.

The theme applies globally across all pages, dialogs, and menus — no flickering, no mismatched elements, no visual surprises. Accent colors, hover states, and surface elevations remain consistent throughout the interface, creating a cohesive experience that feels thoughtfully designed rather than cobbled together. Your theme preference is persisted across app restarts, so Novara always looks the way you left it.

<p align="center">
  <img src="images/English%20+%20Dark%20Welcome%20Page.png" alt="English + Dark Welcome Page" width="500" />
</p>

## Data Import & Export
Your data should move with you — freely, securely, and without friction. Novara provides full database backup and restore capabilities, ensuring your memos, todos, diary entries, and settings are never locked into a single machine.

Exports are always generated in plaintext with a complete MD5 integrity header, making them human-readable, easily inspectable, and compatible across versions. Whether you are migrating to a new device, reinstalling Windows, or simply keeping an offline archive, the export process preserves every piece of your data with zero loss. No proprietary formats, no vendor lock-in — just your data, plain and clear.

Imports are handled with equal care. The system performs thorough validation before merging — duplicate IDs are automatically regenerated, null partitions are normalized, and orphaned references are cleaned up. If the import process fails at any point, the database is safely rolled back to its previous state, ensuring your data is never left in a half-imported, corrupted condition. For encrypted databases, both import and export require password verification, ensuring your data stays protected even during transfer. The result is a backup system that is both powerful and transparent — your data, always within your control, always ready to move with you.

## Startup & Tray Options
Novara adapts to the way you work. Enable automatic startup with Windows so your notes are always ready when you log in. For users who prefer quick access without cluttering the taskbar, the system tray mode keeps Novara running silently in the background — click the tray icon to show or hide the main window, or right-click for quick actions. Both settings are fully optional and can be toggled at any time from the Settings page. Your preferences persist across app restarts, so Novara always behaves the way you expect.

## Tech Stack
- Language: C# / XAML
- UI Framework: WinUI 3 / Windows App SDK 1.6
- Runtime: .NET 8 LTS
- Target OS: Windows 10 (Build 19041, 2004) & Windows 11
- Deployment: Self-contained publish + Inno Setup installer
- Storage: Single-file JSON database with optional AES-256 encryption (GZip compressed)
- Rich Text Engine: WebView2 (Edge Chromium)

## Installation
Download the latest installer from the Releases page, then follow these steps:
- Run Novara-Setup.exe
- Follow the setup wizard to complete installation
- Launch Novara from the desktop shortcut or Start Menu

### System Requirements
- Windows 10 Version 2004 (Build 19041) or newer / Windows 11
- WebView2 Runtime (will auto-install if missing)
- .NET 8 Runtime (bundled inside self-contained package)

## Local Data Directory
All user data is stored in this local path:%LocalAppData%\Novara\
File breakdown:
- `data.novadb`: Main JSON database (plaintext or AES encrypted)
- `security.dat`: Password salt & SHA256 hash storage
- `lockout.dat`: Password failure counter & lock timer
- `language.dat`: Saved UI language preference

You can migrate all your notes by copying this entire folder to another PC.

## Security Specifications
- Local-only AES-256 encryption, PBKDF2 key derivation with 100,000 iterations
- Passwords are stored as salted SHA256 hashes; plaintext passwords are never saved on disk
- Database protected by exclusive file lock + Hidden / Read-only file attributes
- Diary HTML content double-sanitized on save & load, strips `<script>`, inline event attributes and malicious protocols like `javascript:`
- No outbound network requests except manual API connectivity tests initiated by users

## Core Architecture Highlights
- Single-file database with 7 isolated logical partitions: Diaries, File Paths, To-Dos, Stickies, Memo Groups, Memo Entries, App Settings
- Memory-first design: Full data load on startup; all edits are written to disk via thread-safe async operations
- Atomic write mechanism using temporary `.tmp` files to prevent database corruption on crash
- Unified theme rendering via `App.GetBrush()` to maintain consistent colors across all UI elements
- Double HTML sanitization for diaries to block XSS injection vectors

### Contact Us
For business inquiries, technical support, bug reports, feature suggestions, or any other feedback, please reach out to us
- owner@novara.xin
- novara.xin@outlook.com
- Novara_xin@163.com

We read every message and appreciate your input.

# Novara
Novara 是一款基于 C# / WinUI 3 构建的本地优先、隐私至上的备忘录应用，专为 Windows 平台打造。它将备忘、文件路径、待办、便签和富文本日记整合在单一加密数据库中 — 所有数据全部存储于本地设备，无云端上传、无数据追踪、无需注册账号。数据位于 %LocalAppData%\Novara\ 目录下，以可移植的单文件数据库形式存在，并可选择以 6 位数字 PIN 码配合 AES-256 加密进行保护。无论你是在管理日常备忘、跟踪项目任务、保存常用文件路径、撰写私人日记，还是存储 API 密钥并随时检测其连通性，Novara 都将这一切整合在原生 Windows 应用中，带来流畅、即时响应且完全离线的可靠体验。深度系统集成、深色/浅色主题支持、中英文双语界面，以及贯穿长期维护的稳定性承诺，Novara 的设计目标，就是成为你最后需要的那款备忘录工具。

<p align="center">
  <img src="images/English%20+%20Light%20Welcome%20Page.png" alt="English + Light Welcome Page" width="500" />
</p>

## 隐私锁
可选 6 位数字 PIN 码保护，采用 AES-256 加密技术。启用后，整个数据库处于静止状态时完全加密 — 每一条备忘、路径、待办和日记条目，在未输入正确 PIN 码之前均无法读取。PIN 码本身永不存储明文，本地仅保留加盐 SHA256 哈希值。无后门，无密码恢复机制，不依赖任何云服务。若遗忘 PIN 码，唯一的选择是清空整个数据库重新开始。

安全性进一步强化：连续 5 次输入错误后触发 30 分钟锁定 — 失败计数与锁定状态跨应用重启持久化，有效防止暴力破解企图。修改 PIN 码需验证当前密码，确保无人能在未经授权的情况下更改安全设置。锁屏界面跟随系统主题，全屏遮挡整个应用窗口，不暴露任何状态线索。作为额外防护层，窗口失焦时输入框自动清空，有效防止旁窥攻击。

<p align="center">
  <img src="images/Privacy%20Lock.png" alt="Privacy Lock Settings" width="500" />
</p>

## 备忘管理
这是所有备忘的首页 — Novara 的核心枢纽。将条目整理到自定义分组中，通过星标或置顶标记重要内容以便快速访问，并实时搜索所有条目。每条备忘均支持标题、内容和可选备注，足够灵活地容纳密码、账户凭证、代码片段、API 密钥或任何其他需要随时调用的文本信息。

条目通过自定义分组进行组织，可通过右键菜单在分组间移动，让你按照自己的思维方式构建数据结构。星标与置顶优先级系统将最重要的内容置顶显示，实时全文搜索确保你永远不会在查找某条信息上浪费时。四种内置条目类型 — 邮箱、账户、API 密钥和自定义 — 覆盖绝大多数使用场景，其中自定义类型为其他任何需求提供了无限灵活性。

<p align="center">
  <img src="images/Backup%20Page.png" alt="Backup Page" width="500" />
</p>

## API 密钥管理
随着 AI 智能体和大语言模型应用成为日常工作流程中不可或缺的工具，安全地管理 API 密钥变得前所未有的重要。Novara 提供了专用的 API 密钥条目类型，将你的密钥存储在加密数据库中 — 避免明文泄露、截图意外暴露或他人窥视的风险。

但存储只是故事的一半。无效或过期的 API 密钥可能在最糟糕的时刻中断你的工作流。这就是为什么每条 API 密钥条目都内置了连通性检测功能：右键点击任意 API 密钥条目，选择「检测连通性」，Novara 便会自动向目标端点发送 GET /v1/models 请求 — 该接口幂等且不产生费用。它会智能地先尝试 Bearer Token 鉴权，若服务器返回 401 或 403，则自动切换为无鉴权模式重试，确保与 OpenAI 标准接口及国内厂商无缝兼容。

检测结果通过清晰的视觉状态呈现：检测中（主题色）、成功（绿色）、鉴权失败（红色）、无 models 接口（黄色）或网络异常（红色）。高级配置选项仅在检测失败后显示，允许你在 OpenAI 标准（Bearer）、国内兼容（无鉴权）和 RAW 模式（禁用检测）之间切换。检测成功后协议自动保存至条目中，检测失败则绝不篡改已有配置 — 你的数据始终保持原样。每条 API 密钥卡片还在第二行显示其端点 URL，方便你一眼识别每条密钥对应的服务。

<p align="center">
  <img src="images/API.png" alt="API Key Management" width="500" />
</p>

## 文件路径管理
一个专用于保存和组织常用文件及文件夹路径的页面。无需每次都翻找文件资源管理器，将你的重要位置保存在这里 — 项目目录、配置文件、日志文件夹，或任何其他需要快速访问的路径。每条路径卡片以清晰的三行布局展示名称、完整路径和可选备注。一键复制路径到剪贴板，另一键在资源管理器中打开并自动高亮目标文件或文件夹。无需手动导航，即刻直达。

### 路径实时有效性检测
路径会变化。文件会被移动、重命名或删除。一条被保存的路径只有在它仍然指向一个真实存在的位置时才有价值。Novara 持续监控每一条已保存的路径，并通过即时的视觉反馈让你一目了然：绿色边框表示路径存在且可访问，红色边框表示文件或文件夹已缺失、被移动或无法访问。

<p align="center">
  <img src="images/File%20Backup.png" alt="File Backup" width="500" />
</p>

## 计划看板
一个轻量级的生产力面板，将待办清单和便签整合于一处。用于项目规划、日常任务跟踪，或随手记下那些不值得单独创建备忘的灵光一现。设计目标就是简单、快速、无摩擦 — 你高效执行事务的第二大脑。

### 待办
每张待办卡片由主待办和可选子任务组成，子任务内联显示在主项下方。点击一下即可折叠或展开卡片，让看板保持整洁与聚焦。
智能完成逻辑为你承担了繁重的工作：
- 子任务全部勾选 → 主待办自动勾选 → 卡片折叠以节省空间
- 取消任意子任务 → 主待办自动取消勾选 → 卡片展开显示全部内容
- 手动取消主待办勾选 → 子任务保持不变，卡片展开（不做强制变更）
- 无子任务 → 展开/折叠按钮隐藏；勾选主待办即标记卡片完成并折叠
所有勾选状态均持久化到磁盘，跨应用重启不丢失 — 你的进度始终被完整保留。

### 便签
用于快速记录那些不适合待办格式的非结构化想法。每条便签可容纳更长的文本内容，不受限制。
- 内容超过 240 字符或包含换行 → 自动显示展开按钮
- 展开后移除最大行数限制，完整呈现全部内容
- 展开/折叠状态跨应用重启持久化

<p align="center">
  <img src="images/Plan%20Page.png" alt="Plan Page" width="500" />
</p>

## 富文本日记
基于 WebView2 构建的全功能富文本日记编辑器，将日记写作转变为一种精致、无干扰的书写体验。无论你是记录个人日记、起草会议纪要，还是撰写详细的项目文档，编辑器都提供了清晰表达所需的一切工具 — 全部集成在干净的原生 Windows 界面中。

<p align="center">
  <img src="images/Diary%20Page.png" alt="Diary Page" width="500" />
</p>

### 富文本编辑工具
工具栏以紧凑的胶囊形态悬浮在内容上方，无需滚动回顶部即可随时取用 — 格式化工具始终精准地出现在你需要的位置。所有格式选项即时生效，并伴有清晰的视觉反馈：
- 加粗、倾斜、下划线 — 一键切换样式；选中状态与光标位置实时同步
- 字体颜色 — 自由选取颜色以高亮、强调或区分文本
- 图片插入 — 直接将图片粘贴或上传至编辑器；以 Base64 编码存储在数据库中，图片随日记一同保存
- HTML 导入 — 从其他来源粘贴格式化内容；自动净化，移除脚本与危险属性

### 自动保存与数据安全
编辑器会在你导航离开或关闭应用时自动保存内容 — 无需手动保存，不会丢失工作。标题与正文独立保存，系统主动防止空白覆盖：若编辑器未就绪或内容加载失败，保存操作将被阻止，以保护现有数据。

安全性贯穿每一层。所有 HTML 内容在保存和加载时均经过双重净化，配合严格的白名单（b, strong, i, em, u, span, font, div, br, p, img, ul, ol, li），剥离所有脚本标签、事件处理器及危险协议。无论内容源自何处，你的日记始终免受 XSS 攻击威胁。

<p align="center">
  <img src="images/Diary%20Editor%20Page.png" alt="Diary Editor Page" width="500" />
</p>

## 双语界面
Novara 原生支持完整的中英文双语界面，后续版本将扩展更多语言支持。语言切换流畅无感 — 从设置页选择偏好的语言，确认更改后应用自动重启，新语言将应用至每一页、每一对话框及每一菜单。无局部翻译，无语言混杂 — 完整的、一致的界面体验，以你选择的语言呈现。

底层架构为可靠性而生。静态资源字典驱动整个 UI — 无运行时 XAML 合并意味着零崩溃风险、无 COM 异常、查找即时完成。回退链条同样健壮：存储偏好优先，其次为系统语言，中文作为最终兜底。所有用户数据保持语言无关 — 仅 UI 文本被翻译，无论你以何种语言查看应用，备忘、待办、日记条目都保持原样。

<p align="center">
  <img src="images/Chinese%20+%20Light%20Welcome%20Page.png" alt="Chinese + Light Welcome Page" width="500" />
</p>

## 主题系统
Novara 提供三种主题选项 — 浅色、深色、跟随系统 — 从设置页即可无缝切换。每种颜色、边框和表面都经过多轮精细调校，确保两种模式下都呈现精致、高级的视觉质感。深色模式深邃舒适，适合深夜使用，深色表面搭配柔和对比，有效减轻眼部疲劳；浅色模式明快清爽，适合日间高效工作。

主题全局应用于所有页面、对话框和菜单 — 无闪烁、无错位元素、无视觉意外。强调色、悬停状态和表面层次在整个界面中保持一致，营造出经过深思熟虑而非拼凑而成的统一体验。主题偏好跨应用重启持久化保存，Novara 始终以你离开时的模样呈现。

<p align="center">
  <img src="images/English%20+%20Dark%20Welcome%20Page.png" alt="English + Dark Welcome Page" width="500" />
</p>

## 数据导入与导出
你的数据应当随你而动 — 自由、安全、无阻碍。Novara 提供完整的数据库备份与恢复功能，确保你的备忘、待办、日记条目和设置永不锁定于单一设备。导出始终以明文格式生成，并附带完整的 MD5 完整性校验头，使其人类可读、易于检查、跨版本兼容。无论你是迁移至新设备、重装 Windows，还是仅保留一份离线归档，导出过程都会完整保留每一条数据，零损失。无专有格式，无供应商锁定 — 你的数据，纯粹而清晰。

导入同样谨慎处理。系统在合并前执行全面验证 — 重复 ID 自动重新生成、空分区归一化、悬空引用被清理。若导入过程在任何环节失败，数据库将安全回滚至先前状态，确保你的数据从不会处于半导入的损坏状态。对于加密数据库，导入和导出均需密码验证，确保数据在传输过程中同样受到保护。最终呈现的是一个既强大又透明的备份系统 — 你的数据，始终由你掌控，始终准备随你迁移。

## 开机自启与托盘驻留
Novara 适应你的工作方式。启用随 Windows 自动启动，登录系统时备忘即刻就绪。对于偏好快速访问但不愿让任务栏拥挤的用户，系统托盘模式让 Novara 在后台静默运行 — 点击托盘图标显示或隐藏主窗口，右键唤起快捷操作菜单。两项设置均为完全可选，随时可从设置页切换。偏好跨应用重启持久化保存，Novara 始终保持你期望的行为方式。

## 技术栈
语言：C# / XAML
UI 框架：WinUI 3 / Windows App SDK 1.6
运行时：.NET 8 LTS
目标系统：Windows 10（Build 19041，2004）及 Windows 11
部署方式：自包含发布 + Inno Setup 安装向导
数据存储：单文件 JSON 数据库，可选 AES-256 加密（GZip 压缩）
富文本引擎：WebView2（Edge Chromium）

## 安装指南
从 Releases 页面下载最新安装包，然后按以下步骤操作：
运行 Novara-Setup.exe
跟随安装向导完成安装
从桌面快捷方式或开始菜单启动 Novara

## 系统要求
Windows 10 版本 2004（Build 19041）或更新版本，或 Windows 11
WebView2 运行时（缺失时自动安装）
.NET 8 运行时（自包含部署包中已内置）

## 本地数据目录
所有用户数据存储在以下本地路径：
%LocalAppData%\Novara\

### 文件说明：
data.novadb — 主 JSON 数据库（明文或 AES 加密）
security.dat — 密码盐值与 SHA256 哈希存储
lockout.dat — 密码失败计数器与锁定计时器
language.dat — 已保存的 UI 语言偏好
若要将所有笔记迁移至另一台电脑，只需复制此完整文件夹即可。

## 安全规格
全本地 AES-256 加密，PBKDF2 密钥派生（100,000 次迭代）
密码以加盐 SHA256 哈希存储 — 明文密码永不写入磁盘
数据库受独占文件锁及 Hidden/Read-Only 文件属性双重保护
日记 HTML 内容在保存和加载时双重净化 — 剥离 <script> 标签、内联事件属性及 javascript: 等恶意协议
除用户主动发起的 API 连通性检测外，无任何出站网络请求

## 核心架构亮点
单文件数据库，包含 7 个隔离的逻辑分区：日记、文件路径、待办、便签、备忘分组、备忘条目、应用设置
内存优先设计 — 启动时全量加载数据；所有编辑通过线程安全的异步操作持久化到磁盘
原子写入机制，使用临时 .tmp 文件防止崩溃时数据库损坏
通过 App.GetBrush() 统一主题渲染，确保所有 UI 元素颜色一致
日记双重 HTML 净化，阻断 XSS 注入攻击向量

## 联系我们
如有商务合作、技术支持、Bug 反馈、功能建议或其他任何意见，请通过以下邮箱与我们联系：

- owner@novara.xin
- novara.xin@outlook.com
- Novara_xin@163.com

我们认真阅读每一条反馈，感谢你的宝贵意见。
