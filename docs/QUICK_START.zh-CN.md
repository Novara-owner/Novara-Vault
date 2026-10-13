# 快速入门

[English](QUICK_START.md) · 简体中文

从下载到日常使用共五步。Novara 本地优先：一切都在你机器上的单一数据文件里，除非你主动要求，什么都不外发。

## 1. 下载并安装

从 [Releases](https://github.com/Novara-owner/Novara-Vault/releases) 页（或 [README](../README.zh-CN.md) 中链接的官网）获取最新安装包。需要的话可以校验下载——[docs/VERIFYING.zh-CN.md](VERIFYING.zh-CN.md) 描述了 SHA-256、Sigstore 与 Authenticode 三种校验。运行安装器；除常规 UAC 提示外无需管理员权限。

## 2. 走一遍首启向导

首次启动有简短的欢迎向导（七页），介绍标签页、隐私锁与数据模型。之后可随时在 设置 → 欢迎页展示 重放。

## 3. 设置隐私锁（建议）

设置 → 隐私访问锁 可设置锁密码。开启后数据文件以 AES-256-GCM 加密，应用在闲置后自动锁定。设计上没有密码找回，请选择记得住的密码。之后可开启 Windows Hello 作为便捷解锁门。

## 4. 认识四个标签页

- **备忘** —— 分组管理账号、密钥与笔记；最敏感的分区。
- **路径备份** —— 常用文件夹与文档的快速入口。
- **计划** —— 带子任务的待办卡与便签；便签可钉在桌面。
- **记录** —— 日记与文档，富文本编辑器、全文搜索与回收站。

Ctrl+K 随处唤起全局搜索；设置页页脚显示当前版本。

## 5. 知道数据在哪

一切都在 `%LocalAppData%\Novara`（`data.novadb` 与锁文件）。工具 → 数据备份 可导出加密的 `.novaenc` 备份——请留一份在机器之外。工具 → 安全快照 生成可分享的加密快照；同步（自部署）与 MCP Agent 接口是可选功能，见 [docs/MCP.zh-CN.md](MCP.zh-CN.md) 与 [docs/BUILD.zh-CN.md](BUILD.zh-CN.md)。

开发与自部署见 [docs/BUILD.zh-CN.md](BUILD.zh-CN.md)。
