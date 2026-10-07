# Acorn — Offline Password & Server Vault (Spec)

> เอกสารนี้เป็น spec สำหรับให้ Claude Code อ่านแล้วสร้างโปรเจกต์ทั้งหมด
> ชื่อโปรเจกต์ `Acorn` เปลี่ยนได้ภายหลัง

---

## 0. วิธีใช้กับ Claude Code

วางไฟล์นี้ไว้ที่ root ของ repo ว่าง แล้วสั่ง:

```
อ่าน SPEC.md แล้วสร้างโปรเจกต์ตามนั้นทีละขั้นตาม "Milestones" (หัวข้อ 12)
หลังจบแต่ละ milestone ให้รัน build + test และสรุปผลก่อนไปต่อ
```

**กฎสำหรับ Claude Code**
- ห้ามคิด algorithm เข้ารหัสเอง ใช้ library มาตรฐานตามหัวข้อ 4 เท่านั้น
- ห้าม log / print / ใส่ใน exception message ซึ่งรหัสผ่าน, master password, key, plaintext ของ vault
- ห้ามมี network call ใดๆ ใน runtime (ไม่มี telemetry, ไม่มี update checker, ไม่โหลดอะไรจาก CDN)
- ถ้าสิ่งใดทดสอบอัตโนมัติไม่ได้ (เช่น UI บน Mac, screen lock event) ให้เขียนไว้ใน `docs/MANUAL_TESTS.md` ตรงๆ อย่าอ้างว่าทดสอบแล้ว
- ต้องเขียนตามสถาปัตยกรรม **MVC** ในหัวข้อ 3 อย่างเคร่งครัด: View ห้ามมี business logic / ห้ามเรียก Core หรือ crypto โดยตรง ทุก action ต้องผ่าน Controller

---

## 1. เป้าหมาย

แอป desktop สำหรับเก็บ username / password / IP ของ VM และ login ต่างๆ
- ใช้เองเป็นหลัก แจกเพื่อนผ่าน GitHub (clone → รัน setup → ใช้ได้เลย)
- ทำงาน **offline 100%** ไม่มี web server ไม่เปิด port
- รองรับ **Windows และ macOS** (Linux ได้ก็ดี แต่ไม่บังคับ)
- UI สวยแบบโน้ต สะอาด มี Dark mode
- แต่ละคนมี vault และ master password ของตัวเอง โค้ดที่แจกไม่มีข้อมูลผู้ใช้

## 2. Tech stack

| ส่วน | เลือก |
|---|---|
| Language / Runtime | C# / .NET 8 (LTS) หรือใหม่กว่า |
| UI | **Blazor Hybrid ด้วย Photino.Blazor** (NuGet `Photino.Blazor`) |
| Crypto | `System.Security.Cryptography.AesGcm` + **Argon2id** (NuGet `Konscious.Security.Cryptography.Argon2`) |
| Clipboard | NuGet `TextCopy` (หรือเทียบเท่า) ฝั่ง C# ไม่ใช้ browser clipboard API |
| Architecture | **MVC** (ดูหัวข้อ 3) — ไม่ใช้ ASP.NET Core MVC/Kestrel เพราะจะเปิด port ขัดกับข้อกำหนด offline จึงใช้รูปแบบ MVC บน Blazor Hybrid แทน |
| Test | xUnit (+ Moq หรือ NSubstitute สำหรับ mock service ในการทดสอบ Controller) |
| Distribution | `setup.bat` / `setup.sh` + (รอบ 2) GitHub Actions release |

หมายเหตุ: ก่อนเลือกเวอร์ชัน package ให้ตรวจ NuGet ว่าเวอร์ชันล่าสุดที่เข้ากับ .NET ที่ใช้อยู่คือเท่าไร อย่าเดาจากความจำ

## 3. โครงสร้างโปรเจกต์

