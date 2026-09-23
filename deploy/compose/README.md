# NovaraSync · Docker Compose 一键部署

面向自托管用户。**不用 Docker 的裸机部署**见上一级目录的 `README.md`（那份也是四条上线路径的完整说明）。

---

## 0. 前置条件

- Docker 与 Compose v2（`docker compose version` 有输出即可）
- 一个域名（走 https profile 时必需；没有域名见 §4）
- **数据目录必须落在本机磁盘。** SQLite 的 WAL 依赖共享内存与文件锁，放在 NFS/SMB 共享上属于**数据损坏风险**，不是性能问题 —— 不要把 `./data` 指到别的机器挂过来的网络盘上。

---

## 1. 首跑三步

### 第一步：建空间，拿到一次性凭据

```bash
cp .env.example .env
# 编辑 .env：至少填 NOVARA_DOMAIN 与 NOVARA_SYNC_ALLOWED_HOSTS

docker compose run --rm novara-sync space create --name 我的空间
```

输出里会有 **space id** 与 **enrollment secret** —— 这两样**只显示这一次**，请当场存好（服务端只存哈希，找不回来）。

> **为什么用 `run --rm` 而不是 `exec`**：这一步不需要服务端在跑，`run` 起一个一次性容器，挂的是同一个 `./data`，写完即退。凭据落在 `./data/novara-sync.db` 里，与服务端同源。

### 第二步：起服务

```bash
docker compose --profile https up -d
```

Caddy 会自动申请证书（需要 80/443 可达），等几十秒后：

```bash
docker compose ps            # 两个服务都应是 running，novara-sync 应显示 (healthy)
```

### 第三步：客户端配对

在 Novara 桌面端 → **设置 → 互联同步 → 配对**，填三样：

| 字段 | 填什么 |
|---|---|
| 服务器地址 | `https://<你的域名>` |
| space id | 第一步输出的那一串 |
| enrollment secret | 第一步输出的那一串 |
| **space key** | **留空** |

**space key 留空是刻意的**：第一台设备自己生成它，**服务端全程看不到**。配对成功后到「设置 → 互联同步」里可以看到并另存空间密钥 —— 后续设备加入时需要它。

配对完成后，同一页面可以生成**只读访问链接**发给手机：`https://<你的域名>/web/#s=<spaceId>`。

---

## 2. 两条部署形态

| 命令 | 起什么 | 什么时候用 |
|---|---|---|
| `docker compose up -d` | 只有服务端，端口只发布在 `127.0.0.1:5180` | TLS 由**别处**终止：Tailscale、Cloudflare 隧道，或这台机器上已有的反代 |
| `docker compose --profile https up -d` | 服务端 + Caddy（自动证书） | 有域名，让 Caddy 直接对外 |

**明文 HTTP 访问 API 是被刻意拒绝的。** 服务端会拒绝「未受信任来源 + 明文」的 `/api` 请求（返回 403），所以 `http://<nas>:5180` 直连会一路 403 —— 这是规则在生效，不是配置错误。桌面端与浏览器阅读器出于同样的理由拒绝回环之外的明文。

---

## 3. 数据目录、备份与恢复

### 两种形态，选一个

**默认：bind mount `./data`**（看得见、能直接备份）

```yaml
volumes:
  - ./data:/data
```

`./data` 的属主来自宿主机，而容器以**非 root** 用户运行（uid 1654）。属主不对时**服务端会拒绝启动**并打印该执行的那条命令：

```
NovaraSync: refusing to start - the data directory (/data) cannot be written (...)
... fix it with `sudo chown -R 1654:1654 ./data`, or switch to the named volume ...
```

照它说的执行即可。**这是设计好的报错，不是崩溃** —— 与其让 SQLite 在每个请求上失败，不如启动时把话说清楚。

**替代：named volume**（零权限问题）

把上面那行换成 `- novara-data:/data`，并取消 `docker-compose.yml` 末尾 `novara-data:` 的注释。Docker 会用镜像里 `/data` 的属主播种新卷，因此不需要任何 chown。代价是文件不在你的目录树里，备份要靠 `docker run --rm -v ... tar`。

### 备份

```bash
docker compose stop novara-sync
cp -a ./data ./data-backup-$(date +%Y%m%d)      # 整个目录，含 novara-sync.db 与 spaces/
docker compose start novara-sync
```

