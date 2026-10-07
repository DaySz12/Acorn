# Acorn vault format

This document describes `vault.acorn` (format version **2**), the key hierarchy and how the file is
written. The reference implementation is `src/Acorn.Core` (`Format/`, `Crypto/`, `Storage/`,
`VaultSession.cs`). All integers are little-endian.

## 1. Key hierarchy

```
master password ──NFC──UTF-8──Argon2id(salt_pw, m, t, p)──► KEK_pw ─┐
                                                                   ├─ AES-256-GCM wrap ─► DEK (32 random bytes)
recovery key (200 random bits) ──HKDF-SHA256(salt_rec)──► KEK_rec ─┘                         │
                                                                                              ▼
                                     VaultData JSON ──AES-256-GCM(DEK, fresh nonce, AAD = header)──► payload
```

* **DEK** – 32 bytes from `RandomNumberGenerator`; encrypts the payload. Never written in clear.
* **KEK_pw** – Argon2id(password, 16-byte salt, parameters from the header) → 32 bytes.
* **KEK_rec** – HKDF-SHA256(ikm = 25 recovery-key bytes, salt = 16 bytes, info = `"Acorn recovery KEK v1"`) → 32 bytes.
* The DEK is wrapped twice (password slot, recovery slot) with AES-256-GCM, 12-byte random nonce, 16-byte tag.
* KEKs and the DEK live in pinned arrays and are zeroed after use / on lock.

### Why HKDF (not Argon2id) for the recovery key

SPEC 4.2 allows either. The recovery key is 200 bits from `RandomNumberGenerator`, far above the
128-bit threshold, so it cannot be brute-forced regardless of KDF cost. A slow KDF would only add
delay to legitimate recovery. HKDF-SHA256 with a per-vault random salt and a fixed `info` label is
therefore used.

### Recovery key encoding

25 random bytes → 40 symbols of Crockford Base32 (`0123456789ABCDEFGHJKMNPQRSTVWXYZ`, no I/L/O/U),
shown as 8 groups of 5: `XXXXX-XXXXX-XXXXX-XXXXX-XXXXX-XXXXX-XXXXX-XXXXX`. Input is case-insensitive,
ignores spaces/hyphens and reads `O` as `0`, `I`/`L` as `1`. It is displayed once and never stored.

## 2. Algorithms and parameters

| Purpose | Algorithm | Notes |
|---|---|---|
| Password → KEK | Argon2id | salt 16 B, output 32 B, parameters stored in header |
| Recovery key → KEK | HKDF-SHA256 | salt 16 B, info `Acorn recovery KEK v1` |
| DEK wrap, payload | AES-256-GCM | nonce 12 B random per encryption, tag 16 B |
| Randomness | `RandomNumberGenerator` | never `System.Random` |
| Secret comparison | `CryptographicOperations.FixedTimeEquals` | |

**Default Argon2id parameters** (new vaults, `Argon2idParameters.Recommended`): 64 MiB, 3 iterations,
parallelism 2 — the SPEC default. "Upgrade KDF strength" offers 256 MiB, 3 iterations, parallelism 4
(`Argon2idParameters.Strong`).

**Accepted bounds** (checked when parsing, *before* any key derivation, so a corrupted or hostile
header cannot exhaust memory or hang the app):

| Field | Min | Max |
|---|---|---|
| memoryKiB | 8 192 (8 MiB) | 1 048 576 (1 GiB), and ≥ 8 × parallelism |
| iterations | 1 | 20 |
| parallelism | 1 | 16 |

On unlock, parameters weaker than Recommended (memory or iterations lower) cause the app to offer
(not force) an upgrade.

### Benchmark

`dotnet run -c Release --project tools/Acorn.KdfBenchmark` prints the median of 5 derivations per
setting. Measured on the development machine (8 logical CPUs, Windows 11, .NET 10.0.8):

