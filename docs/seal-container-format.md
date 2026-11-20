# Novara Sealed Container Format Specification v1

**Sealed item container, key wrap, index and export bundle — public format reference**

This document is a **publicly publishable format description**: it describes bytes only, not Novara's internal implementation. Anyone who understands this document, in any language and with any tooling, should be able to decode a `.novaseal` file back to its original plaintext, open a `keywrap.nskw` with a password, read an `index.nseal` as a table, and split a `.novabundle` into its individual containers — with no Novara program and with no assumptions beyond this document.

> The intended reader is someone twenty years from now. Therefore this text uses no internal product code names and relies on no library's private behavior; every "why" is written as a verifiable rule.

Format version: **v1** throughout (the first published version). All offsets in this document are counted from the first byte of the file or structure.

---

## 1. General conventions

| Item | Convention |
|---|---|
| Byte order | All fixed-length integers are **little-endian** (unsigned written `u32`/`u64`, signed written `i64`) |
| Key length | 32 bytes (AES-256) |
| GCM nonce | 12 bytes, must be unique for every encryption under each key |
| GCM tag | 16 bytes, appended after the ciphertext (`cipher‖tag`) |
| Encryption algorithm | AES-256-GCM, the only one defined in v1 |
| Compression | None. Ciphertext is the same length as plaintext, plus only the nonce and the tag |
| Padding | None. All structures are tightly packed |
| Text encoding | UTF-8, no BOM |
| SHA-256 | Standard FIPS 180-4, 32-byte output; written in lowercase hexadecimal throughout this document |
| HEX notation | Every `hex` in this document is lowercase and separator-free; `base64` uses the standard alphabet with `=` padding |

**Authentication (AAD) rule**: every AEAD call in this format carries associated data; there is no "bare encryption" anywhere. The purpose of the AAD is to bind structural positions into the ciphertext: encrypting the same plaintext under the same key but at a different position, with a different block index, or against a different header will fail to decrypt. A reader must construct the AAD exactly as written here — no omission, no zero padding; a length difference of one byte counts as an authentication failure.

**Failure is always failure**: no failed check may be "repaired", "guessed", or "skipped". See §8.

---

## 2. `.novaseal` — a single sealed file (or a folder packed as a ZIP)

A container = **a fixed 128-byte header** + **zero or more blocks**. There is no trailer and no optional extension area: the file ends as soon as the last block has been read.

```
+--------------------------- 128 B header ---------------------------+
| magic | ver | algo | flags | itemId | length | sealedAt | chunk | keySlot | rsv |
+--------------------------------------------------------------------+
| [u32 payloadLen][nonce 12][cipher][tag 16]   ← block 0              |
| [u32 payloadLen][nonce 12][cipher][tag 16]   ← block 1              |
| ...                                                                 |
+---------------------------------------------------------------------+
```

### 2.1 Header (128 bytes)

| Offset | Size | Field | Encoding | Meaning |
|---|---|---|---|---|
| 0 | 8 | magic | ASCII `NOVASEAL` | Container identifier. Must match byte for byte |
| 8 | 1 | version | u8 | Format version. v1 writes `1` |
| 9 | 1 | algoId | u8 | Content algorithm. v1 defines only `1` = AES-256-GCM |
| 10 | 1 | flags | u8 | bit0 = 1 means the plaintext is a ZIP (a folder); the remaining bits are undefined in v1 |
| 11 | 1 | reserved | u8 | Must be `0` |
| 12 | 16 | itemId | 16 B | Item identifier. Also the plaintext-side file name and the first segment of every block's AAD |
| 28 | 8 | plaintextLength | u64 | Total plaintext length in bytes. `0` is legal |
| 36 | 8 | sealedAtUtc | i64 | Seal time, Unix epoch seconds (UTC); never rewritten after sealing |
| 44 | 4 | chunkSize | u32 | Upper bound of plaintext per block. Must be a power of two and within [4096, 4194304] |
| 48 | 64 | keySlot | 64 B | Wrapping of the content key, see §2.2 |
| 112 | 16 | reserved2 | 16 B | Must be all `0` |

