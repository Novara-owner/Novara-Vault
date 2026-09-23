# 校验你的下载

本页的每一句话都不需要你「相信我们」——每个发布版本都附带让你自行核验的材料。

## Windows 安装包（Novara_Setup_x.y.z.exe）

1. 从 Release 页面下载安装包**和** `SHA256SUMS` 附件。
2. 比对哈希。在 PowerShell 中：

   ```powershell
   Get-FileHash .\Novara_Setup_8.0.0.exe -Algorithm SHA256
   ```

   结果必须与 `SHA256SUMS` 中的那一行一致。如果你有 `sha256sum`（Git Bash、Linux），把安装包放到 `SHA256SUMS` 旁边，一条命令完成校验：

   ```bash
   sha256sum -c SHA256SUMS
   ```

## Docker 镜像（自托管服务端，9.0 起）

每个服务端版本都会在 `IMAGES.txt` 附件中钉住镜像 digest。**按 digest 拉取**可以把字节钉死，不受标签变化影响：

```bash
docker pull ghcr.io/novara-owner/novara-sync@sha256:<IMAGES.txt 中的 digest>
```

校验已经拉到本地的镜像：

```bash
docker inspect --format='{{index .RepoDigests 0}}' ghcr.io/novara-owner/novara-sync:9.0.0
```

digest 必须与 `IMAGES.txt` 一致。同一 digest 也会发布到 Docker Hub，两个来源解析到的是同一份字节。

## SBOM（9.0 起）

发布附件中包含软件物料清单（SPDX JSON，用 Syft 生成），列出产品中的每一个组件。用任意 SPDX 查看器打开，或直接列出包名：

```bash
jq -r '.packages[].name' novara-9.0.0.spdx.json | sort -u
```

## OpenSSF Scorecard

README 顶部的徽章链接到本仓库的 [OpenSSF Scorecard](https://scorecard.dev) 评分——这是一套对仓库工程实践（分支保护、依赖锁定等）的自动化安全审计，由 OpenSSF 的基础设施计算，与我们无关。

## 代码签名

Windows 安装包**暂未做代码签名**：首次运行时 SmartScreen 可能弹提示。请先完成上文的 SHA-256 校验，再选择「更多信息 → 仍要运行」。免费的开源签名路线（SignPath Foundation / Certum）与镜像的无密钥签名（Sigstore/cosign）都在后续计划中。
