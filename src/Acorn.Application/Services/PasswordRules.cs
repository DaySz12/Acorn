using Acorn.Application.ViewModels;
using Acorn.Core.Security;

namespace Acorn.Application.Services;

/// <summary>Validation and strength display for new master passwords (creation, recovery, change).</summary>
internal static class PasswordRules
{
    private static readonly string[] Labels = ["อ่อนมาก", "อ่อน", "พอใช้", "แข็งแรง", "แข็งแรงมาก"];

    /// <summary>Returns an error message, or null when the password is acceptable.</summary>
    public static string? ValidateNew(string? password, string? confirmation)
    {
        if (string.IsNullOrEmpty(password))
        {
            return Messages.PasswordRequired;
        }
        if (!PasswordPolicy.MeetsMinimumLength(password))
        {
            return Messages.PasswordTooShort;
        }
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            return Messages.PasswordMismatch;
        }
        var strength = PasswordStrength.Evaluate(password);
        if (strength.Score == 0 || strength.Warnings.Contains(PasswordWarning.CommonPassword))
        {
            return Messages.PasswordTooWeak;
        }
        return null;
    }

    public static PasswordStrengthViewModel Describe(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return PasswordStrengthViewModel.Empty;
        }

        var result = PasswordStrength.Evaluate(password);
        var hints = result.Warnings.Select(w => w switch
        {
            PasswordWarning.TooShort => $"ต้องมีอย่างน้อย {PasswordPolicy.MinLength} ตัวอักษร",
            PasswordWarning.CommonPassword => "เป็นรหัสผ่านที่คนใช้กันมาก เดาง่าย",
            PasswordWarning.RepeatedCharacters => "มีตัวอักษรซ้ำกันมาก",
            PasswordWarning.Sequence => "มีลำดับต่อเนื่อง เช่น abc หรือ 123",
            PasswordWarning.SingleCharacterClass => "ลองผสมตัวพิมพ์ใหญ่ ตัวเลข สัญลักษณ์ หรือใช้หลายคำ",
            _ => "",
        }).Where(h => h.Length > 0).ToList();

        return new PasswordStrengthViewModel(
            result.Score,
            Labels[result.Score],
            (result.Score + 1) * 20,
            hints,
            PasswordPolicy.MeetsMinimumLength(password));
    }
}