Fixed-length summary:

| Constant | Value | Description |
|---|---|---|
| headerSize | 128 | Total header length |
| authenticatedPrefix | 48 | Bytes 0..47 of the header; the AAD of keySlot is exactly these 48 bytes |

Reader rules (order does not matter; all of them must hold):

- `magic` not equal → not this format.
- `version == 0` or `version > 1` → **unsupported version**. This is not corruption: the file is almost certainly intact, the reader is simply too old. The user should be told "a newer version is required", not "the data is corrupted".
- `algoId != 1` → unsupported algorithm, reject.
- Any bit of `flags` above bit0 set → reject. `reserved` or `reserved2` non-zero → reject.
- `chunkSize` not a power of two or out of range → reject (so that a reader is never asked to allocate an arbitrarily large buffer).
- `sealedAtUtc` must fall within `946684799 … 253402300799` (that is, 1999-12-31T23:59:59Z through 9999-12-31T23:59:59Z). Anything outside counts as corruption.
- `plaintextLength` must be ≤ 2^63−1.

### 2.2 Key slot (64 bytes inside the header, at offset 48)

| Offset | Size | Field | Encoding | Meaning |
|---|---|---|---|---|
| 0 | 12 | slotNonce | 12 B | GCM nonce used for this wrapping |
| 12 | 32 | wrappedKey | 32 B | Ciphertext of the content key |
| 44 | 16 | slotTag | 16 B | GCM tag |
| 60 | 4 | slotReserved | 4 B | Must be all `0`; **not covered by the tag**, so it can only be checked structurally |

How to open it (pseudocode):

```
prefix = container[0:48]                       # the authenticated header prefix
ck     = AES256GCM_decrypt(key   = VMK,
                           nonce = slot[0:12],
                           data  = slot[12:60],   # cipher‖tag
                           aad   = prefix)
```

- `VMK` is the space master key as wrapped by the password (see §3), 32 bytes.
- Because the AAD is the header prefix, **changing any authenticated byte of the header** leaves the slot unable to open — tampering shows up as "will not open", never as "opened a different but legitimate header".
- The four reserved bytes lie outside the tag, so a reader must check separately that they are 0; otherwise a slot with them changed to non-zero would still open and the check would be decoration.
- There are only two outcomes: it opens (yielding the 32-byte CK) or it does not. When it does not, do not break the cause down for the user (a wrong password, an altered header, and an altered slot are indistinguishable).

### 2.3 Blocks

Each block follows the previous one immediately, with a fixed layout:

| Offset | Size | Field | Encoding | Meaning |
|---|---|---|---|---|
| 0 | 4 | payloadLen | u32 | Number of bytes **from the start of the nonce to the end of the tag**, i.e. `12 + plaintext block length + 16` |
| 4 | 12 | nonce | 12 B | GCM nonce of this block |
| 16 | payloadLen−28 | cipher | Same length as the plaintext | Ciphertext of this block |
| 16+(payloadLen−28) | 16 | tag | 16 B | GCM tag of this block |

Block AAD (37 bytes):

| Offset | Size | Field | Encoding | Meaning |
|---|---|---|---|---|
| 0 | 16 | itemId | 16 B | Same as the itemId in the header |
| 16 | 4 | blockIndex | u32 | Block index starting at `0` |
| 20 | 1 | isFinal | u8 | Whether this block consumed the last plaintext byte: `1` yes, `0` no |

Decryption:

```
plain_i = AES256GCM_decrypt(key = CK, nonce = block.nonce,
                            data = block.cipher‖block.tag,
                            aad  = itemId ‖ u32le(i) ‖ isFinal)
```

Rules:

