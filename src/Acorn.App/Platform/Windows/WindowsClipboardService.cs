using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Acorn.Application.Abstractions;

namespace Acorn.App.Platform.Windows;

/// <summary>
/// Win32 clipboard. Secrets are tagged with the formats Windows clipboard history, cloud clipboard
/// and well-behaved clipboard managers honour (best effort, SPEC R5).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsClipboardService : IClipboardService
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private static readonly object Gate = new();

    private readonly MainWindowHolder _window;

    public WindowsClipboardService(MainWindowHolder window) => _window = window;

    public Task SetTextAsync(string text, bool isSecret)
    {
        ArgumentNullException.ThrowIfNull(text);
        WithClipboard(() =>
        {
            if (!EmptyClipboard())
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            SetData(CfUnicodeText, UnicodeBytes(text));
            if (isSecret)
            {
                SetData(RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"), [0]);
                SetData(RegisterClipboardFormat("CanIncludeInClipboardHistory"), [0, 0, 0, 0]);
                SetData(RegisterClipboardFormat("CanUploadToCloudClipboard"), [0, 0, 0, 0]);
            }
        });
        return Task.CompletedTask;
    }

    public Task<string?> GetTextAsync()
    {
        string? text = null;
        if (IsClipboardFormatAvailable(CfUnicodeText))
        {
            WithClipboard(() =>
            {
                var handle = GetClipboardData(CfUnicodeText);
                if (handle == 0)
                {
                    return;
                }
                var pointer = GlobalLock(handle);
                if (pointer == 0)
                {
                    return;
                }
                try
                {
                    var chars = (int)Math.Min((ulong)GlobalSize(handle) / 2, int.MaxValue);
                    var raw = Marshal.PtrToStringUni(pointer, chars);
                    var end = raw.IndexOf('\0', StringComparison.Ordinal);
                    text = end >= 0 ? raw[..end] : raw;
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            });
        }
        return Task.FromResult(text);
    }

    public Task ClearAsync()
    {
        WithClipboard(() => EmptyClipboard());
        return Task.CompletedTask;
    }

    private void WithClipboard(Action action)
    {
        lock (Gate)
        {
            var owner = _window.IsCreated && _window.Window is { } w ? w.WindowHandle : 0;
            // Another application may hold the clipboard briefly; retry a few times.
            var opened = false;
            for (var attempt = 0; attempt < 10 && !(opened = OpenClipboard(owner)); attempt++)
            {
                Thread.Sleep(20);
            }
            if (!opened)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            try
            {
                action();
            }
            finally
            {
                CloseClipboard();
            }
        }
    }

    private static byte[] UnicodeBytes(string text)
    {
        var bytes = new byte[(text.Length + 1) * 2];
        MemoryMarshal.AsBytes(text.AsSpan()).CopyTo(bytes);
        return bytes;
    }

    private static void SetData(uint format, byte[] data)
    {
        var handle = GlobalAlloc(GmemMoveable, (nuint)data.Length);
        if (handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        try
        {
            var pointer = GlobalLock(handle);
            if (pointer == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            Marshal.Copy(data, 0, pointer, data.Length);
            GlobalUnlock(handle);
            if (SetClipboardData(format, handle) == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            handle = 0; // Owned by the system now.
        }
        finally
        {
            Array.Clear(data);
            if (handle != 0)
            {
                GlobalFree(handle);
            }
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetClipboardData(uint uFormat, nint hMem);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetClipboardData(uint uFormat);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormat(string lpszFormat);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint hMem);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalFree(nint hMem);

    [LibraryImport("kernel32.dll")]
    private static partial nuint GlobalSize(nint hMem);
}
