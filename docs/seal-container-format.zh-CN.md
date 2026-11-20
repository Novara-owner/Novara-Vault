# Novara 加密容器格式规范 v1

**Sealed item container, key wrap, index and export bundle — public format reference**

本文件是**对外可公开的格式说明**：只描述字节，不描述 Novara 的内部实现。任何读懂本文的人，用任何语言、任何工具，都应当能把一个 `.novaseal` 文件解回原始明文，把一份 `keywrap.nskw` 用密码打开，把 `index.nseal` 读成一张表，把 `.novabundle` 拆成若干容器 —— 无需 Novara 程序，无需本文档之外的假设。

> 面向的读者是二十年后的人。因此文中不使用任何产品内部代号，也不依赖任何库的私有行为；一切"为什么"都写成可验证的规则。

格式版本：全部为 **v1**（首次发布版本）。文中所有偏移从文件或结构体的第一个字节起算。

---

## 1. 通用约定

| 项目 | 约定 |
|---|---|
| 字节序 | 所有定长整数一律 **little-endian**（无符号写 `u32`/`u64`，有符号写 `i64`） |
| 密钥长度 | 32 字节（AES-256） |
| GCM nonce | 12 字节，每个密钥每次加密必须唯一 |
| GCM tag | 16 字节，附在密文之后（`cipher‖tag`） |
| 加密算法 | AES-256-GCM，v1 只定义这一种 |
| 压缩 | 无。密文与明文等长，仅多出 nonce 与 tag |
| 填充 | 无。所有结构都是紧排的 |
| 文本编码 | UTF-8，无 BOM |
| SHA-256 | 标准 FIPS 180-4，输出 32 字节；文中用小写十六进制表示 |
| HEX 写法 | 本文所有 `hex` 均为小写、无分隔符；`base64` 为标准字母表带 `=` 填充 |

**认证（AAD）规则**：本格式中每个 AEAD 调用都带 associated data，没有一处是"裸加密"。AAD 的作用是把结构位置绑进密文：同一把密钥加密同样的明文，换一个位置、换一个块序号、换一个包头，解密就会失败。读者必须按本文的原样构造 AAD，不能省略、不能补零；长度差一个字节即视为认证失败。

**失败一律是失败**：任何校验不通过都不得"修复""猜测"或"跳过"。见 §8。

---

## 2. `.novaseal` —— 单个加密文件（或打包成 ZIP 的文件夹）

一个容器 = **128 字节定长头** + **0 个或多个块**。没有文件尾，也没有可选的扩展区：读完最后一个块即文件结束。

```
+--------------------------- 128 B header ---------------------------+
| magic | ver | algo | flags | itemId | length | sealedAt | chunk | keySlot | rsv |
+--------------------------------------------------------------------+
| [u32 payloadLen][nonce 12][cipher][tag 16]   ← block 0              |
| [u32 payloadLen][nonce 12][cipher][tag 16]   ← block 1              |
| ...                                                                 |
+---------------------------------------------------------------------+
```

### 2.1 头（128 字节）

| Offset | Size | Field | Encoding | Meaning |
|---|---|---|---|---|
| 0 | 8 | magic | ASCII `NOVASEAL` | 容器标识。必须逐字节相等 |
| 8 | 1 | version | u8 | 格式版本。v1 写 `1` |
| 9 | 1 | algoId | u8 | 内容算法。v1 只定义 `1` = AES-256-GCM |
| 10 | 1 | flags | u8 | bit0 = 1 表示明文是一个 ZIP（文件夹）；其余位在 v1 未定义 |
| 11 | 1 | reserved | u8 | 必须为 `0` |
| 12 | 16 | itemId | 16 B | 条目标识。同时是明文侧文件名与每个块 AAD 的第一段 |
| 28 | 8 | plaintextLength | u64 | 明文总长度（字节）。`0` 合法 |
| 36 | 8 | sealedAtUtc | i64 | 封存时刻，Unix epoch 秒（UTC），封存后不再改写 |
| 44 | 4 | chunkSize | u32 | 每个块的明文上限。必须是 2 的幂，且在 [4096, 4194304] |
| 48 | 64 | keySlot | 64 B | 内容密钥的封装，见 §2.2 |
| 112 | 16 | reserved2 | 16 B | 必须全为 `0` |

固定长度汇总：

