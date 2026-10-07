using System.Text.RegularExpressions;
using Acorn.Core.Crypto;

namespace Acorn.Core.Tests.Crypto;

public partial class RecoveryKeyTests
{
    [GeneratedRegex("^[0-9A-HJKMNP-TV-Z]{5}(-[0-9A-HJKMNP-TV-Z]{5}){7}$")]
    private static partial Regex DisplayFormat();

    [Fact]
    public void Display_string_is_eight_groups_of_unambiguous_characters()
    {
        using var key = RecoveryKey.Generate();

        Assert.Matches(DisplayFormat(), key.ToDisplayString());
    }

    [Fact]
    public void Parse_round_trips_display_string()
    {
        using var key = RecoveryKey.Generate();

        Assert.True(RecoveryKey.TryParse(key.ToDisplayString(), out var parsed));
        using (parsed)
        {
            Assert.Equal(key.Bytes.ToArray(), parsed.Bytes.ToArray());
        }
    }

    [Fact]
    public void Parse_ignores_case_whitespace_and_maps_ambiguous_letters()
    {
        using var key = RecoveryKey.Generate();
        var display = key.ToDisplayString();
        var typed = display.ToLowerInvariant().Replace("-", " ").Replace('0', 'o').Replace('1', 'l');

        Assert.True(RecoveryKey.TryParse(typed, out var parsed));
        using (parsed)
        {
            Assert.Equal(key.Bytes.ToArray(), parsed.Bytes.ToArray());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCDE-FGHJK")]
    [InlineData("UUUUU-UUUUU-UUUUU-UUUUU-UUUUU-UUUUU-UUUUU-UUUUU")]
    [InlineData("ABCDE-FGHJK-MNPQR-STVWX-YZ012-34567-89ABC-DEFGH-J")]
    [InlineData("ABCDE-FGHJK-MNPQR-STVWX-YZ012-34567-89ABC-DEFG!")]
    public void Parse_rejects_malformed_input(string input)
    {
        Assert.False(RecoveryKey.TryParse(input, out _));
    }

    [Fact]
    public void Generated_keys_are_unique()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 1000; i++)
        {
            using var key = RecoveryKey.Generate();
            Assert.True(seen.Add(key.ToDisplayString()));
        }
    }

    [Fact]
    public void Disposed_key_cannot_be_used()
    {
        var key = RecoveryKey.Generate();
        key.Dispose();

        Assert.Throws<ObjectDisposedException>(() => key.ToDisplayString());
    }
}
