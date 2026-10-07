# Novara Web 只读版 —— 部署与真机验收

这个目录只放**部署**相关的材料；站点本身在 `Novara.Web/`，由 `Novara.Server` 在 `/web` 同域托管。

| 文件 | 用途 |
|---|---|
| `Caddyfile` | 反代 + 自动证书样例（改一处域名即可用） |
| `README.md` | 首次部署（建空间与配对）、四条上线路径、威胁模型、真机验收清单、故障排查 |
| `SECURITY.md` | 服务端安全基线：日志脱敏禁令、密钥与凭据清单、传输层要求 |

---

## 0. 先看清链路：明文在哪一层

```text
手机浏览器 ──HTTPS──> Caddy ──HTTP(回环)──> NovaraSync ──> 磁盘上的密文
 │
 └─ 口令只在本机内存里参与计算（PBKDF2 解封 SpaceKey），从不发出
```

服务端持有的是：密文容器、keywrap 记录、只读令牌的 SHA-256。
**它不持有：明文、口令、令牌明文。**

| 环节 | 手段 |
|---|---|
| 传输 | TLS（Caddy）；服务端另有一道闸——`/api` 上的**非 HTTPS 且非回环**请求一律 403，见下 |
| 载荷 | v4 容器，AES-256-GCM |
| 密钥交接 | SpaceKey 由**口令**包装后存于服务端 —— PBKDF2-SHA256，3,000,000 次迭代，salt 32B / nonce 12B / tag 16B |
| 读取授权 | 只读令牌（120-bit，服务端只存 SHA-256；只允许 `GET space/keywrap` 与 `GET space/data`，其余 403） |

**服务端自己也会拒绝明文凭据**：NovaraSync 只讲 HTTP，TLS 由前置反代终止；如果反代没把 `X-Forwarded-Proto: https` 传进来（或有人直接用 `--urls http://0.0.0.0:5180` 暴露它），`/api` 请求会得到 **403 + `"https is required for api requests"`**，而不是默默收下凭据。回环地址豁免（本机调试与自检需要）。反代在另一台机器上时，还要把它的 IP 填进 `NOVARA_SYNC_TRUSTED_PROXIES`，否则它的转发头不被信任、同样会被这道闸拒掉。

**唯一薄弱环节是口令强度。** 拿到 keywrap 记录 + 只读令牌的人可以离线暴力破解口令（3,000,000 次 PBKDF2-SHA256 的迭代成本即为其门槛）。选择一个强口令比选哪条上线路径更重要。

---

## 0.5 首次部署：建空间与配对（只需一次，跳过必卡死）

服务器的 API 面没有"建空间"端点——空间只能由一次性 CLI 命令创建，然后**先配对、再谈只读访问**。新装服务器上没有空间时，桌面端的"生成令牌"是无处可点的。

1. **建空间**（在服务器这台机器上）：

 ```bash
 NovaraSync space create --name my-space
 ```

 打印 `space id` 与 `enrollment secret`，**仅此一次**，当场收好。注意：数据目录取 `NOVARA_SYNC_DATA` 环境变量或当前工作目录下的 `data`——请与服务器用**同一个目录**（同一工作目录或同一环境变量），否则凭据会落进另一个数据目录、服务器看不到这个空间。

2. **配对第一台设备（PC）**：桌面端 → 互联同步 → **配对**：填服务器地址、空间 ID、配对密钥（enrollment secret）；**空间密钥留空**——首次设备由 PC 生成，服务端从不持有它。配对成功会弹「妥善保存同步空间密钥」，这是第二台设备加入的唯一凭据。

3. 之后才到各上线路径的第 5 步：只读访问 → 生成令牌 → 复制链接。

第二台 PC 设备加入：同样走配对，空间密钥填**创建设备上显示的那把**（设置 → 查看空间密钥）。

---

## 1. 四条上线路径

| 路径 | 需要 | 隐私 | 适用 |
|---|---|---|---|
| **A. 域名 + Caddy 自动证书** | 一个域名 + 80/443 可达 | 好（只有你自己的机器） | **推荐**，一次配好长期用 |
| **B. Tailscale** | 两端装 Tailscale | **最好**（P2P，无第三方中转内容） | 无域名，或不愿开端口 |
| **C. Cloudflare 临时隧道** | 装 `cloudflared` | 一般（第三方可见密文与令牌） | **仅用于一次性验收** |
| **D. 局域网 + 自签证书** | 无 | 好 | 完全离线；但**必须在手机上装 CA**，最麻烦 |

### A. 域名 + Caddy 自动证书（推荐）

1. 域名 A/AAAA 记录指向这台机器，80 与 443 可入。
2. 改 `deploy/Caddyfile` 第一行的域名为你的域名。
3. `caddy run --config deploy/Caddyfile`
4. `NovaraSync.exe --urls http://127.0.0.1:5180`
5. **首次部署还没有空间**：先做 §0.5 的 `space create` 与桌面端配对，再回来。
6. 桌面端：设置 → 互联同步 → 只读访问 → 生成令牌 → 复制链接。

