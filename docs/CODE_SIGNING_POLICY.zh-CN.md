# 代码签名政策

<p align="center"><a href="CODE_SIGNING_POLICY.md">English</a> · <strong>简体中文</strong></p>

> 另见：[核验你的下载](VERIFYING.zh-CN.md) —— 你可以自己动手做的校验。· [构建溯源](PROVENANCE.zh-CN.md) —— 一个版本是怎么造出来的。· [隐私政策](../PRIVACY.zh-CN.md)

**最后更新：** 2026-10-08

Free code signing provided by SignPath.io, certificate by SignPath Foundation

（中文大意：免费代码签名由 SignPath.io 提供，证书由 SignPath Foundation 签发。）

签名服务由 [SignPath.io](https://signpath.io/) 提供；证书签发给 [SignPath Foundation](https://signpath.org/)，由其背书「被签名的二进制确系本仓库源码的自动化构建产物」。

## 签的是什么

`Novara_Setup_x.y.z.exe` —— 随 Release 页附上的 Windows 安装包。

它由本仓库自己的 CI 工作流（[`.github/workflows/ci.yml`](../.github/workflows/ci.yml)）从**打了 tag 的源码**构建，且**每一次发布都要人工批准**才提交签名。

安装包里的程序来自同一次构建。上游开源项目的二进制（.NET 运行时、Windows App SDK、Win2D）以**未签名**形式随包分发 —— 给它们签名是各自维护方的责任，不是我们的。

## 角色

Novara 由单一账号维护，该账号同时承担全部角色。角色定义与 SignPath Foundation 一致。

| 角色 | 成员 |
|---|---|
| Authors（作者）—— 可不经额外评审直接改动源码 | [@Novara-owner](https://github.com/Novara-owner) |
| Reviewers（评审）—— 评审非提交者提出的改动 | [@Novara-owner](https://github.com/Novara-owner) |
| Approvers（批准）—— 对每一次发布决定是否可签名 | [@Novara-owner](https://github.com/Novara-owner) |

## 隐私

本项目的隐私政策是 [PRIVACY.zh-CN.md](../PRIVACY.zh-CN.md)（[English](../PRIVACY.md)）。一句话：Novara 是本地优先的，**除非你要求，它不会向我们或任何第三方传输信息**。

## 状态

SignPath Foundation 证书覆盖**本政策生效之后**发布的第一个安装包。在此之前发布的安装包**未经 Authenticode 签名，也未追溯补签** —— 对这些版本，请按 [VERIFYING.zh-CN.md](VERIFYING.zh-CN.md) 校验 SHA-256 与 Sigstore 签名。Sigstore 签名自 10.0 起随每个版本提供。

## 相关文档

- [核验你的下载](VERIFYING.zh-CN.md) —— 校验命令本身
- [构建溯源](PROVENANCE.zh-CN.md) —— 流水线、每个版本随附什么、以及我们不声称什么
- [PRIVACY.zh-CN.md](../PRIVACY.zh-CN.md) —— 程序怎么处理你的数据
- [LICENSE.md](../LICENSE.md) —— MIT
