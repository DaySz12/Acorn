namespace Acorn.Application.ViewModels;

/// <summary>Strength meter output. The estimate comes from the Model; the view only draws it.</summary>
public sealed record PasswordStrengthViewModel(int Score, string Label, int Percent, IReadOnlyList<string> Hints, bool MeetsMinimumLength)
{
    public static PasswordStrengthViewModel Empty { get; } = new(0, "", 0, [], false);
}

/// <summary>
/// A recovery key being shown once. The user must type back the groups at
/// <see cref="ConfirmGroupIndexes"/> before continuing (SPEC 4.3).
/// </summary>
public sealed record RecoveryKeyViewModel(
    string Formatted,
    IReadOnlyList<string> Groups,
    IReadOnlyList<int> ConfirmGroupIndexes,
    Screen ReturnTo);

public enum KdfPreset
{
    Recommended,
    Strong,
}
