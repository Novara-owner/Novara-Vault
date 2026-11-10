
"""seal_reference_reader.py - an independent reader for the Novara sealed container format v1.

Why this file exists
--------------------
The container format is documented in "docs/seal-container-format.md" so that somebody who has
never seen Novara can still open the files. A document alone cannot prove that; this reader is the
executable proof. It shares no code with the product: standard library plus one AES-256-GCM
implementation (`cryptography`), no .NET, no project imports.

    pip install cryptography

Modes
-----
    --vector MANIFEST --container BIN   decrypted the frozen vector and re-checks both SHA-256 values
    --container FILE --vmk KEY          opens any container (KEY = 64 hex chars or base64)
    --index FILE --vmk KEY              decrypts index.nseal and prints the entry table
    --bundle FILE --vmk KEY             verifies a .novabundle tail hash and its encrypted index

It only reads: no container, index or bundle is ever modified. `--out` writes a freshly decrypted
plaintext to a path you choose, which is the one and only write performed.

Not implemented on purpose: Argon2id. Opening `keywrap.nskw` with a password needs a password hash
function that is not in the Python standard library (use `argon2-cffi`); every mode here therefore
takes the 32-byte master key directly, which is exactly what the frozen vectors publish.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import struct
import sys

try:
    from cryptography.hazmat.primitives.ciphers.aead import AESGCM
except ImportError:
    sys.stderr.write(
        "this reader needs AES-256-GCM: pip install cryptography\n")
    raise SystemExit(2)






CONTAINER_MAGIC = b"NOVASEAL"
CONTAINER_HEADER_SIZE = 128
CONTAINER_AUTH_PREFIX = 48
KEY_SLOT_OFFSET = 48
KEY_SLOT_SIZE = 64
NONCE_SIZE = 12
TAG_SIZE = 16
KEY_SIZE = 32
BLOCK_PREFIX_SIZE = 4
MIN_CHUNK = 4096
MAX_CHUNK = 4 * 1024 * 1024
MIN_SEALED_AT = 946_684_799
MAX_SEALED_AT = 253_402_300_799

INDEX_MAGIC = b"NSEALIDX"
INDEX_HEADER_SIZE = 25

BUNDLE_MAGIC = b"NVSEALBK"
BUNDLE_HEADER_SIZE = 64
BUNDLE_TAIL_SIZE = 32
KEYWRAP_SIZE = 100


class FormatError(Exception):
    """Raised for every structural problem; the message says which rule failed."""



def read_key(text: str) -> bytes:
    """A 32-byte key, given as 64 hex characters or as base64."""
    raw = text.strip()
    candidate: bytes
    if len(raw) == 64 and all(c in "0123456789abcdefABCDEF" for c in raw):
        candidate = bytes.fromhex(raw)
    else:
        try:
            candidate = base64.b64decode(raw, validate=True)
        except Exception as exc:
            raise FormatError("vmk must be 64 hex characters or base64") from exc
    if len(candidate) != KEY_SIZE:
        raise FormatError(f"vmk must be {KEY_SIZE} bytes, got {len(candidate)}")
    return candidate


def aes_gcm_decrypt(key: bytes, nonce: bytes, data: bytes, aad: bytes) -> bytes:
    return AESGCM(key).decrypt(nonce, data, aad)


def aes_gcm_encrypt(key: bytes, nonce: bytes, data: bytes, aad: bytes) -> bytes:
    return AESGCM(key).encrypt(nonce, data, aad)


def hexdump(data: bytes) -> str:
    return data.hex()



class Container:
    """The 128-byte header of a .novaseal container."""

    def __init__(self, raw: bytes):
        if len(raw) < CONTAINER_HEADER_SIZE:
            raise FormatError(f"container is shorter than {CONTAINER_HEADER_SIZE} bytes")
        header = raw[:CONTAINER_HEADER_SIZE]

        if header[0:8] != CONTAINER_MAGIC:
            raise FormatError("not a sealed container: magic is not NOVASEAL")

        self.version = header[8]
        if self.version == 0 or self.version > 1:
            raise FormatError(f"container version {self.version} is not supported by this reader")

        self.algo = header[9]
        if self.algo != 1:
            raise FormatError(f"content algorithm {self.algo} is not AES-256-GCM")

        self.flags = header[10]
        if self.flags & ~0x01:
            raise FormatError("flag bits above bit 0 are not defined in v1")

        if header[11] != 0:
            raise FormatError("reserved byte 11 must be zero")

        self.item_id = header[12:28]
        (self.plaintext_length,) = struct.unpack_from("<Q", header, 28)
        (self.sealed_at,) = struct.unpack_from("<q", header, 36)
        (self.chunk_size,) = struct.unpack_from("<I", header, 44)
        self.key_slot = header[KEY_SLOT_OFFSET:KEY_SLOT_OFFSET + KEY_SLOT_SIZE]

        if self.plaintext_length > 2**63 - 1:
            raise FormatError("plaintext length does not fit a signed 64-bit value")
        if not (MIN_SEALED_AT <= self.sealed_at <= MAX_SEALED_AT):
            raise FormatError("seal moment is outside the representable window")
        if not (MIN_CHUNK <= self.chunk_size <= MAX_CHUNK) or (self.chunk_size & (self.chunk_size - 1)):
            raise FormatError("chunk size must be a power of two in [4096, 4194304]")
        if any(b != 0 for b in header[112:128]):
            raise FormatError("reserved bytes 112..127 must be zero")

        self.header = header
        self.raw = raw

    @property
    def prefix(self) -> bytes:
        return self.header[:CONTAINER_AUTH_PREFIX]

    def content_key(self, vmk: bytes) -> bytes:
        """Opens the key slot. Everything that fails here fails the same way, by design."""
        if any(b != 0 for b in self.key_slot[60:64]):
            raise FormatError("key slot reserved bytes must be zero")
        try:
            return aes_gcm_decrypt(vmk, self.key_slot[0:NONCE_SIZE],
                                   self.key_slot[NONCE_SIZE:60], self.prefix)
        except Exception as exc:
            raise FormatError(
                "key slot does not open: wrong master key, or the header/slot was altered") from exc

    def decrypt(self, vmk: bytes) -> bytes:
        ck = self.content_key(vmk)
        return self.decrypt_with_key(ck)

    def decrypt_with_key(self, content_key: bytes) -> bytes:
        if len(content_key) != KEY_SIZE:
            raise FormatError("content key must be 32 bytes")

        out = bytearray()
        pos = CONTAINER_HEADER_SIZE
        index = 0
        remaining = self.plaintext_length

        while remaining > 0:
            if pos + BLOCK_PREFIX_SIZE > len(self.raw):
                raise FormatError(f"block {index} is missing: the container ends early")
            (payload_len,) = struct.unpack_from("<I", self.raw, pos)


            if payload_len < NONCE_SIZE + 1 + TAG_SIZE:
                raise FormatError(f"block {index} declares {payload_len} bytes, below the minimum for a non-empty block")
            if payload_len > NONCE_SIZE + self.chunk_size + TAG_SIZE:
                raise FormatError(f"block {index} declares {payload_len} bytes, beyond the chunk-size ceiling")

            block_end = pos + BLOCK_PREFIX_SIZE + payload_len
            if block_end > len(self.raw):
                raise FormatError(f"block {index} runs past the end of the file")

            nonce = self.raw[pos + BLOCK_PREFIX_SIZE:pos + BLOCK_PREFIX_SIZE + NONCE_SIZE]
            payload = self.raw[pos + BLOCK_PREFIX_SIZE + NONCE_SIZE:block_end]

            plain_len = payload_len - NONCE_SIZE - TAG_SIZE
            if plain_len > remaining:
                raise FormatError(f"block {index} holds more bytes than the header declares")

            is_final = plain_len == remaining
            aad = self.item_id + struct.pack("<I", index) + (b"\x01" if is_final else b"\x00")

            try:
                plain = aes_gcm_decrypt(content_key, nonce, payload, aad)
            except Exception as exc:
                raise FormatError(
                    f"block {index} failed authentication (wrong key, reordered, truncated or edited)") from exc

            if len(plain) != plain_len:
                raise FormatError(f"block {index} decrypted to {len(plain)} bytes, expected {plain_len}")

            out += plain
            remaining -= plain_len
            pos = block_end
            index += 1

        if pos != len(self.raw):
            raise FormatError("trailing bytes after the last block")

        return bytes(out)


def parse_container(raw: bytes) -> Container:
    return Container(raw)



def parse_index(raw: bytes, vmk: bytes) -> dict:
    if len(raw) < INDEX_HEADER_SIZE + TAG_SIZE:
        raise FormatError("index file is truncated")
    if raw[0:8] != INDEX_MAGIC:
        raise FormatError("not an index file: magic is not NSEALIDX")
    version = raw[8]
    if version == 0 or version > 1:
        raise FormatError(f"index version {version} is not supported")

    (item_count,) = struct.unpack_from("<I", raw, 9)
    nonce = raw[13:25]
    aad = raw[8:13]

    try:
        plain = aes_gcm_decrypt(vmk, nonce, raw[INDEX_HEADER_SIZE:], aad)
    except Exception as exc:
        raise FormatError("index failed authentication: wrong master key or altered file") from exc

    try:
        payload = json.loads(plain.decode("utf-8"))
    except Exception as exc:
        raise FormatError("index plaintext is not valid UTF-8 JSON") from exc

    items = payload.get("items")
    if items is None:
        raise FormatError("index payload has no items array")
    if len(items) != item_count:
        raise FormatError(f"header declares {item_count} entries, payload holds {len(items)}")
    if payload.get("observedMaxUtc", -1) < 0:
        raise FormatError("observedMaxUtc must not be negative")



    seen = set()
    for item in items:
        if not isinstance(item, dict):
            raise FormatError("an index entry is not a JSON object")
        if not _is_hex_str(item.get("id"), 32):
            raise FormatError("index entry id must be 32 hex characters")
        if not _is_hex_str(item.get("contentSha256"), 64):
            raise FormatError("index entry contentSha256 must be 64 hex characters")
        if not item.get("originalName"):
            raise FormatError("index entry originalName is required")
        if not item.get("displayName"):
            raise FormatError("index entry displayName is required")
        if item.get("kind") not in ("normal", "capsule"):
            raise FormatError("index entry kind must be normal or capsule")
        sealed_at = item.get("sealedAtUtc")
        if not isinstance(sealed_at, int) or sealed_at < MIN_SEALED_AT or sealed_at > MAX_SEALED_AT:
            raise FormatError("index entry sealedAtUtc is outside the representable window")
        if item.get("kind") == "capsule":
            expiry = item.get("expiryUtc")
            if expiry is None:
                raise FormatError("a capsule needs an expiry")
            if not isinstance(expiry, int) or expiry <= sealed_at:
                raise FormatError("index entry expiry must follow the seal moment")
            if expiry > MAX_SEALED_AT:
                raise FormatError("index entry expiry is outside the representable window")
        elif item.get("expiryUtc") is not None:
            raise FormatError("a normal item must not carry an expiry")
        folder_count = item.get("folderEntryCount")
        if folder_count is not None and folder_count < 0:
            raise FormatError("index entry folderEntryCount cannot be negative")
        if item.get("flags", 0) != 0:
            raise FormatError("index entry flags must be zero in v1")
        if item["id"] in seen:
            raise FormatError("duplicate index entry id: " + item["id"])
        seen.add(item["id"])

    return payload


def _is_hex_str(value, length: int) -> bool:
    """Mirrors char.IsAsciiHexDigit: both cases, exact length."""
    return (isinstance(value, str) and len(value) == length
            and all(c in "0123456789abcdefABCDEF" for c in value))



def parse_bundle(raw: bytes, vmk: bytes, verbose: bool) -> dict:
    if len(raw) < BUNDLE_HEADER_SIZE + BUNDLE_TAIL_SIZE:
        raise FormatError("bundle is truncated")
    if raw[0:8] != BUNDLE_MAGIC:
        raise FormatError("not a bundle: magic is not NVSEALBK")

    version = raw[8]
    if version == 0 or version > 1:
        raise FormatError(f"bundle version {version} is not supported")

    kind = raw[9]
    if kind not in (1, 2):
        raise FormatError(f"bundle kind {kind} is not defined")
    if any(b != 0 for b in raw[10:12]):
        raise FormatError("bundle flag bytes must be zero")

    bundle_id = raw[12:28]
    (item_count,) = struct.unpack_from("<I", raw, 28)
    (index_length,) = struct.unpack_from("<I", raw, 32)
    (body_length,) = struct.unpack_from("<Q", raw, 36)
    (created_utc,) = struct.unpack_from("<q", raw, 44)
    (app_version,) = struct.unpack_from("<I", raw, 52)
    if any(b != 0 for b in raw[56:64]):
        raise FormatError("bundle reserved bytes must be zero")

    if len(raw) != BUNDLE_HEADER_SIZE + body_length + BUNDLE_TAIL_SIZE:
        raise FormatError(
            f"bundle is {len(raw)} bytes, header says {BUNDLE_HEADER_SIZE} + {body_length} + {BUNDLE_TAIL_SIZE}")

    body_end = BUNDLE_HEADER_SIZE + body_length
    tail = raw[body_end:]
    computed = hashlib.sha256(raw[:body_end]).digest()
    if tail != computed:
        raise FormatError("tail hash does not match the file: the bundle was damaged in transit")

    body = raw[BUNDLE_HEADER_SIZE:body_end]
    if len(body) < KEYWRAP_SIZE + index_length:
        raise FormatError("body is too short to hold the key wrap and the index")

    keywrap = body[0:KEYWRAP_SIZE]
    index_block = body[KEYWRAP_SIZE:KEYWRAP_SIZE + index_length]

    index_aad = (bundle_id + hashlib.sha256(keywrap).digest() + struct.pack("<I", item_count))
    try:
        index_plain = aes_gcm_decrypt(vmk, index_block[0:NONCE_SIZE], index_block[NONCE_SIZE:], index_aad)
    except Exception as exc:
        raise FormatError("bundle index failed authentication: wrong master key or altered package") from exc

    payload = json.loads(index_plain.decode("utf-8"))
    items = payload.get("items", [])
    if len(items) != item_count:
        raise FormatError(f"header declares {item_count} entries, index holds {len(items)}")

    rest = body[KEYWRAP_SIZE + index_length:]
    blocks = []
    while rest:
        if len(rest) < 8:
            raise FormatError("a body block is missing its length prefix")
        (length,) = struct.unpack_from("<Q", rest, 0)
        if len(rest) < 8 + length:
            raise FormatError("a body block runs past the end of the body")
        blocks.append(rest[8:8 + length])
        rest = rest[8 + length:]

    expected = item_count + (1 if kind == 2 else 0)
    if len(blocks) != expected:
        raise FormatError(f"body holds {len(blocks)} blocks, kind {kind} with {item_count} entries needs {expected}")

    if verbose:
        print(f"  bundleId      {bundle_id.hex()}")
        print(f"  kind          {kind}")
        print(f"  createdUtc    {created_utc}")
        print(f"  appVersion    {app_version}")
        print(f"  entries       {item_count}")
        print(f"  indexLength   {index_length}")
        print(f"  bodyLength    {body_length}")
        print(f"  tail sha256   {tail.hex()}  (matches)")

    containers = blocks[1:] if kind == 2 else blocks
    for i, blob in enumerate(containers):
        try:
            parse_container(blob)
        except FormatError as exc:
            raise FormatError(f"entry {i} is not a valid container: {exc}") from exc

    return {"kind": kind, "bundleId": bundle_id.hex(), "itemCount": item_count, "payload": payload}



def run_vector(manifest_path: str, container_path: str, show_hex: bool) -> int:
    with open(manifest_path, "r", encoding="utf-8") as handle:
        manifest = json.load(handle)
    with open(container_path, "rb") as handle:
        raw = handle.read()

    params = manifest["parameters"]
    item_id = base64.b64decode(params["itemId"])
    vmk = base64.b64decode(params["vmk"])
    expected_ck = base64.b64decode(params["contentKey"])

    print("frozen vector check")
    print(f"  container     {container_path}")
    print(f"  manifest      {manifest_path}")

    container = parse_container(raw)

    if container.item_id != item_id:
        print("  FAIL itemId in the header does not match the manifest")
        return 1
    if container.sealed_at != params["sealedAtUtc"]:
        print("  FAIL sealedAtUtc in the header does not match the manifest")
        return 1
    if container.chunk_size != params["chunkSize"]:
        print("  FAIL chunkSize in the header does not match the manifest")
        return 1
    if container.plaintext_length != params["plaintextLength"]:
        print("  FAIL plaintextLength in the header does not match the manifest")
        return 1
    print("  header        fields match the manifest")

    ck = container.content_key(vmk)
    if ck != expected_ck:
        print("  FAIL the key slot did not unwrap to the manifest's content key")
        return 1
    print(f"  key slot      opens under the manifest VMK -> content key {ck.hex()[:16]}…")

    container_sha = hashlib.sha256(raw).hexdigest()
    plain = container.decrypt(vmk)
    plain_sha = hashlib.sha256(plain).hexdigest()

    print(f"  container sha256  {container_sha}")
    print(f"  plaintext sha256  {plain_sha}")

    ok = True
    if container_sha != manifest["digests"]["containerSha256"]:
        print("  FAIL container sha256 does not match the manifest")
        ok = False
    if plain_sha != manifest["digests"]["plaintextSha256"]:
        print("  FAIL plaintext sha256 does not match the manifest")
        ok = False
    if len(raw) != manifest["shape"]["containerLength"]:
        print("  FAIL container length does not match the manifest")
        ok = False

    for entry in manifest.get("boundaries", []):
        header = base64.b64decode(entry["header"])
        parsed = Container(header)
        structural = (
            parsed.version == 1
            and parsed.algo == 1
            and parsed.flags == 0
            and parsed.item_id == item_id
            and parsed.plaintext_length == entry["plaintextLength"]
            and len(header) == CONTAINER_HEADER_SIZE
            and entry["containerLength"] == CONTAINER_HEADER_SIZE
                + entry["blockCount"] * (BLOCK_PREFIX_SIZE + NONCE_SIZE + TAG_SIZE)
                + entry["plaintextLength"]
        )
        print(f"  boundary {entry['name']:<15} {'ok' if structural else 'FAIL'}")
        ok = ok and structural

    if show_hex:
        print("  container hex")
        print(hexdump(raw))

    print("  result        PASS" if ok else "  result        FAIL")
    return 0 if ok else 1



def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description="Independent reader for the Novara sealed container format v1 (read-only).")
    parser.add_argument("--vector", metavar="MANIFEST", help="frozen vector manifest (.json)")
    parser.add_argument("--container", metavar="FILE", help="a .novaseal container")
    parser.add_argument("--index", metavar="FILE", help="an index.nseal file")
    parser.add_argument("--bundle", metavar="FILE", help="a .novabundle package")
    parser.add_argument("--vmk", metavar="KEY", help="space master key as hex (64 chars) or base64")
    parser.add_argument("--out", metavar="FILE", help="write the decrypted bytes here")
    parser.add_argument("--hex", action="store_true", help="print the container bytes in hex")
    parser.add_argument("--quiet", action="store_true", help="print only the digests")
    args = parser.parse_args(argv)

    try:
        if args.vector:
            if not args.container:
                parser.error("--vector needs --container")
            return run_vector(args.vector, args.container, show_hex=args.hex)

        if args.container:
            if not args.vmk:
                parser.error("--container needs --vmk")
            with open(args.container, "rb") as handle:
                raw = handle.read()
            vmk = read_key(args.vmk)
            plain = parse_container(raw).decrypt(vmk)
            print(f"container sha256  {hashlib.sha256(raw).hexdigest()}")
            print(f"plaintext sha256  {hashlib.sha256(plain).hexdigest()}")
            print(f"plaintext bytes   {len(plain)}")
            if args.out:
                with open(args.out, "wb") as handle:
                    handle.write(plain)
                print(f"wrote             {args.out}")
            if args.hex:
                print(hexdump(raw))
            return 0

        if args.index:
            if not args.vmk:
                parser.error("--index needs --vmk")
            with open(args.index, "rb") as handle:
                raw = handle.read()
            vmk = read_key(args.vmk)
            payload = parse_index(raw, vmk)
            if not args.quiet:
                print(f"entries           {len(payload['items'])}")
                print(f"observedMaxUtc    {payload.get('observedMaxUtc')}")
                print(json.dumps(payload["items"], ensure_ascii=False, indent=2))
            return 0

        if args.bundle:
            if not args.vmk:
                parser.error("--bundle needs --vmk")
            with open(args.bundle, "rb") as handle:
                raw = handle.read()
            vmk = read_key(args.vmk)
            print(f"bundle sha256     {hashlib.sha256(raw).hexdigest()}")
            parse_bundle(raw, vmk, verbose=not args.quiet)
            print("result            PASS")
            return 0

        parser.print_help()
        return 2

    except FormatError as exc:
        sys.stderr.write(f"error: {exc}\n")
        return 1
    except OSError as exc:
        sys.stderr.write(f"error: {exc}\n")
        return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))