| Value | 值 | 说明 |
|---|---|---|
| headerSize | 128 | 头的总长 |
| authenticatedPrefix | 48 | 头的第 0..47 字节，keySlot 的 AAD 就是这 48 字节 |

读取规则（顺序无关，全部必须成立）：

- `magic` 不等 → 不是本格式。
- `version == 0` 或 `version > 1` → **版本不支持**。这不是损坏：文件大概率完好，只是读者太旧。用户应被提示"需要更新的版本"，而不是"数据已损坏"。
- `algoId != 1` → 不支持的算法，拒绝。
- `flags` 高于 bit0 的位非零 → 拒绝。`reserved` 与 `reserved2` 非零 → 拒绝。
- `chunkSize` 不是 2 的幂或越界 → 拒绝（避免读者被要求分配任意大的缓冲）。
- `sealedAtUtc` 必须落在 `946684799 … 253402300799`（即 1999-12-31T23:59:59Z 至 9999-12-31T23:59:59Z）。超出视为损坏。
- `plaintextLength` 必须 ≤ 2^63−1。

### 2.2 密钥槽（头内 64 字节，偏移 48）

| Offset | Size | Field | Encoding | Meaning |
|---|---|---|---|---|
| 0 | 12 | slotNonce | 12 B | 本次封装用的 GCM nonce |
| 12 | 32 | wrappedKey | 32 B | 内容密钥的密文 |
| 44 | 16 | slotTag | 16 B | GCM tag |
| 60 | 4 | slotReserved | 4 B | 必须全为 `0`；**不在 tag 覆盖范围内**，只能结构校验 |

打开方式（伪代码）：

```
prefix = container[0:48]                       # 被认证的头前缀
ck     = AES256GCM_decrypt(key   = VMK,
                           nonce = slot[0:12],
                           data  = slot[12:60],   # cipher‖tag
                           aad   = prefix)
```

- `VMK` 是由密码封装出来的空间主密钥（见 §3），32 字节。
- 因为 AAD 是头前缀，**改动头里任何一个被认证的字节**都会让槽打不开 —— 篡改表现为"打不开"，而不是"打开了另一个合法头"。
- 四个保留字节在 tag 之外，读者必须另外检查其为 0；否则一个改成非零的槽仍能打开，校验就成了摆设。
- 只有两种结果：打开（得到 32 字节 CK）或打不开。打不开时不要把原因细分给用户（密码错、头被改、槽被改三者不可区分）。

### 2.3 块

每个块紧接前一个块，布局固定：

| Offset | Size | Field | Encoding | Meaning |
|---|---|---|---|---|
| 0 | 4 | payloadLen | u32 | **nonce 起点到 tag 终点**的字节数，即 `12 + 明文块长 + 16` |
| 4 | 12 | nonce | 12 B | 本块 GCM nonce |
| 16 | payloadLen−28 | cipher | 同明文等长 | 本块密文 |
| 16+(payloadLen−28) | 16 | tag | 16 B | 本块 GCM tag |

块 AAD（37 字节）：

| Offset | Size | Field | Encoding | Meaning |
|---|---|---|---|---|
| 0 | 16 | itemId | 16 B | 与头里的 itemId 相同 |
| 16 | 4 | blockIndex | u32 | 从 `0` 开始的块序号 |
| 20 | 1 | isFinal | u8 | 本块是否消耗了最后一个明文字节：`1` 是，`0` 否 |

解密：

```
plain_i = AES256GCM_decrypt(key = CK, nonce = block.nonce,
                            data = block.cipher‖block.tag,
                            aad  = itemId ‖ u32le(i) ‖ isFinal)
```

规则：

- 块序号必须从 0 连续递增；序号进 AAD，因此**调换两个块**会立刻失败，**从别的条目搬块进来**也会失败（itemId 不同）。
- `isFinal` 只有一个块为 1，且必须是最后一个块；截断会让最后一个已知块仍是"非末块"或缺失末块，从而失败。
- 明文恰好是 `chunkSize` 的整数倍时，**最后一个块是满块**，不额外补一个空块。
- 明文长度为 0 时，**没有任何块**；只有 128 字节头就是合法容器。
- 每块的明文长度由 `payloadLen − 28` 得出；累计必须恰好等于 `plaintextLength`。多一个字节或少一个字节都是损坏。
- 解完之后重算明文的 SHA-256，与索引里记录的 `contentSha256` 比较（见 §4）。

