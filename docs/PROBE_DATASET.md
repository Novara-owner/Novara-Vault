# Building Your Own Probe Dataset

<p align="center"><strong>English</strong> · <a href="PROBE_DATASET.zh-CN.md">简体中文</a></p>

The relay probe (Memos → API check → Relay probe) uses an externalizable **probe dataset** containing benchmark questions, adversarial benchmark questions, and injection signature patterns. You can build or extend this JSON file yourself to cover newer cheating techniques — no new release needed; replace the file and it takes effect.

---

## File Locations and Read Priority

The dataset is read from the following locations in order, and the one with the **highest `version`** wins (ties go to the earlier entry):

| Priority | Location | Notes |
|---|---|---|
| 1 | `%LOCALAPPDATA%\Novara\ProbeDataSet.json` | Imported via the in-app "Load Probe Dataset" button; survives upgrades |
| 2 | `ProbeDataSet.json` in the install directory | Shipped with the release |
| 3 | Built-in default | Compiled into the code; the fallback when no file is present or valid |

A damaged or malformed file is skipped in favor of the next priority; if nothing parses, the built-in default applies. **A bad file can never crash the probe.**

## Field Reference

| Field | Type | Required | Description |
|---|---|---|---|
| `version` | integer | yes | Dataset version. Compared across files; the highest wins |
| `benchmarkQuestions` | array | yes | **Base tier** benchmark questions. Keep semantics stable (never reword a shipped question or change its expected answer), so results stay comparable across versions |
| `adversarialBenchmarks` | array | no | **Adversarial tier** benchmark questions. Append-only growth; entries are never removed once shipped (a proven false positive may be removed with a release note) |
| `poisoningStrongPatterns` | array | yes | Strong injection signatures (regex). A match reports "high risk" |
| `poisoningWeakPatterns` | array | yes | Weak injection signatures (regex). A match reports "suspicious" |

Each benchmark entry looks like `{ "key": "unique id", "prompt": "question sent to the model", "expected": "expected answer (a number)" }`.

## Constraints

- `benchmarkQuestions` must not be empty; at most the first **7** questions are kept.
- `adversarialBenchmarks` is optional, capped at **64** entries; each run samples from both tiers.
- Every question's `prompt` and `expected` must be non-empty — an empty `expected` would make scoring trivially true.
- Patterns are .NET regular expressions and must compile; the two pattern lists are capped at **64** entries combined.
- `expected` is compared numerically (a number appearing in the reply equal to the expected value counts as a pass), so design questions whose answer is an unambiguous number.

## Import / Apply / Revert

- **Import**: Memos page → API card → Relay probe → "Load Probe Dataset" at the bottom of the dialog, then pick the JSON file. After validation it is written to `%LOCALAPPDATA%\Novara\ProbeDataSet.json` and the app restarts to apply it.
- The **"How to build your own dataset?" link** in the dialog opens this page (Chinese or English, following the interface language).
- **Revert**: delete `%LOCALAPPDATA%\Novara\ProbeDataSet.json` and restart to fall back to the shipped dataset; or import a file with a lower `version` (a lower-version file loses to a higher one in the read chain).

## Full Example

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

## Honest Limits of Detection

Each probe run randomizes its question draw and request markers, which prevents relays from recognizing the probe by **traffic fingerprints** (fixed request prefixes, fixed question order). But **randomization cannot defeat semantic cheating**: some relays understand the questions, answer probes with a genuine model, and serve a downgraded model to real traffic. Growing the adversarial tier improves detection of such relays, but 100% recognition is not guaranteed — consistent with the note in the probe dialog. This check is probabilistic and intended for personal reference only.

---

## Related Pages

- [Novara Sync Deployment](QUICK_START.md)
- [MCP Interface](MCP.md)
