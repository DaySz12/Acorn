namespace Acorn.Application.Abstractions;

/// <summary>OS clipboard. Implemented per platform in Acorn.App/Platform.</summary>
public interface IClipboardService
{
    /// <param name="isSecret">Ask the OS (best effort) to keep the value out of clipboard history and cloud sync.</param>
    Task SetTextAsync(string text, bool isSecret);

    Task<string?> GetTextAsync();

    Task ClearAsync();
}