恢复就是反过来：停服务 → 把备份目录换回去 → 起服务。

> 镜像里**刻意不带 `sqlite3` 命令行**（少一个攻击面，也符合「不往镜像里塞诊断二进制」的既定纪律）。要查库请在宿主机上用工具打开 `./data/novara-sync.db`。服务端存的是**密文**，备份的敏感度低于客户端本地库。

---

## 4. 没有域名怎么办

在 `Caddyfile` 里取消 `# tls internal` 那一行的注释，然后正常 `--profile https up -d`。Caddy 会自签一张证书。

**但必须让每台要打开阅读器的设备都信任那个 CA** —— 浏览器在证书不被信任时**根本不是安全上下文**，`crypto.subtle` **根本不会暴露**，阅读器连解密都做不到，点「继续访问」也没用。具体做法见上一级 `README.md` 的「局域网（自签证书）」。

---

## 5. 升级

```bash
docker compose pull
docker compose up -d
```

v4 容器格式与存储 schema 不变，数据天然向前兼容。**升级前先备份**（§3）。

---

## 6. 故障排查

| 现象 | 原因 | 处理 |
|---|---|---|
| `/api` 一律 **403** `https is required for api requests` | 请求在服务端看来不是 HTTPS：Caddy 的地址没进 `NOVARA_SYNC_TRUSTED_PROXIES`，或反代没传 `X-Forwarded-Proto`，或你把明文端口开到了网络上 | 确认 `NOVARA_SYNC_TRUSTED_PROXIES` 含 `172.28.0.250`；确认 `Caddyfile` 里那两行 `header_up` 还在 |
| 服务端启动即退出，日志里是 `refusing to start - the data directory ...` | `./data` 属主不对（bind mount 的经典陷阱） | 按报错里的 `chown -R 1654:1654 ./data` 执行 |
| `docker compose up` 报 `Address already in use` | 固定 IP 被别的容器先占了（Docker 的动态分配从低地址往上走） | `docker compose down` 后重试；若反复出现，把 `172.28.0.0/24` 换成不冲突的网段，并同步改 Caddy 的 `ipv4_address` 与 `NOVARA_SYNC_TRUSTED_PROXIES` |
| 容器报 `unhealthy`，但服务本身访问正常 | 健康检查被服务端自己的 Host 过滤拒掉了 | 已在镜像内修好（探测头取自 `NOVARA_SYNC_ALLOWED_HOSTS`）；若你自定义了该变量成多段形式，确认第一段是主机名 |
| `docker compose ps` 里 novara-sync 一直 `starting` | 数据目录写不进去，或端口被占 | `docker compose logs novara-sync` 看第一行 |
| Caddy 拿不到证书 | 80 端口没通到这台机器，或域名 A 记录不对 | `docker compose logs caddy`；确认 80/443 都可达 |
| 打开链接跳到 `127.0.0.1` | 反代没转发 `X-Forwarded-Proto/Host` | 用仓库内的 `Caddyfile`；跨机反代把它的 IP 加进 `NOVARA_SYNC_TRUSTED_PROXIES` |
| 直接访问 `http://<主机>:5180/healthz` 得到 **400** | Host 过滤在工作：`Host: <主机>:5180` 不在 `NOVARA_SYNC_ALLOWED_HOSTS` 里 | 这是预期的。健康检查由容器自己在内部做；要从宿主探测就带上 `-H "Host: <你的域名>"` |
| `docker compose up` 报 `set NOVARA_DOMAIN in .env` | `.env` 没建或没填 | `cp .env.example .env` 后填 `NOVARA_DOMAIN` |

---

## 7. 与裸机部署的关系

**同一套代码、同一组环境变量、同一份契约**（`C5`：镜像与裸二进制行为必须一致）。两条路只有三点不同：

1. 数据目录的路径（容器里固定是 `/data`）
2. `NOVARA_SYNC_TRUSTED_PROXIES` 要写上 Caddy 容器的地址（裸机时 Caddy 在回环上，默认就被信任）
3. 站点目录由镜像自带（`NOVARA_SYNC_WEB_ROOT` 默认指向应用目录下的 `Novara.Web`，无需配置）

`Caddyfile` **只有一份**，两条路共用：compose 通过 `NOVARA_UPSTREAM=novara-sync:5180` 指向容器，裸机用默认的 `127.0.0.1:5180`。