### 2.4 边界样例

| 明文长度 | 块数 | 容器总长度 | 说明 |
|---|---|---|---|
| 0 | 0 | 128 | 只有头 |
| 1 | 1 | 161 | 128 + 4 + 12 + 1 + 16 |
| 1048576（= chunkSize） | 1 | 1048736 | 满块，`isFinal = 1`，不补空块 |
| 1048577 | 2 | 1048769 | 第二块 1 字节 |

---

## 3. `keywrap.nskw` —— 用密码封装空间主密钥

固定 **100 字节**：40 字节被认证的头，加 60 字节的 AEAD 数据。

| Offset | Size | Field | Encoding | Meaning |
|---|---|---|---|---|
| 0 | 8 | magic | ASCII `NOVAKWRP` | 密钥封装标识 |
| 8 | 1 | version | u8 | v1 写 `1` |
| 9 | 1 | kdfId | u8 | `1` = Argon2id |
| 10 | 2 | flags | 2 B | 必须全 `0` |
| 12 | 12 | kdfParams | 12 B | 见 §3.1 |
| 24 | 16 | salt | 16 B | KDF 盐，随机 |
| 40 | 12 | nonce | 12 B | 本文件 GCM nonce |
| 52 | 32 | wrappedVmk | 32 B | 空间主密钥的密文 |
| 84 | 16 | tag | 16 B | GCM tag |

打开方式：

```
kek = Argon2id(password_utf8, salt, m = kdfParams.memoryKiB,
               t = kdfParams.iterations, p = 1, outLen = 32)
vmk = AES256GCM_decrypt(key = kek, nonce = wrap[40:52],
                        data = wrap[52:100],       # cipher‖tag
                        aad  = wrap[0:40])         # 整个头
```

- AAD 是整个 40 字节头：改动记录的 KDF 代价、盐或版本号，都会让封装打不开，而不是让下次解锁悄悄换一套派生参数。
- 密码字节 = **UTF-8 原文**，不做大小写折叠、不做 Unicode 归一化。同一串字符在别的语言里必须按同一编码喂进去。
- 密码错、头被改、密文被改：一律同一个答案"打不开"。

### 3.1 KDF 参数（12 字节）

| Offset | Size | Field | Encoding | Meaning |
|---|---|---|---|---|
| 0 | 4 | memoryKiB | u32 | Argon2id 的 m（单位 KiB） |
| 4 | 4 | iterations | u32 | Argon2id 的 t |
| 8 | 1 | parallelism | u8 | 必须为 `1` |
| 9 | 3 | reserved | 3 B | 必须全 `0` |

夹逼（读者必须在**派生之前**检查，避免被要求一次分配 1 GiB 或少到可猜的代价）：

| 参数 | 下限 | 上限 | 默认（本版本写出） |
|---|---|---|---|
| memoryKiB | 8192（8 MiB） | 1048576（1 GiB） | 65536（64 MiB） |
| iterations | 1 | 16 | 3 |
| parallelism | 1 | 1 | 1 |

- 越界即拒绝，不做截断。
- 代价低于默认值（旧文件或手工文件）**仍然允许打开**——拒绝等于把用户锁在自己的旧文件之外——但调用方应当告知"此文件的保护强度低于当前默认"。

---

## 4. `index.nseal` —— 加密的条目表

这是空间里"有什么"的唯一权威记录。整个表加密存放；磁盘上每个条目的文件只有 `<itemId>.novaseal`，所以**文件名、原始路径、大小在不解锁时不可读**。

```
+-- 25 B clear header --+---------------- 至文件末尾 ----------------+
| magic | ver | count | nonce |            cipher ‖ tag              |
+-----------------------+--------------------------------------------+
```

| Offset | Size | Field | Encoding | Meaning |
|---|---|---|---|---|
| 0 | 8 | magic | ASCII `NSEALIDX` | 索引标识 |
| 8 | 1 | version | u8 | v1 写 `1` |
| 9 | 4 | itemCount | u32 | 条目数量。同时进 AAD |
| 13 | 12 | nonce | 12 B | 本文件 GCM nonce |
| 25 | rest | body | cipher‖tag | 加密后的 JSON |

