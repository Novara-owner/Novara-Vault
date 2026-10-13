# 保证案例

[English](ASSURANCE.md) · 简体中文

本页汇编项目的安全保证案例：软件主张什么、针对哪些威胁、适用了哪些设计原则、证据在哪里。本页只做摘要与链接——规范性来源是 [threat-model.zh-CN.md](threat-model.zh-CN.md)、[security-architecture.zh-CN.md](security-architecture.zh-CN.md)、[SECURITY.zh-CN.md](../SECURITY.zh-CN.md)、[docs/PROVENANCE.zh-CN.md](PROVENANCE.zh-CN.md) 与 [docs/CODE_SIGNING_POLICY.zh-CN.md](CODE_SIGNING_POLICY.zh-CN.md)。

## 1. 系统概述与安全主张

Novara 是面向 Windows 的本地优先个人知识工具：备忘、路径收藏、计划与日记记录存于单一本地文件，附带可选的端到端加密同步（服务器自部署）、只读 Web 伴侣，以及带权限闸门的 Agent（MCP）接口。没有账号、没有遥测、没有任何能读你数据的官方服务器。

整个案例建立在以下主张上：

| # | 主张 | 证据 |
|---|---|---|
| C1 | 静态数据由 AES-256-GCM 与版本化认证头保护；锁密码不落盘 | [security-architecture.zh-CN.md](security-architecture.zh-CN.md) §1；[threat-model.zh-CN.md](threat-model.zh-CN.md) §6 缓解表 |
| C2 | 导出与同步产物只以用户选择的形式离开机器：强制密码的加密容器，或服务器无法解读的密文 | [security-architecture.zh-CN.md](security-architecture.zh-CN.md) §3–4；[threat-model.zh-CN.md](threat-model.zh-CN.md) §2、§6 |
| C3 | Agent 接口默认关闭、按客户端设闸、全程可审计 | [security-architecture.zh-CN.md](security-architecture.zh-CN.md) §2；[docs/MCP.zh-CN.md](MCP.zh-CN.md) |
| C4 | 发布产物可复核校验：校验和、钉死的镜像摘要、SBOM、Sigstore 无钥签名、签名更新清单 | [docs/VERIFYING.zh-CN.md](VERIFYING.zh-CN.md)；[docs/PROVENANCE.zh-CN.md](PROVENANCE.zh-CN.md)；[docs/CODE_SIGNING_POLICY.zh-CN.md](CODE_SIGNING_POLICY.zh-CN.md) |
| C5 | 桌面应用不开任何监听端口，只发起用户触发的出站连接 | [threat-model.zh-CN.md](threat-model.zh-CN.md) §6 末行；应用内网络活动面板 |
| C6 | 服务器只持密文，以哈希令牌在限速下认证设备，永远不掌握空间密钥 | [security-architecture.zh-CN.md](security-architecture.zh-CN.md) §4；[threat-model.zh-CN.md](threat-model.zh-CN.md) §3（A4） |

## 2. 威胁模型摘要

完整模型见 [threat-model.zh-CN.md](threat-model.zh-CN.md)。其结构即本案例的结构：

- **资产** —— 数据库、锁密码与派生密钥、MCP 凭据、同步秘密、导出副本、本地日志，以及用户选择存放的凭据。
- **攻击者** —— 七类，每类都有必须成立的边界与失效条件：同机他人（A1）、以你的身份运行的恶意软件（A2——设计上明确无边界）、持有数据副本的离线攻击者（A3）、同步路径上的网络攻击者（A4）、已获批准的 AI 客户端及其上游（A5）、分发渠道上的攻击者（A6）、物理或操作系统以下的访问（A7——范围外）。
- **假设与非目标** —— 强密码、未沦陷的系统账户、由用户负责的部署安全，以及明确立场：无密码找回、无遥测、无官方云。

威胁模型随代码库维护：「威胁模型与软件相矛盾即缺陷」。

## 3. 信任边界

[security-architecture.zh-CN.md](security-architecture.zh-CN.md) 描述了四条链，每条都把边界画在「不可信输入转化为可信状态」的位置：

