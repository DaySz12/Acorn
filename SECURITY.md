# ความปลอดภัยของ Acorn (Security)

> **Acorn ยังไม่ผ่านการตรวจสอบ (audit) โดยผู้เชี่ยวชาญด้านความปลอดภัยอิสระ**
> ถ้าจะใช้เก็บรหัสของระบบ production ที่สำคัญ ควรให้ผู้เชี่ยวชาญรีวิวโค้ดก่อน
> โดยเฉพาะ `src/Acorn.Core` (crypto, รูปแบบไฟล์, storage)

เอกสารนี้อธิบายว่า Acorn ป้องกันอะไรได้และป้องกันอะไรไม่ได้ ตามที่ระบบทำได้จริง
รายละเอียดรูปแบบไฟล์และ key hierarchy อยู่ใน [docs/FORMAT.md](docs/FORMAT.md)

---

## 1. สิ่งที่ Acorn ป้องกันได้

| สถานการณ์ | วิธีป้องกัน |
|---|---|
| ไฟล์ `vault.acorn` หรือไฟล์ backup หลุดออกไป (เครื่องหาย, sync ขึ้น cloud, คัดลอกผิดที่) | ข้อมูลทั้งหมดรวมทั้งชื่อรายการ, host และ tag อยู่ใน payload ที่เข้ารหัสด้วย AES-256-GCM ผู้ที่ได้ไฟล์ไปต้องเดา master password ผ่าน Argon2id (ค่าเริ่มต้น 64 MiB, 3 รอบ) หรือเดา recovery key ขนาด 200 บิต |
| มีคนแก้ไฟล์ เช่น ลดค่า KDF ใน header ให้เดารหัสง่ายขึ้น | header ทั้งหมดใช้เป็น AAD ของ AES-GCM แก้แม้แต่บิตเดียวก็เปิดไม่ได้ ค่า Argon2 ใน header ถูกตรวจขอบเขตก่อน derive key เสมอ ไฟล์ที่ตั้งค่าให้กินแรมเกินจึงถูกปฏิเสธทันที |
| คนแอบมองจอหรือจับภาพหน้าจอ | รหัสผ่านแสดงเป็น `••••` เสมอ กดดูได้ทีละช่องแล้วซ่อนกลับเอง (ค่าเริ่มต้น 12 วินาที หรือเมื่อเปลี่ยนหน้า) และหน้าต่างถูกตัดออกจาก screenshot/screen share (ดูหัวข้อ 4) |
| ลุกจากเครื่องโดยไม่ได้ล็อก | ล็อกอัตโนมัติเมื่อไม่ได้ใช้งาน (ค่าเริ่มต้น 5 นาที), เมื่อล็อกหน้าจอ, เมื่อเครื่อง sleep, เมื่อ sign out และเมื่อย่อหน้าต่าง (ปิดได้) |
| รหัสผ่านค้างใน clipboard | ล้างหลัง 30 วินาที (ตั้งได้) และล้างทันทีเมื่อล็อกหรือปิดแอป แต่ล้าง**เฉพาะเมื่อ clipboard ยังเป็นค่าที่ Acorn ใส่ไว้** เพื่อไม่ลบสิ่งที่ผู้ใช้คัดลอกมาทีหลัง |
| ไฟล์เสียเพราะไฟดับหรือแอปค้างกลางการบันทึก | เขียนลงไฟล์ `.tmp` → flush ลงดิสก์ → อ่านกลับมาถอดรหัสตรวจ → แทนที่แบบ atomic และสำรองไฟล์เดิมไว้ใน `backups/` ก่อนเขียนทับทุกครั้ง |
| เปิดแอปสองหน้าต่างแล้วเขียนทับกัน | ถือ OS lock บน `vault.acorn.lock` ตลอดอายุแอป ถ้าแอปถูก kill ระบบปฏิบัติการปล่อย lock ให้เอง |

## 2. สิ่งที่ Acorn ป้องกันไม่ได้