- 头总长 25 字节（8+1+4+12）。
- AAD = 第 8..12 字节，即 `version ‖ itemCount`（5 字节）。这两个值正是攻击者最想改的东西：把条目数改小，一张被删过条目的表就能冒充一张"本来就这么短"的表。
- 条目数在明文头里是**必要的**：AAD 需要它，而解密之前 JSON 不可读。它的可见性不泄漏额外信息 —— 密文长度本来就界定了条目数。

解密后的明文是 UTF-8 JSON：

```json
{
  "observedMaxUtc": 1790000000,
  "items": [
    {
      "id": "000102030405060708090a0b0c0d0e0f",
      "displayName": "合同.pdf",
      "originalName": "合同.pdf",
      "sourcePath": "C:\\Users\\me\\Documents\\合同.pdf",
      "size": 12345,
      "sealedAtUtc": 1790000000,
      "kind": "normal",
      "expiryUtc": null,
      "note": null,
      "contentSha256": "…64 个小写十六进制…",
      "folderEntryCount": null,
      "flags": 0
    }
  ]
}
```

字段：

| Field | Type | Meaning |
|---|---|---|
| observedMaxUtc | i64 | 本空间见过的最大封存时刻。**只升不降**，用于识别时钟回拨 |
| items | array | 条目列表，顺序即序列化顺序 |
| id | string | 16 字节的 32 位小写十六进制；也是容器文件名 |
| displayName | string | 列表里显示的名字，允许在封存时改名 |
| originalName | string | 封存前磁盘上的名字；取出时按它还原 |
| sourcePath | string | 来源路径，仅用于显示，绝不用于搬回 |
| size | u64 | 字节数；文件夹取打包前总大小 |
| sealedAtUtc | i64 | 封存时刻（epoch 秒） |
| kind | string | `normal` 或 `capsule` |
| expiryUtc | i64 或 null | 时间胶囊的生效时刻；`normal` 必须为 null |
| note | string 或 null | 备注 |
| contentSha256 | string | 明文 SHA-256，64 位小写十六进制 |
| folderEntryCount | i32 或 null | 非 null 表示这是文件夹（值是 ZIP 内文件+目录数） |
| flags | i32 | v1 必须为 0 |

读者必须执行的检查：条目数等于 `items` 长度；`id` 为 32 位十六进制且互不重复；`contentSha256` 为 64 位十六进制；`kind` 只能是两个字面值之一；`capsule` 必须有 `expiryUtc` 且大于 `sealedAtUtc`；`normal` 不得带 `expiryUtc`；`sealedAtUtc` 落在 §2.1 的时间窗内；`flags` 为 0。**出现未知字段即拒绝**（新版本加的字段必须伴随版本号提升，不能被静默丢弃）。

索引每次变更都整体重写（先写同名 `.tmp`、落盘、再替换），不做增量。

---

## 5. `.novabundle` v1 —— 一次导出的自包含包

```
+-- 64 B header --+-- body (bodyLength) --+-- tail 32 B --+
| magic | ver | kind | flags | bundleId | itemCount | indexLength | bodyLength |
| createdUtc | appVersion | reserved |
+---------------------------------------------------------+
body:  keywrap(100) ‖ indexBlock(indexLength) ‖ [backup len+bytes] ‖ item ×(len+bytes)
tail:  SHA-256(header ‖ body)
```

### 5.1 头（64 字节）

| Offset | Size | Field | Encoding | Meaning |
|---|---|---|---|---|
| 0 | 8 | magic | ASCII `NVSEALBK` | 包标识 |
| 8 | 1 | version | u8 | v1 写 `1` |
| 9 | 1 | kind | u8 | `1` = 仅条目；`2` = 先带一份整库备份块 |
| 10 | 2 | flags | 2 B | v1 全 `0` |
| 12 | 16 | bundleId | 16 B | 包标识，随机。参与两处 AAD |
| 28 | 4 | itemCount | u32 | 条目数 |
| 32 | 4 | indexLength | u32 | indexBlock 的长度（含 12 字节 nonce） |
| 36 | 8 | bodyLength | u64 | body 总长度 |
| 44 | 8 | createdUtc | i64 | 创建时刻，epoch 秒 |
| 52 | 4 | appVersion | u32 | 写入者的版本码（`major*1000 + minor`），仅供显示 |
| 56 | 8 | reserved | 8 B | 必须全 `0` |