- Block indices must increase contiguously from 0; the index goes into the AAD, so **swapping two blocks** fails immediately, and **moving a block in from another item** fails as well (different itemId).
- Exactly one block has `isFinal` = 1, and it must be the last block; truncation leaves the last known block still "not final", or removes the final block, and therefore fails.
- When the plaintext is exactly a multiple of `chunkSize`, **the last block is a full block**; no additional empty block is appended.
- When the plaintext length is 0, **there are no blocks at all**; the 128-byte header alone is a legal container.
- The plaintext length of each block is `payloadLen − 28`; the running total must equal `plaintextLength` exactly. One byte more or one byte less is corruption.
- After decoding, recompute the SHA-256 of the plaintext and compare it with the `contentSha256` recorded in the index (see §4).

### 2.4 Boundary examples

| Plaintext length | Blocks | Total container length | Notes |
|---|---|---|---|
| 0 | 0 | 128 | Header only |
| 1 | 1 | 161 | 128 + 4 + 12 + 1 + 16 |
| 1048576 (= chunkSize) | 1 | 1048736 | Full block, `isFinal = 1`, no empty block appended |
| 1048577 | 2 | 1048769 | Second block holds 1 byte |

---

## 3. `keywrap.nskw` — wrapping the space master key with a password

A fixed **100 bytes**: a 40-byte authenticated header plus 60 bytes of AEAD data.

| Offset | Size | Field | Encoding | Meaning |
|---|---|---|---|---|
| 0 | 8 | magic | ASCII `NOVAKWRP` | Key-wrap identifier |
| 8 | 1 | version | u8 | v1 writes `1` |
| 9 | 1 | kdfId | u8 | `1` = Argon2id |
| 10 | 2 | flags | 2 B | Must be all `0` |
| 12 | 12 | kdfParams | 12 B | See §3.1 |
| 24 | 16 | salt | 16 B | KDF salt, random |
| 40 | 12 | nonce | 12 B | GCM nonce of this file |
| 52 | 32 | wrappedVmk | 32 B | Ciphertext of the space master key |
| 84 | 16 | tag | 16 B | GCM tag |

How to open it:

```
kek = Argon2id(password_utf8, salt, m = kdfParams.memoryKiB,
               t = kdfParams.iterations, p = 1, outLen = 32)
vmk = AES256GCM_decrypt(key = kek, nonce = wrap[40:52],
                        data = wrap[52:100],       # cipher‖tag
                        aad  = wrap[0:40])         # the entire header
```

- The AAD is the entire 40-byte header: changing the recorded KDF cost, the salt, or the version number leaves the wrapping unable to open, rather than silently switching to a different derivation parameter set on the next unlock.
- Password bytes = the **raw UTF-8** text, with no case folding and no Unicode normalization. The same string of characters must be fed in under the same encoding in any other language.
- Wrong password, altered header, altered ciphertext: always the same answer, "will not open".

### 3.1 KDF parameters (12 bytes)

| Offset | Size | Field | Encoding | Meaning |
|---|---|---|---|---|
| 0 | 4 | memoryKiB | u32 | Argon2id's m (in KiB) |
| 4 | 4 | iterations | u32 | Argon2id's t |
| 8 | 1 | parallelism | u8 | Must be `1` |
| 9 | 3 | reserved | 3 B | Must be all `0` |

Clamping (a reader must check **before deriving**, so that it is never asked to allocate 1 GiB at once or to accept a cost low enough to be guessed):

| Parameter | Lower bound | Upper bound | Default (written by this version) |
|---|---|---|---|
| memoryKiB | 8192 (8 MiB) | 1048576 (1 GiB) | 65536 (64 MiB) |
| iterations | 1 | 16 | 3 |
| parallelism | 1 | 1 | 1 |

- Out of range means reject; no truncation is performed.
- A cost below the default (old files or hand-made files) **is still allowed to open** — rejecting would lock users out of their own old files — but the caller should report "this file's protection strength is below the current default".

---

## 4. `index.nseal` — the encrypted item table

This is the single authoritative record of "what exists" in a space. The entire table is stored encrypted; on disk, each item's file is only `<itemId>.novaseal`, so **file names, original paths, and sizes are unreadable while locked**.

```
+-- 25 B clear header --+-------------- to end of file --------------+
| magic | ver | count | nonce |            cipher ‖ tag              |
+-----------------------+--------------------------------------------+
```