```
Acorn.sln
├─ src/
│  ├─ Acorn.Core/            ← MODEL: crypto + vault format + storage (ไม่ผูก UI)
│  │   ├─ Crypto/              (Kdf, Aead, KeyWrap, RecoveryKey)
│  │   ├─ Format/              (VaultHeader, VaultFile, Migrations)
│  │   ├─ Storage/             (AtomicFileWriter, BackupRotator, VaultLock)
│  │   ├─ Models/              (VaultData, Entry, CustomField)
│  │   └─ VaultSession.cs      (unlock / lock / save / change password)
│  ├─ Acorn.Application/     ← CONTROLLER layer (C# ล้วน ไม่ reference Blazor/Photino)
│  │   ├─ Controllers/         (UnlockController, VaultController, EntryController,
│  │   │                        SettingsController, BackupController)
│  │   ├─ ViewModels/          (UnlockViewModel, EntryListViewModel, EntryEditViewModel, ...)
│  │   ├─ Abstractions/        (IClipboardService, ISessionEvents, IScreenProtection,
│  │   │                        IAutoLockTimer, INavigator, IClock)
│  │   └─ AppState.cs          (state กลางที่ View subscribe; เปลี่ยนได้ผ่าน Controller เท่านั้น)
│  └─ Acorn.App/             ← VIEW layer + Photino.Blazor host + platform impl
│      ├─ Views/               (Razor: UnlockView, CreateVaultView, RecoveryView,
│      │                        VaultView, EntryEditView, SettingsView, BackupsView)
│      ├─ Components/          (EntryCard, SecretField, Sidebar, SearchBox, ...)
│      ├─ Platform/            (Windows/, MacOS/ — implement interface จาก Abstractions)
│      ├─ Program.cs           (composition root: DI, Photino, CSP)
│      └─ wwwroot/             (css, ฟอนต์ — ฝังในแอปทั้งหมด)
├─ tests/
│  ├─ Acorn.Core.Tests/
│  └─ Acorn.Application.Tests/   ← ทดสอบ Controller ด้วย mock
├─ docs/  (MANUAL_TESTS.md, FORMAT.md, ARCHITECTURE.md)
├─ setup.bat
├─ setup.sh
├─ README.md
├─ SECURITY.md
├─ LICENSE (MIT)
└─ .gitignore
```

**หลักการ:** `Acorn.Core` และ `Acorn.Application` ต้องไม่ reference UI/Blazor/Photino ใดๆ และต้องทดสอบได้ด้วย unit test ล้วน

### 3.1 กติกา MVC

**Model (`Acorn.Core`)**
- เป็นเจ้าของข้อมูลและกฎของ vault ทั้งหมด: crypto, format, storage, `VaultSession`, `Entry`
- ไม่รู้จัก Controller, View หรือ ViewModel

**Controller (`Acorn.Application/Controllers`)**
- เป็น **ทางเดียว** ที่ View ใช้สั่งงาน เช่น `UnlockController.UnlockWithPasswordAsync(...)`, `EntryController.SaveAsync(...)`, `VaultController.Lock()`
- ทำหน้าที่: validate input → เรียก Model (`VaultSession`) / Service ผ่าน interface → อัปเดต `AppState` → คืนผลเป็น `Result<T>` (สำเร็จ/ข้อผิดพลาดแบบข้อความทั่วไปที่ไม่เปิดเผยข้อมูลลับ)
- รับ dependency ผ่าน constructor (DI) เท่านั้น ห้ามใช้ static/singleton ซ่อน
- เป็นที่ implement นโยบาย R5–R8: ล้าง clipboard + zero key + reset state เมื่อ lock/auto-lock/sleep/exit, ตั้ง timer ซ่อนรหัส ฯลฯ (ตัว OS-specific อยู่หลัง interface)
- ห้าม reference ชนิดจาก Blazor, Photino, `System.Windows`, หรือ API เฉพาะ OS

**View (`Acorn.App/Views`, `Components`)**
- Razor component ที่ **แสดงผลและส่ง event เท่านั้น**: bind กับ ViewModel / `AppState`, เรียก Controller เมื่อผู้ใช้กดปุ่ม
- ห้ามเรียก `VaultSession`, crypto, ระบบไฟล์, clipboard, หรือ `HttpClient` โดยตรง ห้ามมีกฎธุรกิจ (เช่น การตรวจความแข็งแรงรหัสต้องอยู่ใน Controller/Model แล้ว View แค่แสดงผล)
- Inject ได้เฉพาะ Controller และ `AppState`
- ใช้ `@implements IDisposable` เพื่อ unsubscribe จาก `AppState` ทุกครั้ง และเมื่อ lock ต้องไม่เหลือข้อมูลลับค้างใน field/state ของ component

**ViewModel (`Acorn.Application/ViewModels`)**
- DTO ที่ Controller ส่งให้ View ประกอบด้วยเฉพาะข้อมูลที่ View ต้องแสดง
- รหัสผ่านใน `EntryListViewModel` ต้อง **ไม่มีค่าจริง** (มีแค่ `HasPassword`) ค่าจริงส่งให้ View เฉพาะตอนผู้ใช้กด "แสดง" ผ่าน `EntryController.RevealSecretAsync(...)` และมี timer ซ่อนกลับ (R7)
- การ copy ลง clipboard ทำใน Controller (`EntryController.CopyPasswordAsync`) ค่าลับจึงไม่ผ่าน View/JS เลย

