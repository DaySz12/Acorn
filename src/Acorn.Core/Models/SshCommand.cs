using System.Text;
using System.Text.RegularExpressions;

namespace Acorn.Core.Models;

/// <summary>Builds "ssh user@host -p port" for an entry, quoting anything a shell could interpret.</summary>
public static partial class SshCommand
{
    [GeneratedRegex(@"^[A-Za-z0-9._\-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeUser();

    [GeneratedRegex(@"^[A-Za-z0-9._\-:%\[\]]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeHost();

    [GeneratedRegex(@"^[A-Za-z0-9._\-/~:\\]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafePath();

    /// <summary>Returns null when the entry has no host.</summary>
    public static string? Build(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var host = entry.Host.Trim();
        if (host.Length == 0)
        {
            return null;
        }

        var user = entry.Username.Trim();
        var builder = new StringBuilder("ssh ");
        if (user.Length > 0)
        {
            builder.Append(Quote(user, SafeUser())).Append('@');
        }
        builder.Append(Quote(host, SafeHost()));
        if (entry.Port is { } port)
        {
            builder.Append(" -p ").Append(port);
        }
        var keyPath = entry.SshKeyPath.Trim();
        if (keyPath.Length > 0)
        {
            builder.Append(" -i ").Append(Quote(keyPath, SafePath()));
        }
        return builder.ToString();
    }

    /// <summary>POSIX single-quote escaping, which is also literal in PowerShell.</summary>
    private static string Quote(string value, Regex safe) =>
        safe.IsMatch(value) ? value : "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
