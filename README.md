# Acorn 🌰

แอปเก็บ username / password / IP ของเซิร์ฟเวอร์และบัญชีต่างๆ แบบ **offline 100%** บน Windows และ macOS
ไม่มี server ไม่เปิด port ไม่ส่งข้อมูลออกนอกเครื่อง ข้อมูลทั้งหมดเข้ารหัสด้วย master password ของคุณเอง

> ⚠️ **ถ้าลืม master password และทำ recovery key หาย จะกู้ข้อมูลไม่ได้เลย**
> ไม่มีใคร (รวมถึงผู้พัฒนา) ถอดรหัสให้ได้ จด recovery key เก็บไว้นอกคอมพิวเตอร์เสมอ

[English summary](#english)

---

## ทำอะไรได้บ้าง

- เก็บรายการ 3 แบบ: เซิร์ฟเวอร์ (host, port, user, password, SSH key path), บัญชีล็อกอิน และโน้ต
- จัดกลุ่มตาม environment (dev / staging / prod) และ tag, ติดดาวรายการโปรด, ค้นหาเร็วด้วย **Ctrl+K** (Mac: **⌘K**)
- ปุ่มคัดลอก host / username / password และคัดลอกคำสั่ง `ssh user@host -p port` ได้ในคลิกเดียว
- สุ่มรหัสผ่าน (ปรับความยาวและชุดตัวอักษรได้)
- ธีมสว่าง / มืด / ตามระบบ

ความปลอดภัย (รายละเอียดใน [SECURITY.md](SECURITY.md)):

- เข้ารหัสด้วย AES-256-GCM, master password ผ่าน Argon2id
- รหัสผ่านแสดงเป็น `••••` เสมอ กดดูได้ทีละช่องแล้วซ่อนกลับเอง
- คัดลอกค่าลับแล้ว clipboard ถูกล้างอัตโนมัติ (ค่าเริ่มต้น 30 วินาที) และล้างทันทีเมื่อล็อกหรือปิดแอป
- ล็อกอัตโนมัติเมื่อไม่ได้ใช้งาน, เมื่อล็อกหน้าจอ, เมื่อเครื่อง sleep และเมื่อย่อหน้าต่าง (ปิดได้)
- กันการจับภาพหน้าจอ / แชร์หน้าจอ (Windows; macOS แบบ best effort)
- สำรองไฟล์อัตโนมัติก่อนบันทึกทุกครั้ง และเขียนไฟล์แบบ atomic (ไฟล์ไม่เสียแม้ไฟดับกลางทาง)

## ติดตั้ง

ต้องมี **.NET SDK 10** ขึ้นไป ดาวน์โหลดจากเว็บทางการ: <https://dotnet.microsoft.com/download/dotnet/10.0>

```bash
git clone <url ของ repo นี้> acorn
cd acorn
```

**Windows** — ดับเบิลคลิก `setup.bat` หรือรันใน terminal:

```bat
setup.bat
```

ได้แอปที่ `dist\win-x64\Acorn.exe` พร้อมทางลัดบน Desktop และ Start Menu
(ใช้ `setup.bat --no-shortcuts` ถ้าไม่ต้องการทางลัด) Windows 10/11 ต้องมี Microsoft Edge WebView2 Runtime ซึ่งปกติติดตั้งมากับระบบแล้ว

**macOS** — ใน Terminal:

```bash
chmod +x setup.sh && ./setup.sh
```

ได้ `dist/Acorn.app` ลากไปไว้ใน Applications ได้เลย
แอปนี้ **ไม่ได้ sign ด้วย Apple Developer ID** ครั้งแรกให้ **คลิกขวาที่แอป > Open** แล้วกด Open อีกครั้ง
หรือรัน `xattr -dr com.apple.quarantine /Applications/Acorn.app`

**Linux** (ไม่บังคับ) — `./setup.sh` เหมือน macOS ต้องมี WebKitGTK (`libwebkit2gtk-4.1`)

ตอน setup ครั้งแรกต้องมีอินเทอร์เน็ตเพื่อดาวน์โหลด NuGet packages หลังจากนั้นใช้งาน offline ได้ทั้งหมด
สคริปต์รันซ้ำได้ และไม่แตะไฟล์ vault ของคุณ

## ใช้งานครั้งแรก

1. เปิด Acorn แล้วตั้ง **master password** (อย่างน้อย 12 ตัวอักษร แนะนำเป็นวลีหลายคำ)
2. Acorn จะแสดง **recovery key** 8 กลุ่ม **เพียงครั้งเดียว** ให้จดหรือพิมพ์เก็บไว้นอกคอมพิวเตอร์
3. พิมพ์กลุ่มที่ระบบขอกลับเข้าไปเพื่อยืนยันว่าจดแล้ว
4. กด **เพิ่ม** เพื่อสร้างรายการแรก

ลืม master password? ที่หน้าปลดล็อกกด "ใช้ recovery key" แล้วตั้งรหัสใหม่ (จะได้ recovery key ชุดใหม่ ชุดเดิมใช้ไม่ได้อีก)

## ไฟล์ vault อยู่ที่ไหน

| OS | ตำแหน่ง |
|---|---|
| Windows | `%APPDATA%\Acorn\` |
| macOS | `~/Library/Application Support/Acorn/` |
| Linux | `$XDG_DATA_HOME/Acorn` หรือ `~/.local/share/Acorn/` |

ในโฟลเดอร์นี้มี

- `vault.acorn` — ข้อมูลทั้งหมด (เข้ารหัสแล้ว)
- `backups/vault-YYYYMMDD-HHmmss.acorn.bak` — สำรองอัตโนมัติ (เวลาเป็น UTC) เก็บล่าสุด 10 ไฟล์ ตั้งค่าได้ 5–50
- `vault.acorn.lock` — ใช้กันเปิดแอปซ้อนกันสองหน้าต่าง (ลบได้เมื่อปิดแอปแล้ว)

บน Windows มีโฟลเดอร์ cache ของหน้าต่างแอป (WebView2) อีกที่ `%LOCALAPPDATA%\Acorn\WebView2` ซึ่งไม่มีข้อมูลใน vault และลบได้เมื่อปิดแอป

ถ้าต้องการเก็บ vault ไว้ที่อื่น (เช่นทดสอบ) ตั้ง environment variable `ACORN_DATA_DIR` เป็นโฟลเดอร์ที่ต้องการก่อนเปิดแอป

## สำรองข้อมูลด้วยตัวเอง

1. **ปิด Acorn ก่อน**
2. คัดลอก `vault.acorn` (และโฟลเดอร์ `backups` ถ้าต้องการ) ไปเก็บที่ USB หรือที่เก็บอื่น
3. ไฟล์เข้ารหัสอยู่แล้ว แต่ใครได้ไฟล์ไปก็พยายามเดารหัสแบบ offline ได้ จึงควรใช้ master password ที่แข็งแรง
4. เก็บ recovery key แยกจากไฟล์ vault

กู้คืน: เปิด Acorn > หน้าปลดล็อกหรือหน้าตั้งค่า > "กู้คืนจาก backup" แล้วปลดล็อกด้วย master password **ของเวอร์ชันนั้น**
(หรือปิดแอปแล้วคัดลอกไฟล์ที่สำรองไว้กลับมาเป็น `vault.acorn`)

## สำหรับนักพัฒนา

```bash
dotnet build Acorn.sln
dotnet test --solution Acorn.sln
dotnet run --project src/Acorn.App
```

- โครงสร้างและกติกา MVC: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- รูปแบบไฟล์และ key hierarchy: [docs/FORMAT.md](docs/FORMAT.md)
- เช็กลิสต์ทดสอบด้วยมือ: [docs/MANUAL_TESTS.md](docs/MANUAL_TESTS.md)
- วัดความเร็ว Argon2 บนเครื่อง: `dotnet run -c Release --project tools/Acorn.KdfBenchmark`
- Debug build เท่านั้น: `ACORN_DEV_ALLOW_CAPTURE=1` ปิดการกันจับภาพหน้าจอชั่วคราว (เพื่อถ่ายภาพหน้าจอประกอบเอกสาร)

---

## English

**Acorn** is an offline desktop vault for server credentials and logins (Windows and macOS; Linux works but is not a target).
It never opens a port or makes network calls. Data is encrypted with AES-256-GCM under a key protected by your master
password (Argon2id) and by a one-time recovery key.

> **If you forget your master password and lose your recovery key, your data cannot be recovered. By anyone.**

**Install:** install the .NET SDK 10+ from <https://dotnet.microsoft.com/download/dotnet/10.0>, clone this repository and run
`setup.bat` (Windows) or `./setup.sh` (macOS/Linux). The app is published to `dist/`. Internet is needed only for the first
package restore. On macOS the app is unsigned: right-click > Open on first launch, or `xattr -dr com.apple.quarantine`.

**Vault location:** `%APPDATA%\Acorn` (Windows), `~/Library/Application Support/Acorn` (macOS),
`~/.local/share/Acorn` (Linux). Automatic backups are kept in `backups/` next to the vault.
To back up manually, close Acorn and copy `vault.acorn`. Keep the recovery key somewhere other than the computer.

Security model and limitations: [SECURITY.md](SECURITY.md). File format: [docs/FORMAT.md](docs/FORMAT.md).
Architecture: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). License: MIT.
