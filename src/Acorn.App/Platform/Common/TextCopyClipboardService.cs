using Acorn.Application.Abstractions;
using TextCopy;

namespace Acorn.App.Platform.Common;

/// <summary>
/// Clipboard via TextCopy on macOS and Linux. The "secret" hint cannot be expressed here;
/// clipboard managers may record the value (documented in SECURITY.md).
/// </summary>
internal sealed class TextCopyClipboardService : IClipboardService
{
    public Task SetTextAsync(string text, bool isSecret) => ClipboardService.SetTextAsync(text);

    public Task<string?> GetTextAsync() => ClipboardService.GetTextAsync();

    public Task ClearAsync() => ClipboardService.SetTextAsync("");
}