1. **静态数据** —— 隐私锁链：密码 → 版本化 KDF → AES-256-GCM 覆盖版本化头；头部作为关联数据绑定。
2. **Agent 访问** —— MCP 链：解锁闸 → 按客户端批准 → 权限矩阵 → 字段脱敏 → 删除总闸 → 审计日志。
3. **快照导出** —— 互联链：强制导出密码、封包前剥除凭据、版本化容器。
4. **跨设备同步** —— 双向链：按空间密封的 keywrap、带明文版本戳的密文信封（服务端据此路由）、乐观并发（If-Match），以及保护旧客户端免受不可读格式冲击的世代锁。

链路上的信任边界：同步服务器对每个请求做空间/设备绑定校验、绝不解析载荷本体；Web 阅读器只在内存持有解密数据；更新渠道对清单字节先验签后解析。

## 4. 设计原则的适用

| 原则 | 适用之处 |
|---|---|
| 失效安全默认 | 不加密是可见且声明过的状态——但 MCP 接口默认关闭；更新渠道宁拒不降级（签名缺失或畸形即整体失败）；同步信封接受未知字段、未知值 fail-closed |
| 完全仲裁 | 每个同步请求按空间与设备认证；每次 MCP 调用经过解锁闸与权限矩阵；导出永远需要密码 |
| 最小权限 | 设备令牌按设备隔离且可撤销；Web 只读令牌不可写；编辑凭据是单一专属身份；MCP 权限按客户端、按字段并带删除总闸 |
| 最小攻击面 | 桌面应用无监听端口；自部署服务器按需启用且默认绑定回环；浏览器阅读器零持久化 |
| 职责分离 | CI 仅在绿时产出产物；签名无钥且钉死到产出工作流与标签；更新清单密钥独立于发布基础设施 |
| 机制经济性 | 备份/快照/同步共用一种容器格式；单一稳定序列化来源；单一且有文档的存储 schema |
| 倾向不信任 | 客户端输入永不求值：HTML 经严格白名单净化（AngleSharp）；导入先归一化校验再应用；同步服务器视载荷为不透明密文 |

## 5. 常见弱点映射

CWE Top 25 与 OWASP Top 10 中与桌面 C# 应用相关的条目，及其对应位置：

| 弱点 | 缓解 |
|---|---|
| 注入（CWE-78/79/89） | HTML 经严格白名单净化；无 SQL（单文件 JSON 存储）；不用用户数据拼 shell；编辑器内容渲染在带内容安全策略的沙箱化查看器中 |
| 加密失败（CWE-327/321） | 全链 AES-256-GCM、PBKDF2-SHA256 3,000,000 次（11.0 支持 Argon2id）、按用户随机盐、无硬编码密钥——空间密钥由用户生成且永不传输 |
| 不安全反序列化（CWE-502） | 信封 fail-closed 解析（缺字段即抛）、容器头迭代数有界窗口、清单格式严格校验、服务端绝不解析载荷本体 |
| 认证/会话失效（CWE-287/384） | 哈希设备令牌常量时间比较、鉴权失败限速、单调时钟锁定、单实例互斥与文件独占锁 |
| 路径遍历（CWE-22） | 服务端标识符校验（`IsSafeId`、64 位十六进制 chunk id）、路径仅由经验证的段构造 |
| 权限提升（CWE-250/269） | 按客户端的 MCP 权限矩阵、按设备可撤销令牌、签名政策中的角色分离 |
| SSRF / 非预期网络访问（CWE-918） | 出站连接仅在用户触发路径、全部列于网络活动面板；桌面应用不开监听端口 |
| 供应链（CWE-1357/OWASP A8） | 钉死的依赖（`packages.lock.json`）、Dependabot、CI 强制零警告、`SHA256SUMS`、钉死镜像摘要、SBOM、Sigstore 无钥签名、ECDSA 签名更新清单 |
| 日志与监控失效（CWE-778） | 同步与 MCP 动作本地审计日志、脱敏崩溃日志、日志无敏感信息（随发布配置文件的冻结基线强制） |

## 6. 如何核验这些主张

以上每条主张都可以在不信任本页的前提下核验：C1–C3 对照源码读链接的架构页；C4 对已发布产物走 [docs/VERIFYING.zh-CN.md](VERIFYING.zh-CN.md)；C5–C6 检查端点与服务器数据布局。若本页与产品不一致，错的是产品——请经 [SECURITY.zh-CN.md](../SECURITY.zh-CN.md) 第 2 节的私密渠道报告。

---

*最后更新：2026-10-13*