| memory | iterations | parallelism | median |
|---|---|---|---|
| 32 MiB | 3 | 2 | 128 ms |
| **64 MiB** | **3** | **2** | **243 ms** (default) |
| 64 MiB | 4 | 2 | 266 ms |
| 96 MiB | 3 | 2 | 349 ms |
| 128 MiB | 4 | 2 | 500 ms |
| **256 MiB** | **3** | **4** | **475 ms** (Strong) |

This machine is faster than average; the SPEC target of ~0.5–1 s is expected on typical laptops for
the default. The default was kept at the SPEC value rather than raised to fill 0.5 s here, so that
older machines are not penalised; users can opt into Strong.

## 3. File layout (format version 2)

| Offset | Size | Field |
|---|---|---|
| 0 | 5 | magic `"ACORN"` |
| 5 | 2 | formatVersion = `2` (uint16) |
| 7 | 1 | password KDF algorithm = `1` (Argon2id) |
| 8 | 4 | memoryKiB (uint32) |
| 12 | 4 | iterations (uint32) |
| 16 | 4 | parallelism (uint32) |
| 20 | 16 | password salt |
| 36 | 1 | recovery KDF algorithm = `1` (HKDF-SHA256) |
| 37 | 16 | recovery salt |
| 53 | 60 | wrappedDekByPassword = nonce (12) ‖ ciphertext (32) ‖ tag (16) |
| 113 | 60 | wrappedDekByRecovery = nonce (12) ‖ ciphertext (32) ‖ tag (16) |
| **173** | — | *end of header* |
| 173 | 12 | payload nonce |
| 185 | 16 | payload tag |
| 201 | 4 | payload ciphertext length *n* (uint32, ≤ 64 MiB) |
| 205 | *n* | payload ciphertext |

The file must end exactly after the ciphertext; truncated or padded files are rejected.

### Associated data (AAD)

* **Payload AAD** = header bytes `[0, 173)` *exactly as stored*. This covers the magic, formatVersion,
  algorithm ids, all KDF parameters, both salts and both wrapped DEKs. Changing any of them makes the
  payload fail authentication (tests flip every header byte).
* **Wrap AAD** binds each wrapped DEK to its own derivation inputs. It is built from parsed values,
  not raw bytes, so it is identical in every container version:
  * password slot: `"ACORN/wrap/password/v1"` ‖ `0x01` ‖ memoryKiB ‖ iterations ‖ parallelism ‖ password salt
  * recovery slot: `"ACORN/wrap/recovery/v1"` ‖ `0x01` ‖ recovery salt

  Because the recovery wrap does not depend on the container layout, a migrated file keeps a valid
  recovery slot without needing the recovery key.

## 4. Payload

UTF-8 JSON of `VaultData` (`System.Text.Json` source generator, camelCase):

```json
{
  "schemaVersion": 2,
  "entries": [
    {
      "id": "6f1c1a52-8d3e-4c2b-9a51-0d5c3e2b7a10",
      "type": "server",
      "name": "web-01",
      "env": "prod",
      "host": "10.0.0.5",
      "port": 2222,
      "username": "deploy",
      "password": "…",
      "sshKeyPath": "~/.ssh/id_ed25519",
      "tags": ["web", "nginx"],
      "customFields": [{ "id": "…", "label": "API token", "value": "…", "isSecret": true }],
      "notes": "",
      "favorite": false,
      "createdAt": "2026-10-07T09:41:47Z",
      "updatedAt": "2026-10-07T09:41:47Z"
    }
  ],
  "settings": {
    "clipboardClearSeconds": 30, "autoLockMinutes": 5, "lockOnMinimize": true,
    "screenCaptureProtection": true, "revealSeconds": 12, "backupKeep": 10,
    "theme": "system", "environments": ["dev", "staging", "prod"]
  }
}
```

The plaintext JSON exists only in memory (zeroed after encryption/decryption). Settings are clamped to
their valid ranges after loading.

## 5. Writing (SPEC R2, R3)

