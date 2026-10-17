# 自制中转站探针数据集

<p align="center"><a href="PROBE_DATASET.md">English</a> · <strong>简体中文</strong></p>

Novara 的中转站探针检测（备忘页 → API 连通检测 → 中转站探针）使用一份可外置的**探针数据集**，包含基准问题、对抗基准问题与注入特征模式。你可以自制或扩充这份 JSON 文件，让检测覆盖更新的作弊手法——无需等待新版本，替换文件即可生效。

---

## 文件位置与读取优先级

探针数据集按以下顺序读取，**取 `version` 最高的一份**（`version` 相同时先列出的优先）：

| 优先级 | 位置 | 说明 |
|---|---|---|
| 1 | `%LOCALAPPDATA%\Novara\ProbeDataSet.json` | 通过应用内「加载外部探针数据集」导入，升级不会被覆盖 |
| 2 | 程序安装目录下的 `ProbeDataSet.json` | 随版本内置 |
| 3 | 程序内置默认 | 内置于代码中，文件缺失或无效时的兜底 |

某一处的文件损坏或格式非法时会被跳过，继续读取下一优先级；全部无效时回落到内置默认。**任何情况下检测都不会因坏文件而崩溃。**

## 字段说明

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `version` | 整数 | 是 | 数据集版本号。读取时跨文件比较，最高者生效 |
| `benchmarkQuestions` | 数组 | 是 | **基础层**基准问题。语义应保持稳定（题目与预期答案一经发布不再改动），便于跨版本对比 |
| `adversarialBenchmarks` | 数组 | 否 | **对抗层**基准问题。持续追加、旧条目不删除（证实误判的条目可随版本说明移除） |
| `poisoningStrongPatterns` | 数组 | 是 | 强注入特征（正则）。命中即判「高危」 |
| `poisoningWeakPatterns` | 数组 | 是 | 弱注入特征（正则）。命中判「可疑」 |

基准问题条目结构：`{ "key": "唯一标识", "prompt": "发给模型的问题", "expected": "预期答案（数字）" }`。

## 约束

- `benchmarkQuestions` 不能为空；单次运行最多取前 **7** 题。
- `adversarialBenchmarks` 可选，上限 **64** 条；单次运行从两层分别抽样执行。
- 每题 `prompt` 与 `expected` 均不得为空——空的 `expected` 会让判分恒真。
- 特征模式为 .NET 正则，必须可编译；两类模式合计上限 **64** 条。
- `expected` 按数值比较（回复中出现的数字与预期值相等即算通过），因此题目设计应保证答案为明确的数字。

## 导入 / 生效 / 回退

- **导入**：备忘页 → API 卡片 → 中转站探针 → 弹窗底部「加载外部探针数据集」，选择 JSON 文件。校验通过后写入 `%LOCALAPPDATA%\Novara\ProbeDataSet.json` 并自动重启生效。
- **弹窗内的「如何自制数据集？」链接**会按界面语言打开本页（中文或英文）。
- **回退**：删除 `%LOCALAPPDATA%\Novara\ProbeDataSet.json` 后重启，即回到随版本内置的数据集；或导入一份更低 `version` 的文件（低版本文件会被高版本覆盖优先级）。

## 完整示例

```json
{
  "version": 2,
  "benchmarkQuestions": [
    { "key": "strawberry", "prompt": "How many times does the letter 'r' appear in the word 'strawberry'? Answer with just the number.", "expected": "3" },
    { "key": "compare", "prompt": "Which number is larger: 9.11 or 9.9? Answer with just the number.", "expected": "9.9" },
    { "key": "math", "prompt": "What is 24 * 7 + 5? Answer with just the number.", "expected": "173" }
  ],
  "adversarialBenchmarks": [
    { "key": "adv-count", "prompt": "How many times does the letter 'o' appear in the word 'collaboration'? Answer with just the number.", "expected": "3" },
    { "key": "adv-words", "prompt": "How many words are in the sentence 'The quick brown fox jumps over the lazy dog'? Answer with just the number.", "expected": "9" }
  ],
  "poisoningStrongPatterns": [
    "\\b(curl|wget)\\s",
    "\\b(sh|bash)\\s+-c",
    "powershell",
    "cmd\\.exe",
    "[\\u200B-\\u200D\\uFEFF]",
    "data:image/[^;]+;base64,[A-Za-z0-9+/=]{100,}"
  ],
  "poisoningWeakPatterns": [
    "!\\[[^\\]]*\\]\\(https?://",
    "[A-Za-z0-9+/=]{200,}"
  ]
}
```

## 检测能力的诚实边界

探针每次运行的题目抽样顺序与请求标记均为随机生成，可防止中转站通过**流量特征**（固定的请求前缀、固定的题目顺序）识别探针。但**随机化无法防御语义识别型作弊**：部分中转可以理解题意、对探针请求返回正版模型应答、对正常业务请求返回降级模型。扩充对抗层题库可以提升此类中转的检出率，但无法保证 100% 识别——这与检测弹窗中的说明一致。本检测为概率性判断，仅作个人自查参考。

---

## 相关页面

- [Novara Sync 部署](QUICK_START.md)
- [MCP 接口](MCP.md)
