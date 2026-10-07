namespace Acorn.Application;

/// <summary>User-facing texts. None of them may include secrets or exception details.</summary>
internal static class Messages
{
    public const string UnlockFailed = "ปลดล็อกไม่สำเร็จ: รหัสผ่านไม่ถูกต้อง หรือไฟล์ vault ถูกแก้ไข";
    public const string RecoveryFailed = "ปลดล็อกไม่สำเร็จ: Recovery key ไม่ถูกต้อง หรือไฟล์ vault ถูกแก้ไข";
    public const string CurrentPasswordWrong = "รหัสผ่านปัจจุบันไม่ถูกต้อง";
    public const string VaultNewer = "ไฟล์ vault นี้สร้างจาก Acorn เวอร์ชันใหม่กว่า กรุณาอัปเดตแอปก่อนเปิด (ไฟล์ไม่ถูกแก้ไข)";
    public const string VaultCorrupt = "ไฟล์ vault เสียหายหรือไม่ใช่ไฟล์ของ Acorn ลองกู้คืนจาก backup";
    public const string VaultMissing = "ไม่พบไฟล์ vault";
    public const string VaultAlreadyExists = "มี vault อยู่แล้วในเครื่องนี้";
    public const string VaultInUse = "มีอีกหน้าต่างหรือโปรเซสกำลังใช้ vault นี้อยู่ ปิดหน้าต่างนั้นก่อนแล้วลองใหม่";
    public const string VaultLocked = "Vault ถูกล็อกแล้ว";
    public const string StorageError = "อ่านหรือเขียนไฟล์ไม่สำเร็จ ข้อมูลเดิมยังอยู่ครบ";
    public const string Unexpected = "เกิดข้อผิดพลาดที่ไม่คาดคิด ข้อมูลเดิมยังไม่ถูกแก้ไข";
    public const string Busy = "กำลังทำงานอยู่ กรุณารอสักครู่";

    public const string PasswordRequired = "กรุณากรอกรหัสผ่าน";
    public const string PasswordTooShort = "Master password ต้องมีอย่างน้อย 12 ตัวอักษร";
    public const string PasswordMismatch = "รหัสผ่านทั้งสองช่องไม่ตรงกัน";
    public const string PasswordTooWeak = "รหัสผ่านนี้เดาง่ายเกินไป ลองใช้วลีหลายคำ หรือผสมตัวอักษรให้หลากหลายขึ้น";
    public const string PasswordSameAsCurrent = "รหัสผ่านใหม่ต้องไม่ซ้ำกับรหัสเดิม";
    public const string RecoveryKeyRequired = "กรุณากรอก recovery key";
    public const string RecoveryConfirmMismatch = "กลุ่มที่พิมพ์ไม่ตรงกับ recovery key ที่แสดง ลองตรวจอีกครั้ง";
    public const string NoPendingRecoveryKey = "ไม่มี recovery key ที่รอการยืนยัน";
    public const string BackupNotFound = "ไม่พบไฟล์ backup ที่เลือก";

    public const string NothingToEdit = "ไม่มีรายการที่กำลังแก้ไข";
    public const string EntryNotFound = "ไม่พบรายการนี้";
    public const string EntryNameRequired = "กรุณาตั้งชื่อรายการ";
    public const string EntryTypeInvalid = "ประเภทรายการไม่ถูกต้อง";
    public const string PortInvalid = "Port ต้องเป็นตัวเลข 1–65535";
    public const string CustomFieldLabelRequired = "ฟิลด์เพิ่มเติมที่มีค่าต้องมีชื่อฟิลด์";
    public const string NothingToCopy = "ไม่มีข้อมูลให้คัดลอก";
    public const string CopyFailed = "คัดลอกไม่สำเร็จ ลองใหม่อีกครั้ง";
    public const string NothingToReveal = "ไม่มีค่าที่จะแสดง";
    public const string GeneratorNeedsCharacterSet = "เลือกชุดตัวอักษรอย่างน้อยหนึ่งชุด";

    public const string SettingsSaved = "บันทึกการตั้งค่าแล้ว";
    public const string SettingsInvalid = "ค่าการตั้งค่าไม่ถูกต้อง";
    public const string RangeError = "{0}: ต้องอยู่ระหว่าง {1}–{2} {3}";
    public const string EnvironmentsInvalid = "ชื่อ environment ใช้ได้เฉพาะ a-z 0-9 - _ (ยาวไม่เกิน 24 ตัว สูงสุด 12 ชื่อ)";

    public const string LockedManual = "ล็อกแล้ว";
    public const string LockedIdle = "ล็อกอัตโนมัติเพราะไม่ได้ใช้งาน";
    public const string LockedScreen = "ล็อกเพราะหน้าจอคอมพิวเตอร์ถูกล็อก";
    public const string LockedSuspend = "ล็อกเพราะเครื่องเข้าสู่โหมด sleep";
    public const string LockedMinimized = "ล็อกเพราะย่อหน้าต่าง";
    public const string LockedSessionEnding = "ล็อกเพราะกำลังออกจากระบบ";
    public const string RecoveryKeyLostOnLock = "ล็อกก่อนยืนยันว่าจด recovery key แล้ว — หลังปลดล็อกให้ไปที่ ตั้งค่า แล้วสร้าง recovery key ใหม่";
    public const string LockedRestored = "กู้คืนจาก backup แล้ว ปลดล็อกด้วย master password ของเวอร์ชันนั้น";

    public const string Migrated = "อัปเกรดไฟล์ vault เป็นรูปแบบล่าสุดแล้ว (ไฟล์เดิมสำรองไว้ใน backups)";
    public const string KdfUpgraded = "เพิ่มความแข็งแรงของการเข้ารหัส master password แล้ว";
    public const string PasswordChanged = "เปลี่ยน master password แล้ว จด recovery key ชุดใหม่ด้านล่าง (ชุดเก่าใช้ไม่ได้แล้ว)";
    public const string RecoveryKeyRegenerated = "สร้าง recovery key ใหม่แล้ว ชุดเก่าใช้ไม่ได้แล้ว";
    public const string Recovered = "ตั้ง master password ใหม่แล้ว จด recovery key ชุดใหม่ด้านล่าง (ชุดเดิมใช้ไม่ได้แล้ว)";
    public const string Copied = "คัดลอกแล้ว";
    public const string CopiedSecret = "คัดลอกแล้ว จะล้าง clipboard ใน {0} วินาที";

    public static string LeftoverTemp(string fileName) =>
        $"พบไฟล์บันทึกที่ไม่สมบูรณ์จากการปิดแอปครั้งก่อน ย้ายไปเก็บไว้ที่ backups/{fileName} แล้ว (vault หลักไม่ได้รับผลกระทบ)";
}
