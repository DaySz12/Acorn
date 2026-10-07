using System.Security.Cryptography;

namespace Acorn.Core.Security;

public sealed record PasswordGeneratorOptions(
    int Length = 20,
    bool Lowercase = true,
    bool Uppercase = true,
    bool Digits = true,
    bool Symbols = true,
    bool ExcludeAmbiguous = true)
{
    public const int MinLength = 8;
    public const int MaxLength = 128;
}

/// <summary>Random passwords from <see cref="RandomNumberGenerator"/> only (SPEC 7.1 item 6).</summary>
public static class PasswordGenerator
{
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digit = "0123456789";
    private const string Symbol = "!@#$%^&*()-_=+[]{};:,.?/~";
    private const string Ambiguous = "Il1O0o";

    /// <summary>Contains at least one character from every selected class, in random positions.</summary>
    public static string Generate(PasswordGeneratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Length, PasswordGeneratorOptions.MinLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Length, PasswordGeneratorOptions.MaxLength);

        var sets = new List<string>();
        if (options.Lowercase) sets.Add(Lower);
        if (options.Uppercase) sets.Add(Upper);
        if (options.Digits) sets.Add(Digit);
        if (options.Symbols) sets.Add(Symbol);
        if (options.ExcludeAmbiguous)
        {
            sets = sets.Select(s => new string(s.Where(c => !Ambiguous.Contains(c)).ToArray())).ToList();
        }
        if (sets.Count == 0)
        {
            throw new ArgumentException("Select at least one character set.", nameof(options));
        }

        var all = string.Concat(sets);
        var chars = new char[options.Length];
        try
        {
            var i = 0;
            foreach (var set in sets)
            {
                chars[i++] = set[RandomNumberGenerator.GetInt32(set.Length)];
            }
            for (; i < chars.Length; i++)
            {
                chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
            }
            // Fisher–Yates so the guaranteed characters are not always at the front.
            for (var j = chars.Length - 1; j > 0; j--)
            {
                var k = RandomNumberGenerator.GetInt32(j + 1);
                (chars[j], chars[k]) = (chars[k], chars[j]);
            }
            return new string(chars);
        }
        finally
        {
            Array.Clear(chars);
        }
    }
}