**Service (Platform)**
- `IClipboardService`, `ISessionEvents`, `IScreenProtection`, `IAutoLockTimer` ถูกประกาศใน `Abstractions` และ implement ใน `Acorn.App/Platform` แยกตาม OS (Windows/Mac) ลงทะเบียนใน `Program.cs`

**Flow ตัวอย่าง (Unlock)**
```
UnlockView (กดปุ่ม) → UnlockController.UnlockWithPasswordAsync(pw)
   → VaultSession.UnlockAsync (Model) → สำเร็จ: AppState.Status = Unlocked, โหลด ViewModel
   → INavigator.GoTo(VaultView)  → View render ใหม่จาก AppState
```

**ข้อห้ามข้ามชั้น (ให้ enforce ด้วย test)**
- มี architecture test (เช่น NetArchTest หรือ reflection ธรรมดา) ตรวจว่า:
  - `Acorn.Core` ไม่ reference `Acorn.Application` / `Acorn.App`
  - `Acorn.Application` ไม่ reference `Acorn.App`, Blazor, Photino
  - Component ใน `Acorn.App/Views` ไม่ inject `VaultSession` หรือ interface ใน `Abstractions` โดยตรง (ยกเว้น Controller/AppState)

## 4. Crypto design

### 4.1 โครงสร้างกุญแจ (key hierarchy)

- สุ่ม **DEK** (Data Encryption Key, 32 bytes) ตอนสร้าง vault — ใช้เข้ารหัสข้อมูลจริง
- DEK ถูก **wrap (เข้ารหัส)** ไว้ 2 ชุดใน header:
  1. `wrappedDekByPassword` — ด้วย KEK ที่ derive จาก master password (Argon2id)
  2. `wrappedDekByRecovery` — ด้วย KEK ที่ derive จาก recovery key
- ข้อดี: เปลี่ยน master password = สร้าง KEK ใหม่แล้ว re-wrap DEK (ไม่ต้องรอเข้ารหัสข้อมูลทั้งก้อนใหม่ แต่ให้สุ่ม salt/nonce ใหม่ และเขียนไฟล์ใหม่ทั้งไฟล์)
- Wrap ด้วย AES-256-GCM (nonce สุ่ม 12 bytes, tag 16 bytes)

### 4.2 Algorithm

| งาน | ใช้ |
|---|---|
| Master password → KEK | **Argon2id**, salt สุ่ม 16 bytes, output 32 bytes |
| Recovery key → KEK | Argon2id เช่นกัน (ใช้ salt แยก) หรือ HKDF-SHA256 ถ้า recovery key มี entropy ≥ 128 bit จากการสุ่ม — ให้เลือกอย่างใดอย่างหนึ่งและบันทึกใน `docs/FORMAT.md` |
| เข้ารหัสข้อมูล | **AES-256-GCM** ด้วย DEK |
| Nonce | สุ่ม 12 bytes ใหม่ **ทุกครั้งที่บันทึก** ห้ามใช้ซ้ำ |
| สุ่ม | `RandomNumberGenerator` เท่านั้น ห้ามใช้ `System.Random` |
| เปรียบเทียบค่าลับ | `CryptographicOperations.FixedTimeEquals` |
| ล้างหน่วยความจำ | `CryptographicOperations.ZeroMemory` กับ key/`byte[]` ทุกตัวเมื่อ lock |

**ค่าเริ่มต้น Argon2id** (ปรับได้ใน header): memory 64 MiB, iterations 3, parallelism 2 (เป้าหมาย ~0.5–1 วินาทีบนเครื่องทั่วไป ให้เขียน benchmark เล็กๆ เพื่อเลือกค่าและบันทึกไว้)

### 4.3 Recovery key

- สร้างตอนสร้าง vault: สุ่ม 160 bit (หรือมากกว่า) แสดงเป็นข้อความอ่านง่าย แบ่งกลุ่ม เช่น Base32 `XXXXX-XXXXX-...` (หลีกเลี่ยงตัวอักษรที่สับสน)
- **แสดงครั้งเดียว** ตอนสร้าง พร้อมปุ่มพิมพ์/คัดลอก และให้ผู้ใช้ **ยืนยันว่าเก็บแล้ว** (เช่น ให้พิมพ์กลับบางส่วนเพื่อยืนยัน) ก่อนใช้งานต่อ
- ไม่เก็บ recovery key เป็น plaintext ที่ใดเลย
- มีหน้า "ปลดล็อกด้วย Recovery key" → ตั้ง master password ใหม่ได้
- มีฟังก์ชัน "สร้าง recovery key ใหม่" (ต้อง unlock ก่อน) ซึ่งทำให้ key เก่าใช้ไม่ได้

## 5. รูปแบบไฟล์ vault