- **มัลแวร์, keylogger หรือโปรแกรม remote access บนเครื่อง** ที่ทำงานขณะคุณพิมพ์ master password หรือขณะ vault ปลดล็อกอยู่ จะอ่านข้อมูลได้ทั้งหมด ไม่มีแอปเก็บรหัสตัวไหนป้องกันกรณีนี้ได้
- **ผู้ที่มีสิทธิ์ admin/root** หรือ debug โปรเซสได้
- **หน่วยความจำของ .NET `string`** — รหัสผ่านใน entry ที่ถอดรหัสแล้ว, ค่าที่พิมพ์ในช่องกรอก และค่าที่ส่งไปแสดงใน WebView เป็น `string` ซึ่งล้างจากหน่วยความจำให้แน่นอนไม่ได้ GC อาจเก็บสำเนาไว้ชั่วคราว
  Acorn ลดความเสี่ยงด้วยการเก็บ key ใน `byte[]` แบบ pinned แล้ว `ZeroMemory` ทันทีที่ล็อก และจำกัดเวลาที่ข้อมูลถอดรหัสค้างอยู่ด้วยการล็อกเร็ว แต่**ขจัดความเสี่ยงไม่ได้ทั้งหมด**
  นอกจากนี้ไฟล์ swap/pagefile/hibernation อาจมีสำเนาของหน่วยความจำ จึงควรเปิด full-disk encryption
- **กล้องถ่ายหน้าจอ, capture card หรือมัลแวร์ระดับ kernel/driver** — การกันจับภาพทำได้เฉพาะ API จับภาพมาตรฐานของระบบ
- **แอปที่เฝ้า clipboard** — ระหว่างที่ค่าอยู่ใน clipboard (ก่อนถูกล้าง) แอปอื่นอ่านได้เสมอ บน Windows Acorn ขอให้ Clipboard History และ Cloud Clipboard ไม่เก็บค่า (best effort ขึ้นกับว่าแต่ละแอปเคารพ flag หรือไม่) บน macOS/Linux ไม่มี flag ลักษณะนี้ clipboard manager จึงอาจบันทึกค่าไว้
- **master password ที่อ่อน** — ถ้าไฟล์หลุด ผู้โจมตีเดารหัสแบบ offline ได้ไม่จำกัดจำนวนครั้ง Argon2id ช่วยให้แต่ละครั้งช้าลงเท่านั้น
- **ไฟล์ backup เก่าเปิดได้ด้วยรหัสเก่า** — เมื่อเปลี่ยน master password หรือสร้าง recovery key ใหม่ ไฟล์ใน `backups/` ที่สร้างก่อนหน้ายังเปิดได้ด้วยรหัสหรือ recovery key ชุดเดิม ถ้าเปลี่ยนเพราะสงสัยว่ารหัสหลุด **ให้ลบ backup เก่าเหล่านั้น (และสำเนาที่อื่น) ด้วย**
- **ลืม master password และทำ recovery key หาย** — กู้ข้อมูลไม่ได้
- **ส่วนประกอบนอกแอป** เช่น Windows Update, Microsoft Edge WebView2 Runtime และตัวอัปเดตของมัน หรือ telemetry ของระบบปฏิบัติการ ซึ่งอยู่นอกการควบคุมของ Acorn
- Acorn ไม่จำกัดจำนวนครั้งที่ลองรหัสในหน้าปลดล็อก เพราะผู้โจมตีที่มีไฟล์อยู่แล้วเดาแบบ offline ได้โดยไม่ผ่านแอปอยู่ดี ความช้าในการเดามาจาก Argon2id

## 3. รายละเอียดการออกแบบ