| Offset | Size | Field | Encoding | Meaning |
|---|---|---|---|---|
| 0 | 8 | magic | ASCII `NSEALIDX` | Index identifier |
| 8 | 1 | version | u8 | v1 writes `1` |
| 9 | 4 | itemCount | u32 | Number of items. Also enters the AAD |
| 13 | 12 | nonce | 12 B | GCM nonce of this file |
| 25 | rest | body | cipher‖tag | The encrypted JSON |

- The header is 25 bytes total (8+1+4+12).
- AAD = bytes 8..12, i.e. `version ‖ itemCount` (5 bytes). These two values are exactly what an attacker most wants to change: shrink the item count and a table whose items were removed can masquerade as a table that was "always that short".
- The item count is **necessary** in the plaintext header: the AAD needs it, and the JSON is unreadable before decryption. Its visibility leaks nothing extra — the ciphertext length already bounds the number of items.

The decrypted plaintext is UTF-8 JSON:

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
      "contentSha256": "…64 lowercase hex digits…",
      "folderEntryCount": null,
      "flags": 0
    }
  ]
}
```

Fields:

| Field | Type | Meaning |
|---|---|---|
| observedMaxUtc | i64 | Largest seal time ever seen in this space. **Rises, never falls**; used to detect clock rollback |
| items | array | Item list; its order is the serialization order |
| id | string | 32 lowercase hex digits representing 16 bytes; also the container file name |
| displayName | string | Name shown in the list; may be changed at seal time |
| originalName | string | The name on disk before sealing; restored from it on extraction |
| sourcePath | string | Source path, for display only, never used to move anything back |
| size | u64 | Size in bytes; for a folder, the total size before packing |
| sealedAtUtc | i64 | Seal time (epoch seconds) |
| kind | string | `normal` or `capsule` |
| expiryUtc | i64 or null | Effective time of a time capsule; must be null for `normal` |
| note | string or null | Note |
| contentSha256 | string | SHA-256 of the plaintext, 64 lowercase hex digits |
| folderEntryCount | i32 or null | Non-null means this is a folder (the value is the count of files plus directories inside the ZIP) |
| flags | i32 | Must be 0 in v1 |

Checks a reader must perform: the item count equals the length of `items`; every `id` is 32 hex digits and no two repeat; `contentSha256` is 64 hex digits; `kind` is exactly one of the two literals; `capsule` must carry an `expiryUtc` greater than its `sealedAtUtc`; `normal` must not carry an `expiryUtc`; `sealedAtUtc` falls inside the time window of §2.1; `flags` is 0. **Any unknown field is a rejection** (a field added by a later version must come with a version bump and must never be silently dropped).

The index is rewritten in full on every change (write a sibling `.tmp`, flush to disk, then replace); there is no incremental update.

---

## 5. `.novabundle` v1 — a self-contained package for a single export

```
+-- 64 B header --+-- body (bodyLength) --+-- tail 32 B --+
| magic | ver | kind | flags | bundleId | itemCount | indexLength | bodyLength |
| createdUtc | appVersion | reserved |
+---------------------------------------------------------+
body:  keywrap(100) ‖ indexBlock(indexLength) ‖ [backup len+bytes] ‖ item ×(len+bytes)
tail:  SHA-256(header ‖ body)
```

### 5.1 Header (64 bytes)

| Offset | Size | Field | Encoding | Meaning |
|---|---|---|---|---|
| 0 | 8 | magic | ASCII `NVSEALBK` | Bundle identifier |
| 8 | 1 | version | u8 | v1 writes `1` |
| 9 | 1 | kind | u8 | `1` = items only; `2` = carries a full-vault backup block first |
| 10 | 2 | flags | 2 B | All `0` in v1 |
| 12 | 16 | bundleId | 16 B | Bundle identifier, random. Enters the AAD in two places |
| 28 | 4 | itemCount | u32 | Number of items |
| 32 | 4 | indexLength | u32 | Length of indexBlock (including the 12-byte nonce) |
| 36 | 8 | bodyLength | u64 | Total length of body |
| 44 | 8 | createdUtc | i64 | Creation time, epoch seconds |
| 52 | 4 | appVersion | u32 | Version code of the writer (`major*1000 + minor`), for display only |
| 56 | 8 | reserved | 8 B | Must be all `0` |

### 5.2 body

1. **keywrap** — the very same 100-byte structure as §3, but with a different AAD: `wrap[0:40] ‖ kind(1) ‖ bundleId(16)` (57 bytes). Without this segment, the key wrap from one bundle could be moved onto another bundle and still open, and an import could therefore accept somebody else's index.
2. **indexBlock** — `nonce(12) ‖ cipher ‖ tag`, length = `indexLength`. AAD (52 bytes) = `bundleId(16) ‖ SHA-256(keywrap 100 bytes)(32) ‖ u32le(itemCount)`. In other words, the index is bound to "this bundle, this key wrap, this item count"; change any one of them and it fails immediately. The plaintext is isomorphic to the JSON of §4.
3. **Optional backup block** (`kind = 2` only) — `u64le len ‖ bytes`.
4. **Item containers** — one segment per item: `u64le len ‖ complete .novaseal bytes`, in the same order as the index items.

> Note the difference between the two index ciphertexts: the standalone `index.nseal` has AAD `version‖itemCount`; the indexBlock inside a bundle has AAD `bundleId‖sha256(keywrap)‖itemCount`. The same item table is not interchangeable between the two containers, and this is intentional.

### 5.3 Tail

The last 32 bytes = `SHA-256(header ‖ body)`, which does not cover itself. The total file length must be exactly `64 + bodyLength + 32`. This hash only serves to **detect corruption during copying or storage**; it is not a forgery defense — whoever can change bytes can also recompute it. Real authenticity comes from the GCM authentication everywhere.

---

## 6. Frozen vectors

This section pins "still readable twenty years from now" down to an executable fact: given fixed inputs and a fixed nonce rule, **every byte** of the container is determined.

### 6.1 Parameters

| Name | Value |
|---|---|
| itemId | `000102030405060708090a0b0c0d0e0f` |
| vmk | `a0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7b8b9babbbcbdbebf` |
| contentKey | `c0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedf` |
| keySlotNonce | `101112131415161718191a1b` |
| sealedAtUtc | 1790000000 |
| chunkSize | 1048576 |
| plaintextLength (primary sample) | 3145728 (3 MiB) |
| Plaintext generation | `b[i] = (i * 31 + 7) mod 256` |
| Block nonce generation | 12 bytes: the first 8 bytes are `u64le(block index)`, the last 4 bytes are 0 |

Primary sample (3 MiB): container SHA-256 `184f6996beb14f497acb15e2a93257655b1e85ab009e8a1f1413afb88b492abd`, plaintext SHA-256 `bfe74807c87a64433433238baa29cb800d0e4b5f3b8c96037ab15b620e9633c5`, 3 blocks, total container length 3145952.

### 6.2 Empty container (0-byte plaintext, 128 bytes in total, full hex)

```
4e4f56415345414c01010000000102030405060708090a0b0c0d0e0f0000000000000000803bb16a0000000000001000101112131415161718191a1b5477b7cd1a82737552b0a6da55fe727b2383ce1c3b2a65158142d9f75b1aadf7db2019168becac6eb78d700ca9b438b40000000000000000000000000000000000000000
```

The same string split by field (offsets in §2.1 and §2.2):

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

### 6.3 One-byte plaintext (161 bytes in total, full hex)

```
4e4f56415345414c01010000000102030405060708090a0b0c0d0e0f0100000000000000803bb16a0000000000001000101112131415161718191a1b5477b7cd1a82737552b0a6da55fe727b2383ce1c3b2a65158142d9f75b1aadf77ceb50b6fd003ab0806531906a4c763200000000000000000000000000000000000000001d00000000000000000000000000000098b683027f7946a15be60aee747a8a3976
```

Two header fields differ from the empty container (`plaintextLength = 1`, and keySlot differs because the AAD changed), followed by the single block:

```
1d000000                                          block length prefix payloadLen = 29
000000000000000000000000                          block nonce (index 0)
98                                                1-byte ciphertext
b683027f7946a15be60aee747a8a3976                  16-byte tag
```

Both strings can be regenerated in any language from the parameters in §6.1 — if that is not possible, the implementation has diverged from this specification.

### 6.4 Boundary vectors (header bytes)

| Name | Plaintext length | Blocks | Total container length |
|---|---|---|---|
| empty | 0 | 0 | 128 |
| one-byte | 1 | 1 | 161 |
| exact-chunk | 1048576 | 1 | 1048736 |
| chunk-plus-one | 1048577 | 2 | 1048769 |

The 128-byte header of each of the four boundaries is given in base64 in the repository's vector manifest; with the parameters of this section and the rules of §2.3 it can be rebuilt byte for byte, including the smallest container, the zero-block one.

---

## 7. Reference implementation

`docs/seal_reference_reader.py` is the executable evidence for this document: a single file, read-only, using none of Novara's code.

- Dependencies: Python 3.9+, the standard library `json/os/struct/sys/hashlib/base64`, and `cryptography` for AES-256-GCM (`pip install cryptography`). It **does not depend** on Novara, nor on .NET.
- Capabilities: open `.novaseal` (requires the VMK), read `index.nseal` (requires the VMK), split `.novabundle` (verifies the tail hash; reading the index requires the VMK), and self-check against the vectors.
- Not done: writing files, deriving password keys. Opening `keywrap.nskw` with a password requires Argon2id (for example `pip install argon2-cffi`); this reference implementation only validates the recorded parameters and does not implement Argon2id.

Usage:

```
# Cross-language self-check: open the frozen container with the manifest VMK, recompute both SHA-256 values
python docs/seal_reference_reader.py --vector Novara.Tests/Fixtures/seal-container-v1.json \
                                     --container Novara.Tests/Fixtures/seal-container-v1.bin

