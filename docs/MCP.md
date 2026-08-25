# Novara MCP Interface

<p align="center"><strong>English</strong> · <a href="MCP.zh-CN.md">简体中文</a></p>

Novara ships a native **MCP (Model Context Protocol) server** that lets any AI agent — Claude, coding assistants, or custom agents — read and write your Novara data as a first-class client. This page documents the tools, configuration, and security model.

---

## Overview

| | |
|---|---|
| Executable | `NovaraMCP.exe` |
| Transport | stdio (standard input/output), newline-delimited JSON-RPC 2.0 |
| Protocol versions | `2024-11-05`, `2025-03-26`, `2025-06-18` |
| Data types | `memo` / `todo` / `note` / `diary` / `path` |
| Tools | 14 |

The MCP server is a **zero-logic stdio frontend**. Every tool call is forwarded over a local named pipe to the running Novara app (the single data authority), which performs the actual read/write and persists changes.

## Prerequisites

1. Novara is **running** and its database is **unlocked**.
2. The MCP interface is **enabled** in Novara: Settings → MCP card.
3. You have the **token** from that card.

## Configuration

Configure your MCP client to launch `NovaraMCP.exe`. Pass the token either as an argument or an environment variable:

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

Or via environment variable:

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

On the **first connection from any client process**, Novara shows an authorization prompt. Approve it to add the process to the whitelist; subsequent connections from the same process proceed without prompting.

## Tool reference

All tool arguments use camelCase. IDs are GUID strings (except `diary`, whose IDs are strings). `type` is one of `memo`, `todo`, `note`, `diary`, `path`.

### Create

| Tool | Required | Optional | Notes |
|------|----------|----------|-------|
| `create_memo` | `name`, `type` | `keyInfo`, `fields[]`, `groupId`, `iconKey` | `type` ∈ 邮箱 / 账户 / API Key / 网站 / 银行卡 / WiFi / 证件 / 自定义 |
| `create_todo` | `title`, `mainText` | `subTexts[]`, `iconKey` | |
| `create_note` | `title`, `content` | `iconKey` | |
| `create_diary` | `title`, `content` | `format` | `format` ∈ `markdown` (default) / `html`; HTML is sanitized |
| `create_path` | `name`, `path` | `note` | |

`fields` is an array of `{ "label": string, "value": string, "canCopy": boolean }`.

### Update

| Tool | Required | Optional |
|------|----------|----------|
| `update_memo` | `id` | `name`, `type`, `keyInfo`, `fields[]`, `groupId`, `iconKey` |
| `update_todo` | `id` | `title`, `mainText`, `subTexts[]`, `iconKey` |
| `update_note` | `id` | `title`, `content`, `iconKey` |
| `update_diary` | `id` | `title`, `content` |
| `update_path` | `id` | `name`, `path`, `note` |

Notes: `update_diary` cannot change `format`. Passing `subTexts` to `update_todo` rebuilds the list and resets checked states.

### Delete / List / Read / Search

| Tool | Arguments | Notes |
|------|-----------|-------|
| `delete_item` | `type`, `id` | Soft delete (moves to the recycle bin); requires the separate delete permission |
| `list_items` | `type?` | Summaries only (no content); omits the recycle bin |
| `read_item` | `type`, `id` | Full view; sensitive fields redacted |
| `search_items` | `query`, `type?` | Full-text across all types; omits the recycle bin |

## Security model

Defense-in-depth, enabled in Settings → MCP card:

1. **Off by default** — you opt in, then copy a token.
2. **Token authentication** — fixed-time comparison; passed via `NOVARA_MCP_TOKEN` or `--token`.
3. **Database-unlock gate** — a locked or encrypted database refuses every request.
4. **Process whitelist** — first connection from any process requires your approval, persisted in `McpAllowedProcesses`.
5. **Separate delete permission** — deletion is its own opt-in toggle (`McpDeleteEnabled`).
6. **Sensitive-field redaction** — fields whose label matches `密码`, `password`, `密钥`, `secret`, `token`, `key`, `api key`, `apikey`, `api_key`, or `passwd` are returned as `****`, excluded from search results, and protected against relabeling-based extraction.

> **Data boundary:** `NovaraMCP.exe` never transmits data anywhere. If you connect a **cloud-hosted** AI client, the AI client itself may send data the agent reads to the AI provider you chose — that is controlled by the AI client, not by Novara. See the [Privacy Policy](../PRIVACY.md) for details.

## Example

```
# create a memo
create_memo { "name": "OpenAI", "type": "API Key", "keyInfo": "https://api.openai.com/v1", "fields": [{ "label": "API Key", "value": "sk-...", "canCopy": true }] }

# list all todos
list_items { "type": "todo" }

# search for "project"
search_items { "query": "project" }
```