**Crypto** (ใช้ library มาตรฐานเท่านั้น ไม่มี algorithm ที่คิดขึ้นเอง)
- ข้อมูล: AES-256-GCM (`System.Security.Cryptography.AesGcm`) สุ่ม nonce 96 บิตใหม่ทุกครั้งที่บันทึก (มี test บันทึก 1,000 ครั้งแล้วตรวจว่าไม่ซ้ำ)
- master password → KEK: Argon2id (`Konscious.Security.Cryptography.Argon2`) salt 16 bytes ค่า params อ่านจาก header ทุกครั้ง
- recovery key → KEK: HKDF-SHA256 เพราะ recovery key เป็นค่าสุ่ม 200 บิตอยู่แล้ว
- สุ่มทุกอย่างด้วย `RandomNumberGenerator` เทียบค่าลับด้วย `CryptographicOperations.FixedTimeEquals`
- master password ถูก normalize เป็น Unicode NFC ก่อนแปลงเป็น UTF-8 เพื่อให้พิมพ์บน Windows และ macOS แล้วได้ key เดียวกัน

**การเปลี่ยน master password / recovery key** — สร้าง DEK ใหม่ทุกครั้ง แล้วเข้ารหัส payload ทั้งก้อนใหม่ และออก recovery key ชุดใหม่
recovery key เดิมจึงเปิดไฟล์ปัจจุบันไม่ได้ทันที คนที่เคยได้ DEK เก่าไป (จากรหัสที่หลุด) ก็ถอดรหัสไฟล์รุ่นใหม่ไม่ได้ (เหตุผลอยู่ใน FORMAT.md)
ส่วน "Upgrade KDF strength" เปลี่ยนเฉพาะการ wrap DEK ด้วยรหัสผ่าน (DEK เดิม) เพราะไม่ได้เกิดจากการที่รหัสหลุด

**หน่วยความจำ** — DEK และ KEK อยู่ใน `byte[]` แบบ pinned (GC ย้ายหรือคัดลอกไม่ได้) และถูก `ZeroMemory` เมื่อล็อก
master password ถูกแปลงเป็น bytes ครั้งเดียวแล้วล้างทันทีหลัง derive ข้อจำกัดเรื่อง `string` อยู่ในหัวข้อ 2

**UI (WebView)**
- Content-Security-Policy: `default-src 'self'` ไม่อนุญาต origin ภายนอก ไม่มี inline script หรือ inline style ฟอนต์และ CSS ทั้งหมดฝังอยู่ในแอป
- ไม่ใช้ `localStorage` หรือ cookie ค่าลับส่งเข้า WebView เฉพาะตอนผู้ใช้กด "แสดง" ส่วนการคัดลอกทำฝั่ง C# ทั้งหมด ค่าลับจึงไม่ผ่าน JavaScript
- ช่องกรอกปิด autocomplete และไม่ใช้ HTML form submission เพื่อไม่ให้ autofill ของ WebView เก็บค่า
- บน Windows WebView2 ใช้ profile แยกของ Acorn ที่ `%LOCALAPPDATA%\Acorn\WebView2` (ไม่ใช้โฟลเดอร์ `Photino` ที่แอป Photino อื่นใช้ร่วมกัน) โฟลเดอร์นี้มีแค่ cache ของ browser
  ระหว่างพัฒนาได้รันทดสอบ UI ครบทุกขั้น แล้วค้นค่าทดสอบ (รหัส, host, ชื่อรายการ) ในทุกไฟล์ของ profile ทั้งแบบ ASCII และ UTF-16 ไม่พบ ลบโฟลเดอร์นี้ได้ทุกเมื่อที่ปิดแอปแล้ว
- DevTools และ context menu ปิดใน Release build ส่วน Release build จะลบ environment variable `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` และ `WEBVIEW2_PIPE_FOR_SCRIPT_DEBUGGER` ตอนเริ่มโปรแกรม เพื่อกันโปรเซสอื่นเปิด remote debugging เข้ามาอ่านหน้าจอ

**Network** — โค้ดของ Acorn ไม่มี `HttpClient`, `WebClient` หรือ socket (มี architecture test ตรวจ) และไม่มี telemetry หรือ update checker
บน Windows เปิด WebView2 ด้วย flag ที่ปิด background networking, SmartScreen, component updater และ crash reporter