### 5.2 body

1. **keywrap** —— 与 §3 完全相同的 100 字节结构，但 AAD 不同：`wrap[0:40] ‖ kind(1) ‖ bundleId(16)`（57 字节）。没有这一段，一个包里的密钥封装可以被搬到另一个包上仍然打得开，导入也就可能接受别人的索引。
2. **indexBlock** —— `nonce(12) ‖ cipher ‖ tag`，长度 = `indexLength`。AAD（52 字节）= `bundleId(16) ‖ SHA-256(keywrap 100 字节)(32) ‖ u32le(itemCount)`。也就是说索引被绑在"这个包、这份密钥封装、这个条目数"上，换一处立即失败。明文与 §4 的 JSON 同构。
3. **可选备份块**（仅 `kind = 2`）—— `u64le len ‖ bytes`。
4. **条目容器** —— 每个条目一段：`u64le len ‖ 完整 .novaseal 字节`，顺序与索引条目顺序一致。

> 注意两处索引密文的差别：独立 `index.nseal` 的 AAD 是 `version‖itemCount`；包内 indexBlock 的 AAD 是 `bundleId‖sha256(keywrap)‖itemCount`。同一个条目表在两种容器里密文不可互换，这是有意的。

### 5.3 尾

最后 32 字节 = `SHA-256(header ‖ body)`，不覆盖自身。文件总长必须恰好 `64 + bodyLength + 32`。这个哈希只用于**发现拷贝/存储过程中的损坏**，它不防伪：能改字节的人也能重算它。真正的防伪来自各处 GCM 认证。

---

## 6. 冻结向量

本节把"二十年后还能读"钉成可执行的事实：给定固定输入与固定 nonce 规则，容器的**每一个字节**都是确定的。

### 6.1 参数

| 名称 | 值 |
|---|---|
| itemId | `000102030405060708090a0b0c0d0e0f` |
| vmk | `a0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7b8b9babbbcbdbebf` |
| contentKey | `c0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedf` |
| keySlotNonce | `101112131415161718191a1b` |
| sealedAtUtc | 1790000000 |
| chunkSize | 1048576 |
| plaintextLength（主样本） | 3145728（3 MiB） |
| 明文生成 | `b[i] = (i * 31 + 7) mod 256` |
| 块 nonce 生成 | 12 字节，前 8 字节为 `u64le(块序号)`，后 4 字节为 0 |

主样本（3 MiB）：容器 SHA-256 `184f6996beb14f497acb15e2a93257655b1e85ab009e8a1f1413afb88b492abd`，明文 SHA-256 `bfe74807c87a64433433238baa29cb800d0e4b5f3b8c96037ab15b620e9633c5`，块数 3，容器总长 3145952。

### 6.2 空容器（0 字节明文，共 128 字节，hex 全量）

```
4e4f56415345414c01010000000102030405060708090a0b0c0d0e0f0000000000000000803bb16a0000000000001000101112131415161718191a1b5477b7cd1a82737552b0a6da55fe727b2383ce1c3b2a65158142d9f75b1aadf7db2019168becac6eb78d700ca9b438b40000000000000000000000000000000000000000
```

同一串按字段切开（偏移见 §2.1、§2.2）：

```
4e4f56415345414c                                  0..7    magic "NOVASEAL"
01010000                                          8..11   version 1, algo 1, flags 0, reserved 0
000102030405060708090a0b0c0d0e0f                  12..27  itemId
0000000000000000                                  28..35  plaintextLength = 0
00000000803bb16a                                  36..43  sealedAtUtc = 1790000000
00001000                                          44..47  chunkSize = 1048576
101112131415161718191a1b                          48..59  slotNonce
5477b7cd1a82737552b0a6da55fe727b
2383ce1c3b2a65158142d9f75b1aadf7                  60..91  wrappedKey
db2019168becac6eb78d700ca9b438b4                  92..107 slotTag
00000000                                          108..111 slotReserved
00000000000000000000000000000000                  112..127 reserved2
```

### 6.3 单字节明文（共 161 字节，hex 全量）

```
4e4f56415345414c01010000000102030405060708090a0b0c0d0e0f0100000000000000803bb16a0000000000001000101112131415161718191a1b5477b7cd1a82737552b0a6da55fe727b2383ce1c3b2a65158142d9f75b1aadf77ceb50b6fd003ab0806531906a4c763200000000000000000000000000000000000000001d00000000000000000000000000000098b683027f7946a15be60aee747a8a3976
```

