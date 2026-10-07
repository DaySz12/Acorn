using Acorn.Core.Models;
using Acorn.Core.Security;

namespace Acorn.Core.Tests.Security;

public class PasswordGeneratorTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(20)]
    [InlineData(128)]
    public void Generates_requested_length(int length)
    {
        Assert.Equal(length, PasswordGenerator.Generate(new PasswordGeneratorOptions(length)).Length);
    }

    [Fact]
    public void Contains_every_selected_class_and_nothing_else()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = PasswordGenerator.Generate(new PasswordGeneratorOptions(12, Lowercase: true, Uppercase: false, Digits: true, Symbols: false));

            Assert.Contains(password, char.IsAsciiLetterLower);
            Assert.Contains(password, char.IsAsciiDigit);
            Assert.All(password, c => Assert.True(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)));
        }
    }

    [Fact]
    public void Excludes_ambiguous_characters_when_asked()
    {
        var text = string.Concat(Enumerable.Range(0, 200).Select(_ => PasswordGenerator.Generate(new PasswordGeneratorOptions(64))));

        Assert.DoesNotContain(text, c => "Il1O0o".Contains(c));
    }

    [Fact]
    public void Generated_passwords_are_unique()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 1000; i++)
        {
            Assert.True(seen.Add(PasswordGenerator.Generate(new PasswordGeneratorOptions(16))));
        }
    }

    [Fact]
    public void Rejects_invalid_options()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordGenerator.Generate(new PasswordGeneratorOptions(7)));
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordGenerator.Generate(new PasswordGeneratorOptions(129)));
        Assert.Throws<ArgumentException>(() => PasswordGenerator.Generate(new PasswordGeneratorOptions(16, false, false, false, false)));
    }
}

public class SshCommandTests
{
    [Theory]
    [InlineData("admin", "10.0.0.5", 2222, "", "ssh admin@10.0.0.5 -p 2222")]
    [InlineData("", "db.internal", null, "", "ssh db.internal")]
    [InlineData("deploy", "fe80::1", 22, "", "ssh deploy@fe80::1 -p 22")]
    [InlineData("ops", "host", null, "~/.ssh/id_ed25519", "ssh ops@host -i ~/.ssh/id_ed25519")]
    [InlineData("ops", "host", null, "C:\\Users\\me\\My Keys\\id", "ssh ops@host -i 'C:\\Users\\me\\My Keys\\id'")]
    [InlineData("x; rm -rf /", "host$(id)", null, "", "ssh 'x; rm -rf /'@'host$(id)'")]
    [InlineData("o'neil", "h", null, "", "ssh 'o'\\''neil'@h")]
    public void Builds_and_quotes_command(string user, string host, int? port, string key, string expected)
    {
        var entry = new Entry { Username = user, Host = host, Port = port, SshKeyPath = key };

        Assert.Equal(expected, SshCommand.Build(entry));
    }

    [Fact]
    public void No_host_means_no_command()
    {
        Assert.Null(SshCommand.Build(new Entry { Username = "admin" }));
    }
}