# Open an arbitrary container (VMK given as hex or base64)
python docs/seal_reference_reader.py --container secret.novaseal --vmk a0a1…bebf --out plain.bin

# Read the index
python docs/seal_reference_reader.py --index index.nseal --vmk a0a1…bebf

# Split a bundle (verifying the tail hash first)
python docs/seal_reference_reader.py --bundle Novara_Vault_20261030.novabundle --vmk a0a1…bebf
```

Self-check mode prints `container sha256` and `plaintext sha256` and compares them verbatim with the manifest records; any mismatch exits with a non-zero status.

---

## 8. Reader rules (mandatory)

1. **Validate the structure first, then decrypt**: every length, offset, reserved bit, and parameter bound is checked before keys are derived and buffers are allocated.
2. **A higher version ≠ corruption**: when `version` is above what this reader supports, say "a newer program is required", not "the file is corrupted".
3. **Do not break authentication failures down by cause**: a wrong password, an altered file, and altered parameters are one and the same answer externally.
4. **Do not guess, do not repair, do not skip**: a missing block, an extra block, a wrong block order, a wrong final-block marker — all are rejected.
5. **Trust no metadata beyond the plaintext length**: item names are read only from the decrypted index; an on-disk file name is not metadata.
6. **Recompute the SHA-256 after every extraction** and compare it with `contentSha256` in the index; abort on any mismatch.

---

## 9. What this format does not hide (stated honestly)

- The **number of items** can be inferred from the plaintext header of `index.nseal` and from the ciphertext length.
- The **approximate size of each item** can be inferred from the container length and the block count (ciphertext and plaintext are of equal length).
- The **seal time** is plaintext in the container header (this is a feature: someone twenty years from now needs to know when it was sealed).
- The file system itself exposes the creation/modification times of the container files and the total space they occupy.
- **A time capsule's deadline is not enforced by this format**. There is no mechanism in the container that makes it unreadable before expiry: the deadline is merely a moment in the index, honored by Novara itself. Anyone holding the file and the password can read it by following this document — which is the same honest statement the product makes to users at export time.

Beyond that, file names, original paths, notes, and the content itself are all unreadable without the VMK.
