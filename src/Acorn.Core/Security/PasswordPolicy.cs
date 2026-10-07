namespace Acorn.Core.Security;

/// <summary>Rules for master passwords (SPEC R8).</summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;

    /// <summary>Length in Unicode scalar values, so Thai and emoji count as typed.</summary>
    public static int Length(string password) => password.EnumerateRunes().Count();

    public static bool MeetsMinimumLength(string password) => Length(password) >= MinLength;

    public static void EnsureAcceptable(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (!MeetsMinimumLength(password))
        {
            throw new ArgumentException($"The master password must be at least {MinLength} characters.", nameof(password));
        }
    }
}