证书由 Caddy 自动申请与续期。**NovaraSync 本身不持证书**，所以务必让它只监听回环。

### B. Tailscale（隐私最好的无域名方案）

两端加入同一 tailnet 后，用 MagicDNS 名字访问；`tailscale cert <name>` 可签发该名字的真证书，交给 Caddy 用（`tls` 指令 + 证书路径）。这条路径的流量是 P2P 的，第三方不接触内容。

### C. Cloudflare 临时隧道（只用于验收）

```bash
cloudflared tunnel --url http://127.0.0.1:5180
```

会给出一个 `https://<随机>.trycloudflare.com` 的临时域名 —— **零配置就有可信 HTTPS**，特别适合"只想在手机上确认页面能不能用"。

必须知情：这条路径下 Cloudflare 能看到密文、keywrap 记录与只读令牌（都是明面上的东西），但**看不到明文**——解密发生在手机浏览器里，Cloudflare 没有口令。临时域名是公开可达的，**验收完立刻关掉**。

### D. 局域网 + 自签证书（要装 CA，否则白做）

Caddy 用内部 CA：

```caddyfile
novara.lan {
	tls internal
	reverse_proxy 127.0.0.1:5180 { ... }
}
```

**关键前提**：证书必须被手机**信任**。这一点不是可选项——

- 证书不受信任时，浏览器里那个页面**不是 Secure Context**，`crypto.subtle` **根本不会暴露**，页面会明确报"当前浏览器不支持所需的加密能力"；
- 点"继续访问"绕过警告也不行。

所以必须把 Caddy 的根证书装进手机：

1. 找到根证书（Caddy 数据目录 `pki/authorities/local/root.crt`；Windows 在 `%AppData%\Caddy\`，Linux 在 `~/.local/share/caddy/`，macOS 在 `~/Library/Application Support/Caddy/`）。
2. 传到手机并安装：iOS 为「设置 → 通用 → VPN与设备管理」安装描述文件后，**还必须**到「设置 → 通用 → 关于本机 → 证书信任设置」里**开启完全信任**（这一步最常被漏）；Android 为「设置 → 安全 → 加密与凭据 → 安装证书 → CA 证书」。
3. 用域名（而非 IP）访问——证书绑定的是域名。

**如果这三步里任何一步让你觉得麻烦，选 A 或 B。** 自签在手机上的安装体验决定了它不是省事的方案。

---

## 2. 真机验收清单

前置：桌面端已生成只读令牌并复制了访问链接；NovaraSync 在跑；反代已起。

**把链接传到手机**：复制后经任意 IM / 邮件发给自己，在手机上点开即可。手输那条含 `#` 的长 URL 是整条路径里最痛苦的一步，不必那样做。（链接里没有令牌，但仍不宜发到公开频道。）

### 2.1 反向代理正确性（先验这个，否则后面都是错的）

- [ ] 访问 `https://<host>/web`（**故意不带尾斜杠**）→ 跳转到 `https://<host>/web/`，**不是** `127.0.0.1`
 - 这个跳转是**相对地址**，与反代传什么 Host 无关；看不到内网地址即正确
- [ ] 访问 `https://<host>/` → 跳到 `/web/`
- [ ] 在跑脚本：`python Tools/web_selfcheck.py` → 全部通过
- [ ] 响应头里有 CSP 等安全头 —— 它们现在由**服务端**发出，所以走不经 Caddy 的隧道（路径 C）时也在

### 2.2 解锁页

- [ ] 页面加载出解锁卡片（令牌 + 口令两个字段），无脚本报错（DevTools Console 干净）
- [ ] 令牌**故意小写并带连字符** → 仍能通过（客户端会规范化）
- [ ] 令牌**故意把 `1` 打成 `I`、`0` 打成 `O`** → 仍能通过（Crockford 折叠）
- [ ] 输入**错误令牌** → 明确提示"令牌无效或已作废"（而非笼统失败）
- [ ] 输入**错误口令** → 明确提示"口令不正确"
- [ ] 正确凭据 → 进入四分区

### 2.3 只读内容

- [ ] 四个分区都能浏览（Memo / Path / Plan / Record）
- [ ] 搜索可用
- [ ] TOTP 动态码正常（需手机时间准确）
- [ ] 界面上**没有任何编辑、删除、新建入口**

### 2.4 无持久化核验（DevTools Application 面板）

- [ ] Local Storage 空
- [ ] Session Storage 空
- [ ] IndexedDB 空
- [ ] Cache Storage 里**只有** `novara-web-shell-*`（前缀 + 版本号；条目名不必与本文档逐字一致），条目**只含壳文件**，**没有任何 API / keywrap / data 条目**
- [ ] Network 面板：`/api/v1/space/*` 的响应头含 `Cache-Control: no-store`
- [ ] 关闭标签页后重新打开 → **必须重新输入令牌与口令**

