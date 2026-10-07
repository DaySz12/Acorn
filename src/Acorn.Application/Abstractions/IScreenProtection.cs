namespace Acorn.Application.Abstractions;

/// <summary>Excludes the main window from screenshots and screen sharing (SPEC R7). Best effort.</summary>
public interface IScreenProtection
{
    bool IsSupported { get; }

    /// <summary>Returns true when the requested state was applied.</summary>
    bool SetEnabled(bool enabled);
}