Every save:

1. Serialize → encrypt with the DEK, a **new random nonce**, AAD = freshly serialized header.
2. If `vault.acorn` exists, copy it to `backups/vault-YYYYMMDD-HHmmss.acorn.bak` (UTC; `-2`, `-3`… on
   collisions within the same second).
3. Write `vault.acorn.tmp` (owner-only on Unix), `Flush(flushToDisk: true)`.
4. Read the temp file back, parse it and decrypt the payload with the DEK (for password changes, also
   unwrap the DEK with the new password). Failure aborts.
5. Replace: `File.Replace` on Windows, `rename` (`File.Move(overwrite: true)`) elsewhere.
6. Prune backups to the configured count (default 10, range 5–50). Only files matching the backup name
   pattern are ever deleted.

If any step fails, `vault.acorn` is untouched and the temp file is removed. A save is skipped (no write,
no backup) when the serialized payload is byte-identical to the last one written (compared by SHA-256
in memory). A leftover `vault.acorn.tmp` found at start-up is moved to
`backups/unfinished-YYYYMMDD-HHmmss.acorn.tmp` and the user is told; the vault itself is not touched.

## 6. Changing keys

| Operation | DEK | Password wrap | Recovery wrap | Payload |
|---|---|---|---|---|
| Change master password | **new** | new salt (params ≥ Recommended) | **new recovery key** | re-encrypted |
| Regenerate recovery key | **new** | new salt, same params | **new recovery key** | re-encrypted |
| Recover with recovery key | **new** | new password | **new recovery key** | re-encrypted |
| Upgrade KDF strength | same | new salt + stronger params | unchanged | re-encrypted (new nonce) |

**Decision: rotate the DEK on password change.** If the old master password leaked together with an old
copy of the file, an attacker could unwrap the old DEK. Without rotation, that DEK would also decrypt every
future version of the vault. Rotation closes that gap. The cost is that the recovery slot must be
re-created, so the user is shown a new recovery key (the old one stops opening the current file
immediately). The current password is verified first (by unwrapping and comparing the DEK in constant
time), and the old DEK is only zeroed after the new file has been verified on disk.

Old backups keep the keys they were written with; see SECURITY.md.

## 7. Versions and migration

* **Container version** (`formatVersion`) — readers exist for every version from
  `VaultFormat.OldestSupportedVersion` (1) to `CurrentVersion` (2). Writing always produces the current
  version.
* **Payload schema** (`schemaVersion`) — `PayloadMigrator` applies `IPayloadMigration` steps in order.

On unlock, if either is older than current, the data is migrated in memory, the old file is backed up and
the vault is rewritten in the current format (same DEK; the recovery wrap stays valid because its AAD is
layout-independent). A newer `formatVersion` or `schemaVersion` is rejected with a clear message and the
file is never modified.

**Version 1** was the initial draft of the layout during development: it lacks the two algorithm-id bytes
(header = 171 bytes; Argon2id and HKDF-SHA256 are implied) and its payload is schema 1, which stored
`tags` as one comma-separated string. No released build writes it. It is kept, with a committed fixture
(`tests/Acorn.Core.Tests/Fixtures/vault-v1.acorn`, password `acorn-fixture-v1-password`), so that the
migration path is exercised by tests from day one. Regenerate the fixture with
`Acorn.Core.Tests.exe -explicit only -method "*Regenerate_v1_fixture"`.

## 8. Files on disk

| File | Purpose |
|---|---|
| `vault.acorn` | the vault |
| `vault.acorn.lock` | held open with `FileShare.None` while the app runs (single writer); safe to delete when Acorn is closed |
| `vault.acorn.tmp` | transient during a save |
| `backups/vault-YYYYMMDD-HHmmss[-n].acorn.bak` | automatic backups (encrypted, same format) |
| `backups/unfinished-YYYYMMDD-HHmmss.acorn.tmp` | quarantined temp file from an interrupted save |