### 2.5 只读令牌的边界

- [ ] 用令牌直接调 `PUT /api/v1/space/data` → **403**
- [ ] 桌面端点「作废令牌」→ 手机侧再次解锁**立即失败**

### 2.6 PWA 与离线

- [ ] 可以「添加到主屏幕」
- [ ] 从主屏图标启动会提示**"链接不完整"** —— **这是已知限制**：manifest 的 `start_url` 无法携带 fragment，而空间 ID 在 fragment 里。从书签/历史进入即可，不是缺陷。
- [ ] 首次正常加载后**断网**重开 → 解锁页仍能显示（壳已被缓存），解锁会失败（数据需要联网）

### 2.7 非安全上下文的表现（预期行为）

- [ ] 用 `http://`（非回环地址）访问 → 页面明确提示"当前浏览器不支持所需的加密能力"，**不是**白屏或技术栈错误

---

## 3. 故障排查

| 现象 | 原因 | 处理 |
|---|---|---|
| "当前浏览器不支持所需的加密能力" | 不是 Secure Context：明文 HTTP，或自签证书未被信任 | 用 HTTPS；自签需装 CA **并开启完全信任** |
| "链接不完整" | URL 里没有 `#s=<spaceId>` | 用桌面端复制的完整链接（从主屏图标启动也会这样，见 2.6） |
| "令牌无效或已作废" | 抄错、已被重新生成或作废 | 重新复制；`I/L→1`、`O→0` 已自动折叠，无需手工纠正 |
| "口令不正确" | 口令不是手机上的任何密码 | 是**包装 SpaceKey 的那个 Novara 解锁口令**，与桌面端"查看空间密钥"要求输入的一致 |
| 打开链接跳到 `127.0.0.1` | 反代未传 `X-Forwarded-Proto/Host` | 用 `deploy/Caddyfile`；若反代在**另一台机器**，把它的 IP 填进 `NOVARA_SYNC_TRUSTED_PROXIES` |
| `/api` 返回 **403 "https is required for api requests"** | 请求在服务端看来不是 HTTPS：反代没传 `X-Forwarded-Proto`，或反代在另一台机器但没进 `NOVARA_SYNC_TRUSTED_PROXIES`，或直接明文暴露了 5180 端口 | 按 `deploy/Caddyfile` 补 `header_up X-Forwarded-Proto {http.request.scheme}`；跨机反代补 `NOVARA_SYNC_TRUSTED_PROXIES`；不要把明文端口开到网络上 |
| 启动即退出，stderr 报 **refusing to start** | `NOVARA_SYNC_WEB_ROOT` 指向了包含数据目录的位置（例如整个应用根目录），静态托管会把密文库与 keywrap 暴露在 `/web` 下 | 指到纯站点目录（默认 `Novara.Web`），或把数据目录移出它 |
| 不能安装 PWA / manifest 报错 | 反代改写了 `application/manifest+json` | 跑 `python Tools/web_selfcheck.py` 定位 |
| Service Worker 未注册 | `sw.js` 的 MIME 不是 JavaScript；或缺 `viewer.js` 导致 `cache.addAll` 整体失败 | 同上 |

---

## 4. 环境变量速查（服务端）

| 变量 | 默认 | 说明 |
|---|---|---|
| `NOVARA_SYNC_DATA` | `data` | 数据目录。**不要**让它落在 `NOVARA_SYNC_WEB_ROOT` 之内，否则服务端拒绝启动 |
| `NOVARA_SYNC_WEB_ROOT` | 输出目录下的 `Novara.Web` | 站点目录；不存在则只跑 API。指向包含数据目录的路径会被**拒绝启动**；站点树内含 junction/符号链接同样被拒（：静态提供者跟着链接走） |
| `NOVARA_SYNC_TRUSTED_PROXIES` | 空 | 逗号分隔的 IP；反代在**别的机器**上时才需要（回环默认已信任）。不填而跨机反代 → `/api` 会因"看起来是明文"被 403 |
| `NOVARA_SYNC_ALLOWED_HOSTS` | 空 = 不过滤 | 逗号分隔的 Host 白名单；填了就只接受这些 Host（不匹配返回 400）。域名固定时**建议填**，是 Host 头注入的兜底。**只填主机名**：带端口或尾点的条目永远匹配不上（会让所有请求都 400），启动时会在 stderr 告警 |
| `NOVARA_SYNC_STORE` | `sqlite` | `file` 可选零依赖 JSON 存储 |
| `NOVARA_SYNC_QUOTA_BYTES` | 500 MiB | 单空间配额 |
| `NOVARA_SYNC_MAX_VERSIONS` | 10 | 版本保留数 |
| `NOVARA_SYNC_MAX_PAYLOAD_BYTES` | 64 MiB | 单次上传上限 |
| `NOVARA_SYNC_MAX_AUTH_FAILURES` | 10 | 认证失败阈值 |