**Log และ crash**
- Release build ไม่มี log provider เลย และตั้ง log verbosity ของ Photino เป็น 0 (ค่าเริ่มต้นของ Photino พิมพ์ทุก web message ซึ่งรวมหน้าจอที่ render แล้ว)
- exception message ไม่มีค่าลับ error จาก JSON parser ถูกแปลงเป็นข้อความทั่วไป
- Release build ปิด core dump บน macOS/Linux (`setrlimit`) และตั้ง `PR_SET_DUMPABLE=0` บน Linux
- บน Windows ถ้าระบบตั้ง Windows Error Reporting LocalDumps ไว้ dump ยังอาจถูกเขียนได้ ให้ตรวจตามนโยบายของเครื่อง

**ไฟล์** — บน macOS/Linux ไฟล์เป็น `600` โฟลเดอร์เป็น `700` บน Windows ใช้ ACL ของ user profile
ตัวแปร `ACORN_DATA_DIR` เปลี่ยนตำแหน่ง vault ได้ (สำหรับทดสอบ) ส่วน `ACORN_DEV_ALLOW_CAPTURE` มีผลเฉพาะ Debug build เท่านั้น

## 4. ข้อจำกัดตามระบบปฏิบัติการ

**Windows** (ทดสอบบน Windows 11 ระหว่างพัฒนา — ดู [docs/MANUAL_TESTS.md](docs/MANUAL_TESTS.md))
- กันจับภาพ: `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` (ต้องเป็น Windows 10 2004 ขึ้นไป) ถ้าไม่รองรับจะ fallback เป็น `WDA_MONITOR`
- Clipboard: ใส่ format `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory=0`, `CanUploadToCloudClipboard=0`
- ล็อกตาม `SessionSwitch` (lock / disconnect / logoff), `PowerModeChanged` (Suspend) และ `SessionEnding`

**macOS** — **ยังไม่ได้ทดสอบบนเครื่อง Mac จริง**
- กันจับภาพ: `NSWindow.sharingType = NSWindowSharingNone` แบบ best effort ซึ่ง capture API รุ่นใหม่ของ macOS อาจไม่เคารพค่านี้
- ล็อก: ฟัง `com.apple.screenIsLocked`, `NSWorkspaceWillSleepNotification`, `NSWorkspaceScreensDidSleepNotification` และ `NSWorkspaceSessionDidResignActiveNotification` ผ่าน Objective-C runtime
  ถ้า interop ใช้ไม่ได้ จะเหลือ auto-lock เมื่อไม่ได้ใช้งานเป็นตัวป้องกัน
- แอปไม่ได้ sign หรือ notarize ด้วย Apple Developer ID (setup.sh ทำ ad-hoc signature ให้)

**Linux** (ไม่ใช่เป้าหมายหลัก) — ไม่มีการกันจับภาพ และไม่ตรวจจับการล็อกจอหรือ sleep มีเฉพาะล็อกเมื่อย่อหน้าต่างและ auto-lock เมื่อไม่ได้ใช้งาน

## 5. คำแนะนำการใช้งาน

- master password: วลีหลายคำ (4–6 คำ) หรือยาว 16 ตัวอักษรขึ้นไป และไม่ใช้ซ้ำกับที่อื่น
- recovery key: เก็บบนกระดาษหรือในที่ปลอดภัยนอกคอมพิวเตอร์ ไม่ถ่ายรูปเก็บไว้ในเครื่องเดียวกัน
- เปิด full-disk encryption (BitLocker / FileVault) และอัปเดตระบบปฏิบัติการกับ WebView2 สม่ำเสมอ
- ถ้าความแรงของ KDF ต่ำกว่าค่าแนะนำ แอปจะเสนอให้อัปเกรด (หน้า Settings)

## 6. รายงานช่องโหว่

- **อย่าเปิด issue สาธารณะ** สำหรับช่องโหว่
- ใช้ GitHub private vulnerability reporting ของ repository นี้ (แท็บ **Security → Report a vulnerability**) หรือติดต่อเจ้าของ repository แบบส่วนตัว
- ระบุ commit หรือเวอร์ชัน, ระบบปฏิบัติการ และขั้นตอนทำซ้ำ **อย่าแนบไฟล์ vault จริงหรือรหัสผ่านจริง**