ไฟล์เดียว `vault.acorn` (ชื่อปรับได้) โครงสร้างแบบ binary หรือ JSON-header ก็ได้ แต่ต้องมี:

```
magic bytes        "ACORN"
formatVersion      uint16        ← สำหรับ migration
header {
  kdf: { algorithm:"argon2id", memoryKiB, iterations, parallelism, salt }
  recovery: { kdf params, salt }
  wrappedDekByPassword: { nonce, ciphertext, tag }
  wrappedDekByRecovery: { nonce, ciphertext, tag }
}
payload { nonce, ciphertext, tag }   ← AES-256-GCM(DEK, VaultData JSON)
```

**ข้อบังคับ**
- ส่วน header ทั้งหมด (รวม formatVersion และ KDF params) ต้องถูกใช้เป็น **AAD** ของ AES-GCM เพื่อกันการแก้ header (เช่น ลด iterations) โดยไม่ถูกตรวจพบ
- ข้อมูลทั้งหมด (รวมชื่อรายการ, host, tag) อยู่ใน payload ที่เข้ารหัส ไม่รั่วออกนอก payload
- Validate ขอบเขตของค่าใน header ก่อน derive key (เช่น memory/iterations ต้องอยู่ในช่วงที่ยอมรับ) กันไฟล์ที่ถูกทำให้เสียหายทำให้แอปค้างหรือกินแรมหมด
- มี **migration framework**: อ่าน `formatVersion` เก่าแล้วอัปเกรดเป็นเวอร์ชันปัจจุบันตอน unlock และเขียนกลับ (พร้อม backup ก่อน) มี test ด้วยไฟล์ตัวอย่างของเวอร์ชันเก่า (สร้าง fixture ไว้ใน tests)
- ถ้า `formatVersion` ใหม่กว่าที่แอปรู้จัก ให้ปฏิเสธเปิดพร้อมข้อความชัดเจน ห้ามเขียนทับ
- บันทึกรายละเอียดทั้งหมดลง `docs/FORMAT.md`

**ตำแหน่งไฟล์ (นอกโฟลเดอร์โปรเจกต์เสมอ)**
- Windows: `%APPDATA%\Acorn\`
- macOS: `~/Library/Application Support/Acorn/`
- (Linux: `$XDG_DATA_HOME/Acorn` หรือ `~/.local/share/Acorn`)
- ใช้ `Environment.GetFolderPath(SpecialFolder.ApplicationData)` และ `.gitignore` ต้องกัน `*.acorn`, `*.bak`, `*.tmp` ไว้ด้วย

## 6. Data model

```csharp
VaultData { int SchemaVersion; List<Entry> Entries; Settings Settings; }

