using System.Text;

namespace Acorn.Core.Security;

public enum PasswordWarning
{
    TooShort,
    CommonPassword,
    RepeatedCharacters,
    Sequence,
    SingleCharacterClass,
}

/// <param name="Score">0 (very weak) to 4 (very strong).</param>
public sealed record PasswordStrengthResult(int Score, double EntropyBits, IReadOnlyList<PasswordWarning> Warnings);

/// <summary>
/// Rough, offline strength estimate for the strength meter. It is a heuristic (character pool
/// size × length, with penalties for common patterns), not a guarantee.
/// </summary>
public static class PasswordStrength
{
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password123", "passw0rd", "123456", "12345678", "123456789", "1234567890",
        "qwerty", "qwertyuiop", "abc123", "letmein", "welcome", "admin", "administrator", "iloveyou",
        "monkey", "dragon", "football", "baseball", "sunshine", "princess", "master", "superman",
        "trustno1", "changeme", "default", "root", "toor", "p@ssw0rd", "p@ssword", "qwerty123",
        "1q2w3e4r", "zaq12wsx", "111111", "000000", "asdfghjkl", "correcthorsebatterystaple",
    };

    public static PasswordStrengthResult Evaluate(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var warnings = new List<PasswordWarning>();
        var runes = password.EnumerateRunes().ToList();
        if (runes.Count == 0)
        {
            return new PasswordStrengthResult(0, 0, [PasswordWarning.TooShort]);
        }

        bool lower = false, upper = false, digit = false, symbol = false, other = false;
        foreach (var rune in runes)
        {
            if (rune.Value < 128)
            {
                var c = (char)rune.Value;
                if (char.IsAsciiLetterLower(c)) lower = true;
                else if (char.IsAsciiLetterUpper(c)) upper = true;
                else if (char.IsAsciiDigit(c)) digit = true;
                else symbol = true;
            }
            else
            {
                other = true;
            }
        }

        var pool = (lower ? 26 : 0) + (upper ? 26 : 0) + (digit ? 10 : 0) + (symbol ? 33 : 0) + (other ? 64 : 0);
        var classes = new[] { lower, upper, digit, symbol, other }.Count(x => x);

        // Count only "fresh" characters: repeats and +1/-1 runs add little entropy.
        double effectiveLength = 0;
        int repeats = 0, sequences = 0;
        for (var i = 0; i < runes.Count; i++)
        {
            if (i > 0 && runes[i] == runes[i - 1])
            {
                repeats++;
                effectiveLength += 0.25;
            }
            else if (i > 0 && Math.Abs(runes[i].Value - runes[i - 1].Value) == 1)
            {
                sequences++;
                effectiveLength += 0.35;
            }
            else
            {
                effectiveLength += 1;
            }
        }

        var entropy = effectiveLength * Math.Log2(Math.Max(pool, 2));

        if (runes.Count < PasswordPolicy.MinLength) warnings.Add(PasswordWarning.TooShort);
        if (repeats >= Math.Max(2, runes.Count / 4)) warnings.Add(PasswordWarning.RepeatedCharacters);
        if (sequences >= Math.Max(2, runes.Count / 4)) warnings.Add(PasswordWarning.Sequence);
        if (classes == 1) warnings.Add(PasswordWarning.SingleCharacterClass);
        if (IsCommon(password))
        {
            warnings.Add(PasswordWarning.CommonPassword);
            entropy = Math.Min(entropy, 10);
        }

        var score = entropy switch
        {
            < 28 => 0,
            < 40 => 1,
            < 60 => 2,
            < 80 => 3,
            _ => 4,
        };
        if (runes.Count < PasswordPolicy.MinLength)
        {
            score = Math.Min(score, 1);
        }

        return new PasswordStrengthResult(score, Math.Round(entropy, 1), warnings);
    }

    private static bool IsCommon(string password)
    {
        // Strip separators and trailing digits/symbols people append ("Password123!").
        var core = new StringBuilder();
        foreach (var c in password)
        {
            if (!char.IsWhiteSpace(c) && c is not '-' and not '_' and not '.')
            {
                core.Append(c);
            }
        }
        var text = core.ToString();
        var trimmed = text.TrimEnd("0123456789!@#$%^&*?".ToCharArray());
        return CommonPasswords.Contains(text) || (trimmed.Length > 0 && CommonPasswords.Contains(trimmed));
    }
}
