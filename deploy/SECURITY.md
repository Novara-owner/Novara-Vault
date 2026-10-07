# NovaraSync 服务端安全基线

> 本文是「服务端安全约束」的**单一出处**。
> 改动服务端代码前先过一遍；任何新代码若违反其中一条，即为缺陷。

## 1. 日志脱敏（最高优先级禁令）

- **禁止**把 `Authorization` 头（设备令牌 / 只读令牌）写进日志。
- **禁止**把 blob 内容（同步密文容器、keywrap 记录的明文形态）写进日志。
- **禁止**把 enrollment secret、只读令牌明文、编辑凭据明文写进日志或错误信息。
- 审计与日志只记**元数据**（哪个空间、哪个设备、什么动作、结果、字节数、时间戳）。
- 现状说明：服务端当前**没有任何应用层日志**，本节约束为空集满足——它的意义是约束**未来**：
  任何新加的 `logger.LogInformation(...)` / HTTP 日志中间件 / 异常上报，都不得把上述内容带出来。
  若确需调试日志，只允许记录**形状**（长度、哈希前几位、状态码），不允许记录**内容**。

## 2. 服务器持有什么、永不持有什么

| 项 | 状态 |
|---|---|
| 同步密文容器（blob） | 持有（密文，AES-256-GCM，服务器无法解密） |
| keywrap 记录 | 持有（密文：被库密码封装的 SpaceKey） |
| 设备令牌 / 只读令牌 | 只持有 **SHA-256**（明文只在签发响应中出现一次） |
| enrollment secret | 只持有 SHA-256 |
| **SpaceKey** | **永不持有**（由首次配对的 PC 生成、以 keywrap 密文形态经手） |
| 明文数据、库密码、口令 | **永不持有、永不接触** |

## 3. 凭据与比较

- 一切秘密比较走**常量时间比较**（`TokenAuth`）。
- 鉴权失败按 `(计数键, 窗口)` 限速（`FailureRateLimiter`）；`auth:` 与 `read:` 两个计数器独立。
- 配对注册（`POST /devices/register`）必须有 enrollment secret 门禁——知道 space_id 不足以注册。

## 4. 传输层

- NovaraSync 本身**不说 TLS**；TLS 由前置反代终止（见 `Caddyfile`）。服务端只监听回环，
  明文 HTTP 端口绝不暴露到网络。
- 反代必须传 `X-Forwarded-Proto` / `X-Forwarded-Host`，服务端 `UseForwardedHeaders` 只信任回环
  （跨机反代用 `NOVARA_SYNC_TRUSTED_PROXIES` 追加单个 IP）。
- **服务端自己拒绝明文凭据**（）：`/api` 上「非 HTTPS 且非回环」的请求返回 403，不进入端点。
  判据是**转发头生效之后**的 scheme，所以不受信任的反代无法声称一个它没提供的 HTTPS；
  回环豁免是本机调试与自检所必需。两个客户端本来就在发凭据前拒绝非回环明文，
  这道闸让服务端与它们对称——单侧防护在第三方客户端（curl / 脚本）面前是空的。
- `/web` 的尾斜杠跳转是**相对地址**，不由请求的 scheme/host 构造（）：绝对地址既是伪造 Host
  下的开放重定向，也是反代漏传转发头时的内网地址泄漏。`NOVARA_SYNC_ALLOWED_HOSTS` 是可选兜底
  （填了就只接受列出的 Host）。

## 5. API 响应缓存

- 全部 `/api` 响应由**服务端**设置 `Cache-Control: no-store`（绕过反代直连同样成立）。
- 静态壳资源**不得**设 `no-store`（Service Worker 的 `cache.addAll` 是整体失败语义）。

## 5.5 安全响应头

- `X-Content-Type-Options` / `X-Frame-Options` / `Referrer-Policy` / `X-Robots-Tag` / CSP 由**服务端**
  对**每个**响应发出（`SyncHostSetup`），与 `Caddyfile` 里那一份并存（纵深防御）——
  经 Cloudflare 临时隧道等**不经反代**的路径同样受保护（）。两份**必须保持一致**；
  改一处就改另一份 —— 自动自检只比对服务端这份的自身常量，**不读 `Caddyfile`**，反代侧两份头的
  一致性目前靠运维纪律维持（删掉 Caddyfile 中的指令不会让任何自检变红）。
- CSP 里的两个 `'unsafe-inline'` 是必需的（`index.html` 有一处解析期定语言的内联脚本 + 两处
  `style=""`）；真正防注入的指令（`default-src` / `connect-src` / `base-uri` / `form-action` /
  `frame-ancestors` / `object-src`）全部关闭。

## 6. 存储边界

- space_id 高熵（128-bit）且落盘前经文件系统安全校验（`BlobLayout.IsSafeId`），两存储实现一致。
- blob 与元数据的布局由两存储实现共享且相互一致（SQLite 删行在事务内、文件后端整目录原子改名后删）；服务端对 blob 字节不可解释、不校验内容（客户端 GCM tag 负责完整性）。
- **站点目录绝不包含数据目录**（）：`NOVARA_SYNC_WEB_ROOT` 指向的路径里若有 `NOVARA_SYNC_DATA`
  的数据（keywrap 记录、密文 blob、SQLite 元数据），静态托管就等于把它们公开在 `/web/...` 下。
  服务端启动时校验并**拒绝启动**（`SyncHostSetup.ValidateWebRoot`）；只有"数据在站点目录之下"
  这一个方向有风险——站点目录在数据目录之下是安全的。
  ****：上面两条都是**词法**比较，而 `Path.GetFullPath` 不解重解析点 —— 站点树内（含站点根
  自身）出现 junction / 符号链接时同样**拒绝启动**：静态提供者跟着链接走（用服务端进程自己的权限），
  而链接指向何处无法在不解析的前提下判断。若确实需要把外部内容纳入站点，请把内容搬进站点目录。
