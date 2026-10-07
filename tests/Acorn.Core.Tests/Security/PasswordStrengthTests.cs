using Acorn.Core.Security;

namespace Acorn.Core.Tests.Security;

public class PasswordStrengthTests
{
    [Theory]
    [InlineData("password123")]
    [InlineData("Password123!")]
    [InlineData("qwertyuiop")]
    public void Common_passwords_score_very_low(string password)
    {
        var result = PasswordStrength.Evaluate(password);

        Assert.True(result.Score <= 1);
        Assert.Contains(PasswordWarning.CommonPassword, result.Warnings);
    }

    [Fact]
    public void Short_passwords_are_capped_and_warned()
    {
        var result = PasswordStrength.Evaluate("Xk9#qL2!");

        Assert.True(result.Score <= 1);
        Assert.Contains(PasswordWarning.TooShort, result.Warnings);
    }

    [Theory]
    [InlineData("aaaaaaaaaaaaaaaa")]
    [InlineData("abcdefghijklmnop")]
    public void Repeats_and_sequences_are_penalised(string password)
    {
        Assert.True(PasswordStrength.Evaluate(password).Score <= 1);
    }

    [Theory]
    [InlineData("v8#Kq2!mZr7&Lp4@wX")]
    [InlineData("river-tangle-oyster-maple-quiet")]
    [InlineData("ช้างกินกล้วยใต้ต้นมะม่วง2026")]
    public void Long_varied_passwords_score_well(string password)
    {
        Assert.True(PasswordStrength.Evaluate(password).Score >= 3);
    }

    [Fact]
    public void Policy_counts_unicode_scalar_values()
    {
        Assert.True(PasswordPolicy.MeetsMinimumLength("รหัสผ่านยาวพอแล้ว"));
        Assert.False(PasswordPolicy.MeetsMinimumLength("elevenchars"));
        Assert.True(PasswordPolicy.MeetsMinimumLength("twelve-chars"));
    }
}