Entry {
  Guid Id;
  string Type;            // "server" | "login" | "note"
  string Name;
  string Env;             // "dev" | "staging" | "prod" | "" (ตั้งค่าเพิ่มได้)
  string Host;            // IP / hostname
  int? Port;
  string Username;
  string Password;
  string SshKeyPath;      // เก็บ path อ้างอิงเท่านั้น (ไม่เก็บ private key ในรอบ MVP)
  List<string> Tags;
  List<CustomField> CustomFields;   // { Label, Value, IsSecret }
  string Notes;
  bool Favorite;
  DateTime CreatedAt, UpdatedAt;
}
```

## 7. ฟีเจอร์ MVP (ต้องมีครบ)

### 7.1 พื้นฐาน
1. สร้าง vault ใหม่ (ตั้ง master password + แสดง recovery key)
2. ปลดล็อกด้วย master password / ด้วย recovery key
3. เพิ่ม / แก้ / ลบ / ค้นหา entry (ค้นหาแบบ in-memory เมื่อ unlock แล้วเท่านั้น)
4. กรองตาม env และ tag, ดาว favorite
5. ปุ่ม **Copy** ที่ host / username / password และ **Copy `ssh user@host -p port`**
6. Password generator (ความยาวและชุดตัวอักษรปรับได้ ใช้ `RandomNumberGenerator`)
7. Dark mode
8. คีย์ลัด `Ctrl/Cmd+K` เปิดค้นหาเร็ว

### 7.2 ความปลอดภัยและความทนทานของข้อมูล (บังคับ)

**R1. Recovery key** — ตามหัวข้อ 4.3

**R2. Backup อัตโนมัติ**
- ก่อน **ทุกครั้ง** ที่เขียนทับ vault ให้ copy ไฟล์เดิมไปเป็น `backups/vault-YYYYMMDD-HHmmss.acorn.bak`
- เก็บล่าสุด **10 เวอร์ชัน** (ตั้งค่าได้ 5–50) ลบเก่าสุดเมื่อเกิน
- มีหน้า "Restore from backup" เลือกเวอร์ชันย้อนกลับได้ (ผู้ใช้ต้องรู้ master password ของเวอร์ชันนั้นอยู่แล้ว เพราะไฟล์ backup เข้ารหัสอยู่)
- อย่า backup ถี่เกินจำเป็นหากไม่มีการเปลี่ยนแปลงจริง (ข้ามถ้าเนื้อหา payload ไม่เปลี่ยน)

**R3. เขียนไฟล์แบบ atomic**
1. เขียนไฟล์ใหม่ลง `vault.acorn.tmp` ในโฟลเดอร์เดียวกัน
2. `Flush(flushToDisk: true)` ให้ข้อมูลลงดิสก์จริง
3. **อ่านกลับมา verify** (ถอดรหัสได้และ tag ผ่าน) ก่อนแทนที่ไฟล์จริง
4. แทนที่ด้วย `File.Replace` (Windows) / `File.Move(overwrite:true)` (Mac/Linux, atomic rename บน filesystem เดียวกัน)
5. ถ้าขั้นใดล้มเหลว ไฟล์ vault เดิมต้องไม่ถูกแตะ
- ตอนเปิดแอป ถ้าเจอ `.tmp` ค้างจากรอบก่อน ให้จัดการอย่างปลอดภัย (ไม่ลบไฟล์จริง, แจ้งผู้ใช้หรือเก็บไว้เป็น backup)
- ตั้ง permission ไฟล์ให้เจ้าของอ่านเขียนคนเดียว (Mac/Linux `chmod 600` ผ่าน `File.SetUnixFileMode`; Windows ใช้ ACL ของ user profile ที่เป็นค่าเริ่มต้นก็เพียงพอ)

**R4. File lock (กันเปิดสองตัวพร้อมกัน)**
- เปิด `vault.acorn.lock` ด้วย `FileStream(..., FileShare.None)` ค้างไว้ตลอดอายุแอป
- ถ้าเปิดไม่ได้ ให้แสดง "มีอีกหน้าต่างหรือโปรเซสกำลังใช้ vault นี้อยู่" แล้วไม่ให้เขียน (อาจเปิดแบบอ่านอย่างเดียวหรือออก)
- ต้องปล่อย lock เมื่อปิดแอป และต้องทนต่อกรณีแอปค้าง/ถูกฆ่า (ไฟล์ lock ค้างแต่ OS lock หลุดแล้ว ต้องเปิดใหม่ได้ ห้ามพึ่งแค่ "มีไฟล์อยู่ไหม")
- มี test: เปิดสอง instance ในโปรเซสเดียวกัน instance ที่สองต้องถูกปฏิเสธ

**R5. Clear clipboard**
- ทุกครั้งที่ copy ค่าลับ ตั้ง timer ล้างใน **30 วินาที** (ตั้งค่าได้)
- ล้าง **เฉพาะเมื่อ clipboard ยังเป็นค่าที่แอปใส่ไว้** (เทียบแล้วตรงกันเท่านั้น) เพื่อไม่ลบสิ่งที่ผู้ใช้ copy มาจากที่อื่นทีหลัง
- ล้างทันทีเมื่อ: **lock vault, auto-lock, ปิดแอป** (รวมการปิดหน้าต่างและ `ProcessExit`)
- บน Windows พิจารณาใส่ flag ไม่ให้ Clipboard History / cloud clipboard เก็บค่า (`ExcludeClipboardContentFromMonitorProcessing`) เป็น best-effort
- บันทึกข้อจำกัดใน SECURITY.md: แอปอื่นที่เฝ้า clipboard ได้ย่อมอ่านค่าได้ในช่วงที่อยู่ใน clipboard

**R6. Lock เมื่อหน้าจอล็อก / เครื่อง sleep**
- Windows: `Microsoft.Win32.SystemEvents.SessionSwitch` (SessionLock) และ `PowerModeChanged` (Suspend)
- macOS: ฟัง screen lock / sleep notification (`com.apple.screenIsLocked` distributed notification และ `NSWorkspaceWillSleepNotification`) ผ่าน Objective-C interop หรือวิธีที่เสถียรที่สุดที่หาได้ ถ้าทำไม่ได้ให้ **fallback เป็น auto-lock timer** และเขียนข้อจำกัดไว้ใน README/SECURITY.md
- เมื่อ event เกิด: ล้าง clipboard → zero key ในหน่วยความจำ → ล้าง state ใน UI → กลับหน้า Unlock
- นอกจากนี้มี **Auto-lock เมื่อไม่ได้ใช้งาน** (ค่าเริ่มต้น 5 นาที ตั้งได้) และ lock เมื่อหน้าต่างถูก minimize (ตั้งค่าเปิด/ปิดได้)
- ออกแบบ `ISessionEvents` เป็น interface ให้ mock ใน test ได้

**R7. ซ่อนรหัสเป็นค่าเริ่มต้น + กัน screenshot**
- รหัสผ่านและ custom field ที่เป็น secret แสดงเป็น `••••••••` เสมอ กดปุ่มตาเพื่อแสดงทีละรายการ และกลับไปซ่อนเองหลัง ~10–15 วินาที หรือเมื่อเปลี่ยนหน้า
- **Windows:** เรียก `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` (P/Invoke, ต้องการ Windows 10 2004+) บนหน้าต่างหลัก ถ้าเวอร์ชันไม่รองรับให้ fallback เป็น `WDA_MONITOR` และถ้าไม่ได้เลยให้เงียบๆ พร้อมบันทึกใน log (ไม่มีข้อมูลลับ)
- **macOS:** ตั้ง `NSWindow.sharingType = NSWindowSharingNone` ผ่าน Objective-C interop เป็น best-effort
- ให้เปิด/ปิดได้ใน Settings (ค่าเริ่มต้น **เปิด**) เพราะบางคนต้องการ screenshare หน้าอื่น
- เขียนข้อจำกัดตรงๆ ใน SECURITY.md: ป้องกันได้เฉพาะการจับภาพหน้าจอผ่าน API มาตรฐาน กล้องถ่ายหน้าจอหรือมัลแวร์ระดับ kernel ป้องกันไม่ได้

**R8. เปลี่ยน master password**
- ต้องกรอกรหัสเดิมยืนยัน → ตั้งรหัสใหม่ (ตรวจความยาวขั้นต่ำ 12 ตัวอักษร + แสดง strength meter)
- สร้าง salt ใหม่ derive KEK ใหม่ re-wrap DEK เขียนไฟล์ผ่านขั้นตอน atomic + backup ตามปกติ
- DEK ไม่เปลี่ยน หรือ (ตัวเลือกที่ดีกว่า) หมุน DEK ใหม่แล้ว re-encrypt payload ทั้งก้อนด้วย — ให้ Claude Code เลือกและบันทึกเหตุผลใน FORMAT.md; ถ้าไม่หมุน DEK ต้องบอกใน SECURITY.md ว่า recovery key เก่ายังเปิดได้จนกว่าจะสร้างใหม่ และให้ตัวช่วยเสนอสร้าง recovery key ใหม่ต่อท้ายทันที
- มีตัวเลือก "Upgrade KDF strength" ที่ปรับ Argon2 params ให้แรงขึ้นแล้ว re-wrap

**R9. Argon2 params ใน header + format version**
- ตามหัวข้อ 5 — ต้องอ่านค่าจาก header ทุกครั้ง ไม่ hardcode ตอน unlock
- ตอน unlock สำเร็จ ถ้า params ใน header อ่อนกว่าค่าแนะนำปัจจุบัน ให้เสนอ (ไม่บังคับ) upgrade
- Migration framework + test ตามหัวข้อ 5

### 7.3 UI
- Layout: sidebar (All / Favorites / Dev / Staging / Prod / tags) → รายการ → รายละเอียด/แก้ไข แบบแอปโน้ต
- ฝังฟอนต์และ CSS ใน `wwwroot` ทั้งหมด ห้ามโหลดจาก CDN
- ใส่ **Content-Security-Policy** ที่ไม่อนุญาตการเชื่อมต่อภายนอก (`default-src 'self'`; ไม่มี `connect-src` ภายนอก)
- ปิด DevTools / context menu ใน Release build
- ไม่ใช้ `localStorage` เก็บข้อมูลลับใดๆ (ข้อมูลลับอยู่ในหน่วยความจำฝั่ง C# เท่านั้น และส่งให้ UI เท่าที่ต้องแสดง)

## 8. การจัดการหน่วยความจำของข้อมูลลับ

- ใช้ `byte[]` / `Span<byte>` กับ key และ master password เท่าที่ทำได้ แล้ว `ZeroMemory` ทันทีที่เสร็จ
- ข้อจำกัดที่ต้องบันทึกใน SECURITY.md: `string` ใน .NET (รวมรหัสผ่านใน entry ที่ถอดรหัสแล้ว และข้อมูลที่ส่งเข้า WebView) ล้างจากหน่วยความจำให้แน่นอนไม่ได้ GC อาจเก็บสำเนาไว้ชั่วคราว เราลดความเสี่ยงโดยจำกัดเวลาที่ถอดรหัสค้างไว้ (lock เร็ว) ไม่ใช่ขจัดได้หมด
- ปิด crash dump / ไม่ให้ exception handler เขียน state ที่มีข้อมูลลับออกไฟล์
- ใน Release ตั้ง `System.GC` ให้ไม่ต้องทำอะไรพิเศษ แต่ห้าม serialize `VaultData` ลง log หรือ temp file ที่ไม่เข้ารหัสในทุกกรณี

## 9. Setup scripts (สำหรับเพื่อน clone แล้วใช้)

**setup.bat (Windows) / setup.sh (Mac/Linux)** ต้อง:
1. ตรวจว่ามี `dotnet` (เวอร์ชันตรงตามที่ต้องการ) ถ้าไม่มีให้แจ้งพร้อมลิงก์ดาวน์โหลดทางการ แล้วหยุด — **ห้ามดาวน์โหลดและรันตัวติดตั้งอัตโนมัติ**
2. `dotnet restore` + `dotnet publish -c Release` เป็นแอปพร้อมใช้ในโฟลเดอร์ `dist/` (ทางเลือก: self-contained ตาม RID)
3. สร้างทางลัด: Windows → Desktop/Start Menu (`.lnk` ผ่าน PowerShell), macOS → ห่อเป็น `.app` bundle พื้นฐาน หรือสร้างสคริปต์เปิดแอปและบอกวิธีลากไป Applications
4. แจ้งหมายเหตุ: ตอน setup ครั้งแรกต้องมีเน็ตเพื่อ restore NuGet; หลังจากนั้นใช้ offline ได้
5. macOS: ใส่คำอธิบายใน README ว่าแอปไม่ได้ sign ต้องคลิกขวา > Open ครั้งแรก (หรือ `xattr -dr com.apple.quarantine`)
6. สคริปต์ต้อง idempotent (รันซ้ำได้) และไม่แตะ vault ของผู้ใช้เด็ดขาด

## 10. เอกสารที่ต้องสร้าง

- **README.md** (ไทย + อังกฤษสั้นๆ): ติดตั้ง, ใช้งานครั้งแรก, ตำแหน่งไฟล์ vault, วิธี backup เอง, **คำเตือนตัวหนา: ลืม master password และ recovery key = กู้ข้อมูลไม่ได้**
- **SECURITY.md**: threat model, สิ่งที่ป้องกันได้/ไม่ได้ (malware/keylogger, memory ของ .NET string, กล้องถ่ายจอ, clipboard ที่ถูกเฝ้า), ข้อแนะนำให้ผู้เชี่ยวชาญรีวิวก่อนใช้กับรหัส production สำคัญ, วิธีรายงานช่องโหว่
- **docs/FORMAT.md**: รายละเอียดฟอร์แมตไฟล์และ key hierarchy
- **docs/MANUAL_TESTS.md**: เช็กลิสต์ทดสอบด้วยมือบน Windows และ Mac (lock ตอนล็อกจอ/sleep, screenshot protection, clipboard, setup scripts, เปิดสองหน้าต่าง)

## 11. Unit tests (Core ต้องผ่านทั้งหมดก่อนไป UI)

- สร้าง vault → ปิด → เปิดด้วยรหัสถูก ได้ข้อมูลเดิมครบ
- รหัสผิด → ล้มเหลวด้วยข้อความทั่วไป ไม่บอกใบ้ว่าส่วนไหนผิด
- Recovery key ถูก → เปิดได้และตั้ง master password ใหม่ได้; recovery key ผิด → ล้มเหลว
- แก้ไขไบต์ใดๆ ใน payload / header / KDF params → ถอดรหัสต้องล้มเหลว (ทดสอบแบบ flip ทีละบิตหลายตำแหน่ง)
- Nonce ไม่ซ้ำ: บันทึก 1,000 ครั้ง nonce ต้องไม่ซ้ำกันเลย
- ไฟล์ที่ header ระบุ Argon2 memory/iterations เกินขอบเขต → ปฏิเสธก่อน derive
- เปลี่ยน master password → รหัสเก่าเปิดไม่ได้ รหัสใหม่เปิดได้ ข้อมูลครบ
- Atomic write: จำลองความล้มเหลวกลางทาง (inject exception ระหว่างเขียน/verify) → ไฟล์ vault เดิมต้องยังเปิดได้ปกติ
- Backup rotation: ครบจำนวนที่ตั้ง ลบเก่าสุดถูกต้อง ไม่ลบไฟล์อื่น
- File lock: instance ที่สองถูกปฏิเสธ; ปล่อย lock แล้วเปิดใหม่ได้
- Migration: fixture ฟอร์แมตเวอร์ชันเก่า → อัปเกรดสำเร็จ; เวอร์ชันใหม่กว่าที่รู้จัก → ปฏิเสธโดยไม่เขียนทับ
- Clipboard service (mock): ล้างเฉพาะเมื่อค่ายังตรง, ล้างเมื่อ lock/exit
- ไม่มี test ใดพิมพ์ secret ลง output

**Controller tests (`Acorn.Application.Tests`, ใช้ mock ของ interface ใน Abstractions)**
- `UnlockController`: รหัสถูก → `AppState` เป็น Unlocked; รหัสผิด → Result ล้มเหลวด้วยข้อความทั่วไป และ state ยัง Locked
- `VaultController.Lock()`: เรียก clipboard clear + zero key + reset `AppState` ครบ (verify ลำดับการเรียกด้วย mock)
- เมื่อ `ISessionEvents` ยิง screen-lock / sleep / auto-lock timer หมดเวลา → Controller สั่ง lock เหมือนกัน
- `EntryController.CopyPasswordAsync`: เรียก `IClipboardService` พร้อมตั้งตัวจับเวลาล้าง และไม่คืนค่ารหัสให้ View
- `EntryController.RevealSecretAsync`: คืนค่าเฉพาะเมื่อ unlocked และมี timer ซ่อนกลับ
- ViewModel ของรายการ (list) ไม่มีค่ารหัสผ่านจริง
- เปลี่ยน master password / สร้าง recovery key ใหม่ ผ่าน Controller แล้ว state ถูกต้อง
- Architecture tests ตามหัวข้อ 3.1

## 12. Milestones (ทำตามลำดับ)

1. **Core crypto + format**: Kdf, Aead, KeyWrap, RecoveryKey, header/payload, test ข้อ 11 ส่วน crypto
2. **Storage**: AtomicFileWriter, BackupRotator, VaultLock, migration framework + tests
3. **VaultSession**: create / unlock (password, recovery) / lock / save / change password / rotate recovery + tests
4. **Application layer (Controller)**: สร้าง `Acorn.Application` — Abstractions, `AppState`, ViewModels, `UnlockController`, `VaultController` พร้อม Controller tests + architecture tests (ยังไม่มี UI)
5. **View + App shell**: Photino.Blazor เปิดหน้าต่าง, `Program.cs` (DI, CSP, ปิด DevTools ใน Release), Views: Create / Unlock / Recovery ต่อกับ Controller
6. **หน้าหลัก**: `EntryController` + `SettingsController` + Views: sidebar / รายการ / แก้ไข entry, ค้นหา, filter, favorite, password generator, copy SSH command
7. **ความปลอดภัยฝั่งแอป**: implement Platform services — ClipboardService (R5), AutoLock + SessionEvents (R6), ซ่อนรหัส + ScreenProtection (R7), เปลี่ยน master password (R8) ผ่าน Controller + `BackupController` (restore)
8. **Setup scripts + เอกสาร**: setup.bat/sh, README, SECURITY, FORMAT, ARCHITECTURE (อธิบายชั้น MVC และกติกาข้ามชั้น), MANUAL_TESTS, LICENSE, .gitignore
9. **ทบทวนรอบสุดท้าย**: รัน test ทั้งหมด, ไล่หา log/exception ที่อาจรั่ว secret, ไล่หา network call, ตรวจว่า View ไม่แตะ Model/Service โดยตรง, สรุปสิ่งที่ยังไม่ได้ทดสอบบน Windows/Mac จริง

## 13. นอกขอบเขตรอบแรก (รอบ 2)

TOTP generator, แนบไฟล์/เก็บ SSH private key ใน vault, import จาก Bitwarden/KeePass/CSV, หลาย vault ในเครื่องเดียว, ประวัติรหัสผ่านเก่า, GitHub Actions สำหรับ build + release พร้อม SHA-256 checksum, ปุ่มเปิด terminal ต่อ SSH ให้เลย

## 14. เกณฑ์ว่า "เสร็จ" (Definition of Done)

- `dotnet build` และ `dotnet test` ผ่านทั้งหมดบนเครื่องที่ใช้พัฒนา
- ไม่มี network call ใน runtime (ตรวจด้วยการ grep หา `HttpClient`, `WebClient`, `Socket`, URL ภายนอกใน `wwwroot`)
- ทุกข้อ R1–R9 มีทั้งโค้ดและ test (หรือรายการใน MANUAL_TESTS.md ถ้าทดสอบอัตโนมัติไม่ได้)
- โครงสร้าง MVC ถูกต้องตามหัวข้อ 3.1 และ architecture tests ผ่าน: ไม่มี View ที่เรียก Model/Service/crypto โดยตรง
- เอกสารตามหัวข้อ 10 ครบ และข้อจำกัดด้านความปลอดภัยเขียนไว้ตรงๆ ไม่โอ้อวดเกินจริง