头的两个字段与空容器不同（`plaintextLength = 1`，且 keySlot 因 AAD 变化而不同），随后是唯一个块：

```
1d000000                                          块长前缀 payloadLen = 29
000000000000000000000000                          块 nonce（序号 0）
98                                                1 字节密文
b683027f7946a15be60aee747a8a3976                  16 字节 tag
```

两串都可以在任意语言里由 §6.1 的参数重新生成 —— 若做不到，说明实现偏离了本规范。

### 6.4 边界向量（头字节）

| 名称 | 明文长度 | 块数 | 容器总长 |
|---|---|---|---|
| empty | 0 | 0 | 128 |
| one-byte | 1 | 1 | 161 |
| exact-chunk | 1048576 | 1 | 1048736 |
| chunk-plus-one | 1048577 | 2 | 1048769 |

四个边界各自的 128 字节头在仓库的向量清单里以 base64 给出；用本节参数与 §2.3 规则可逐字节重建，包括"零块"这种最小容器。

---

## 7. 参考实现

`docs/seal_reference_reader.py` 是本文的可执行证据：单文件、只读、不使用 Novara 的任何代码。

- 依赖：Python 3.9+，标准库 `json/os/struct/sys/hashlib/base64`，AES-256-GCM 用 `cryptography`（`pip install cryptography`）。**不依赖** Novara、不依赖 .NET。
- 能力：解开 `.novaseal`（需要 VMK）、读 `index.nseal`（需要 VMK）、拆 `.novabundle`（验尾哈希，读索引需 VMK）、对照向量自检。
- 不做：写文件、派生密码密钥。要用密码打开 `keywrap.nskw`，需要 Argon2id（例如 `pip install argon2-cffi`）；本参考实现只验证记录下来的参数，不实现 Argon2id。

用法：

```
# 跨语言自检：用向量清单里的 VMK 打开冻结容器，重算两个 SHA-256
python docs/seal_reference_reader.py --vector Novara.Tests/Fixtures/seal-container-v1.json \
                                     --container Novara.Tests/Fixtures/seal-container-v1.bin

# 打开任意容器（VMK 用 hex 或 base64）
python docs/seal_reference_reader.py --container secret.novaseal --vmk a0a1…bebf --out plain.bin

# 读索引
python docs/seal_reference_reader.py --index index.nseal --vmk a0a1…bebf

# 拆包（先验尾哈希）
python docs/seal_reference_reader.py --bundle Novara_Vault_20261030.novabundle --vmk a0a1…bebf
```

自检模式会打印 `container sha256`、`plaintext sha256`，并与清单记录逐字比对；不一致即以非零状态退出。

---

## 8. 读者规则（必须照做）

1. **先验结构，再解密**：任何长度、偏移、保留位、参数边界都在派生密钥与分配缓冲之前检查。
2. **版本高 ≠ 损坏**：`version` 高于本读者支持的范围时，提示"需要更新的程序"，不要提示"文件损坏"。
3. **认证失败不细分原因**：密码错、文件被改、参数被改，对外是同一个答案。
4. **不猜测、不修复、不跳过**：缺块、多块、块序错、末块标记错，全都拒绝。
5. **不信明文长度以外的元数据**：条目名只从解密后的索引里读，磁盘文件名不是元数据。
6. **每次取出后重算 SHA-256** 与索引里的 `contentSha256` 比对；不一致即中止。

---

## 9. 本格式不隐藏什么（如实说明）

- **条目数量**可从 `index.nseal` 的明文头与密文长度推断。
- **每个条目的大致大小**可从容器长度与块数推断（密文与明文等长）。
- **封存时刻**在容器头里是明文的（这是特性：二十年后的人需要知道它何时被封存）。
- 文件系统本身会暴露容器文件的创建/修改时间、总占用空间。
- **时间胶囊的期限不受本格式强制**。容器里没有任何"到期之前读不出来"的机制：期限只是索引里的一个时刻，由 Novara 自己遵守。任何拿到文件与密码的人，按本文即可读取 —— 这与产品在导出时对用户说的话是同一句实话。

除此之外，文件名、原始路径、备注、内容本身，在不知道 VMK 的情况下均不可读。