# 回归测试

[English](REGRESSION_TESTS.md) · 简体中文

近六个月修复的缺陷如何映射到自动化回归面。项目始于 2026 年 8 月，「近六个月」即项目全部历史。缺陷来源为 [CHANGELOG](../CHANGELOG.zh-CN.md) 的 Fixed 条目与验证轮次汇总；回归面有两层：

1. **单元/集成测试** —— `Novara.Tests`（xUnit，50+ 文件，1,076 个测试），直接引用发行程序集，覆盖数据、加密、同步与服务端层。
2. **静态守卫** —— `Tools/page_lifecycle_guard.py`（1,032 条判据）、`Tools/reverse_verify.py`（207 条经变异验证的条目）、`Tools/cooldown_guard.py`、`Tools/danger_red_guard.py`、`Tools/guards.py`（汇总套件）。WinUI 页面在结构上无法被测试工程引用，因此页面级修复以源码形态不变量钉在这些守卫里——每条都经变异验证（移除修复必须使守卫失败）。

## 映射（更新日志中的具名修复）

| 版本 | 修复 | 回归测试 |
|---|---|---|
| 12.0 | 跟随记忆值读取时归一化、以可选同步字段出站、远端 null 视为「无意见」 | `Novara.Tests/MemoDisplayModeTests.cs`（归一化/持久化契约/未触及模型）+ `SyncDiffTests.UngroupedCardExpanded_*`（CI） |
| 12.0 | 本周期自审抓到的交互回归（未分类卡自己展开、首展丢动效、渐隐只落文字层） | `page_lifecycle_guard` 源码形态不变量，各配变异用例 |
| 11.1 (R1) | HTML 编辑器图片选择器用错 COM 集合；多选被禁用 | `RegressionGuardsTests.FilePicker_MultiSelectUsesShellItemArray`（CI） |
| 11.1 (R1) | 多张图片互相替换；图片选中态难以清除 | `RegressionGuardsTests.DiaryEditor_ImageSelectionAndMultiInsert`（CI） |
| 11.1 (R1) | 工具栏命令夺走编辑器焦点 | `RegressionGuardsTests.DiaryEditor_ToolbarKeepsEditorFocus`（CI） |
| 11.0 | 全代码库加固（约 470 项修复） | 全守卫套件 + 本周期 CI 测试；编辑器热修复钉在 11.1 三行 |
| 10.3 | 导出文件丢失扩展名 | `RegressionGuardsTests.FilePicker_KeepsTheExtensionInvariant`（CI）+ 本地守卫 |
| 10.2 | 工具栏编辑不即时刷新预览 | `RegressionGuardsTests.EditorToolbar_ActionsSyncThePreview`（CI） |
| 10.2 | 分屏打字跳到底部 | 人工验证（WebView2 编辑器面） |
| 10.1 | 提权实例中的文件选择器 | `RegressionGuardsTests.FilePicker_UsesNativeWin32DialogsOnly`（CI） |
| 9.4 | 密码弹窗可双提交 | 守卫：`page_lifecycle_guard` 进行中锁定判据 |
| 9.4 | 提醒边框拖拽后丢失 | 守卫：`page_lifecycle_guard` 边框保留判据 |
| 9.4 | 服务端写栅栏防跨进程删除竞态 | `SpaceAdminTests`、`SqliteSpaceStoreTests`；`Tools/m3_e2e.py` HTTP 探针 |
| 9.4 | 更新下载校验清单 | `UpdateManifestVerifierTests`（5 项）+ `UpdatePolicyTests` |
| 9.4 | 首启向导期间的锁屏 | 守卫：`page_lifecycle_guard` 生命周期判据 |
| 9.3 | 编辑器失败诊断（缺 WebView2 运行时） | 人工验证 + 诊断日志审查 |
| 9.3 | WebView2 环境重复初始化错误 | `RegressionGuardsTests.EditorInit_IsReentrancyGuarded`（CI）+ 本地守卫 |
| 9.3 | 向导混语言文案 | 人工验证（语言矩阵） |
| 9.2 | 应用关闭时提醒不触发 | 人工验证（Task Scheduler COM 集成） |
| 9.2 | 类纸主题色解析拒绝 6 位色 | `RegressionGuardsTests.PaperTheme_ParsesSixDigitColors`（CI） |
| 9.2 | UI 各处硬编码危险红 | `RegressionGuardsTests.NoHardcodedDangerRed`（CI）+ 本地守卫 |
| 9.1 | TOTP 动态码冻结（托管包装类 GC） | `RegressionGuardsTests.TotpRegistry_UsesStrongReferences`（CI）+ 本地守卫 |
| 9.1 | 冷静期确认键首帧闪红 | `RegressionGuardsTests.CooldownHosts_KeepInlineButtonTemplates`（CI）+ 本地守卫 |
| 9.1 | 卡片拖拽卡顿 | `RegressionGuardsTests.DragAutoScroll_UsesTheCompositorFrameCallback`（CI） |
| 9.0 | 配置 host 白名单后的容器健康检查 | `Tools/m3_e2e.py` HTTP 级探针 |
| 6.2 | 增量验证轮次 | 由下方守卫套件覆盖 |
| 6.1 | 无筛选视图下新建空分组消失 | `RegressionGuardsTests.EmptyGroup_VisibilityIsWorkspaceConditional`（CI）+ 本地守卫 |
| 6.0 | 五轮核验的约 250 项问题 | 全部守卫套件：193 条反向验证 + 368 条生命周期判据 + 守卫家族——这些守卫正是那几轮核验的产物 |
| 5.2 | 同步锁死循环 | `SyncPlannerTests`（会话锁生命周期规则） |
| 5.1 | 一次全域核验的约 20 项修复 | 由下方守卫套件覆盖 |

## 数据/加密/同步/服务端层的修复按政策随附测试

贡献政策要求重要功能必须随附测试；可单测的层从未在没有测试的情况下发布过修复。代表性测试文件与钉住的内容：`StoreTests`（28，存储格式与原子写）、`SyncContractTests`（19，信封 MAC + keywrap 向量）、`SyncEngineTests`（16，对真实进程内服务器的引擎轮）、`SyncServerTests`（35，乐观并发、配额、保留）、`RelayProbeServiceTests`（37）、`ApiChatClientTests`（27）、`McpLogicTests`（18）、`BackupIntegrityTests`（18）、`HeaderV5Tests`（6，RFC 9106 Argon2id 向量）、`KdfHardeningTests`（6）、`UpdateManifestVerifierTests`（5），其余见 `Novara.Tests` 的套件索引。

## 合计

- 本表覆盖 11.1 编辑器热修复、11.0 加固与更早的具名修复。R1 的三项修复各自新增一条 `RegressionGuardsTests` 源码形态断言（CI）；11.0 加固由全守卫套件兜底。行为级单测、源码形态断言、本地守卫与人工验证的历史占比不再汇总为单一百分比——每次发版混合比例与套件规模都会变化，逐行标注更可靠。
- 本地守卫脚本（每次本机构建前运行、经变异验证）为 CI 断言及汇总轮（6.0 的约 250 项、5.1 的约 20 项）提供后备——它们是开发基础设施，有意不入本公开仓库。

---

*最后更新：2026-10-28*
