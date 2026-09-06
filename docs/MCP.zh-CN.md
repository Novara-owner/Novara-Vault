# Novara MCP 接口

<p align="center"><a href="MCP.md">English</a> · <strong>简体中文</strong></p>

Novara 内置原生 **MCP（Model Context Protocol）服务器**，让任何 AI 助手——Claude、编程助手或自定义 Agent——都能作为一等客户端读写你的 Novara 数据。本页记录工具清单、配置方式与安全模型。

---

## 概览

| | |
|---|---|
| 可执行文件 | `NovaraMCP.exe` |
| 传输 | stdio（标准输入/输出），换行分隔的 JSON-RPC 2.0 |
| 协议版本 | `2024-11-05`、`2025-03-26`、`2025-06-18` |
| 数据类型 | `memo` / `todo` / `note` / `diary` / `path` |
| 工具数 | 14 |

MCP 服务器是**零逻辑的 stdio 前端**。每个工具调用都会通过本地命名管道转发给运行中的 Novara 主程序（唯一数据权威），由主程序完成实际读写并持久化变更。

## 前置条件

1. Novara **正在运行**，且数据库**已解锁**。
2. Novara 中已**开启** MCP 接口：设置 → MCP 卡片。
3. 你已从该卡片拿到**令牌**。

## 配置

在你的 MCP 客户端中配置启动 `NovaraMCP.exe`。令牌可通过参数或环境变量传入：

```json
{
  "mcpServers": {
    "novara": {
      "command": "NovaraMCP.exe",
      "args": ["--token", "YOUR_TOKEN_HERE"]
    }
  }
}
```

或通过环境变量：

```json
{
  "mcpServers": {
    "novara": {
      "command": "NovaraMCP.exe",
      "env": {
        "NOVARA_MCP_TOKEN": "YOUR_TOKEN_HERE"
      }
    }
  }
}
```

任何客户端进程**首次连接**时，Novara 会弹出授权提示。批准后即进入该客户端的**权限矩阵**——读 / 建 / 改 / 删 × 五类数据共 20 位权限格。新客户端**除备忘外默认只读**；此后你可以随时按客户端放权（删除列还需全局总闸放行）。

## 工具参考

所有工具参数使用 camelCase。ID 为 GUID 字符串（`diary` 例外，其 ID 为字符串）。`type` 取 `memo`、`todo`、`note`、`diary`、`path` 之一。

### 创建

| 工具 | 必填 | 可选 | 说明 |
|------|------|------|------|
| `create_memo` | `name`、`type` | `keyInfo`、`fields[]`、`groupId`、`iconKey` | `type` ∈ 邮箱 / 账户 / API Key / 网站 / 银行卡 / WiFi / 证件 / 自定义 |
| `create_todo` | `title`、`mainText` | `subTexts[]`、`iconKey` | |
| `create_note` | `title`、`content` | `iconKey` | |
| `create_diary` | `title`、`content` | `format` | `format` ∈ `markdown`（默认）/ `html`；HTML 会经白名单净化 |
| `create_path` | `name`、`path` | `note` | |

`fields` 为 `{ "label": string, "value": string, "canCopy": boolean }` 数组。

### 更新

| 工具 | 必填 | 可选 |
|------|------|------|
| `update_memo` | `id` | `name`、`type`、`keyInfo`、`fields[]`、`groupId`、`iconKey` |
| `update_todo` | `id` | `title`、`mainText`、`subTexts[]`、`iconKey` |
| `update_note` | `id` | `title`、`content`、`iconKey` |
| `update_diary` | `id` | `title`、`content` |
| `update_path` | `id` | `name`、`path`、`note` |

注意：`update_diary` 不能修改 `format`。向 `update_todo` 传 `subTexts` 会按新列表整体重建，勾选状态重置为未勾选。

### 删除 / 列出 / 读取 / 搜索

| 工具 | 参数 | 说明 |
|------|------|------|
| `delete_item` | `type`、`id` | 软删除（进回收站）；需单独开启删除权限 |
| `list_items` | `type?` | 仅摘要（不含正文）；不含回收站 |
| `read_item` | `type`、`id` | 全文视图；敏感字段已脱敏 |
| `search_items` | `query`、`type?` | 全类型全文搜索；不含回收站 |

## 安全模型

纵深防御，在设置页 → MCP 卡片开启：

1. **默认关闭**——你主动开启，然后复制令牌。
2. **令牌鉴权**——固定时间比较；通过 `NOVARA_MCP_TOKEN` 或 `--token` 传入。
3. **数据库解锁门控**——锁着或加密的数据库会拒绝一切请求。
4. **逐客户端授权与权限矩阵**——任何进程首次连接都需你批准；每个已授权客户端持有独立的 20 位权限格（读 / 建 / 改 / 删 × 五类数据）。混合（全类型）列举绝不会泄露无权分区的内容。
5. **删除总闸**——删除列需要客户端自身权限位与全局总闸同时放行，二者取与。
6. **审计日志**——每次调用（含被拒绝的尝试）都落本地日志：进程、时间、工具、对象、结果。字段限长，凭据形态的值一律掩码。
7. **敏感字段脱敏**——标签匹配 `密码`、`password`、`密钥`、`secret`、`token`、`key`、`api key`、`apikey`、`api_key`、`passwd` 的字段返回为 `****`，不参与搜索，并防止改名绕过脱敏。

> **数据边界：** `NovaraMCP.exe` 绝不把数据发往任何地方。若你接入的是**云端托管**的 AI 客户端，那么助手读到的数据可能由该 AI 客户端发往你所选的 AI 服务商——这由 AI 客户端控制，而非 Novara。详见[隐私政策](../PRIVACY.zh-CN.md)。

## 示例

```
# 新建一个备忘
create_memo { "name": "OpenAI", "type": "API Key", "keyInfo": "https://api.openai.com/v1", "fields": [{ "label": "API Key", "value": "sk-...", "canCopy": true }] }

# 列出所有待办
list_items { "type": "todo" }

# 搜索 "project"
search_items { "query": "project" }
```
