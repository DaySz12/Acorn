using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Acorn.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Acorn.App.Platform.MacOS;

/// <summary>
/// Sets NSWindow.sharingType = NSWindowSharingNone on the app's windows (SPEC R7). Best effort:
/// newer capture APIs may not honour it. NOT verified on real hardware — see docs/MANUAL_TESTS.md.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacScreenProtection : IScreenProtection
{
    private const nuint NSWindowSharingNone = 0;
    private const nuint NSWindowSharingReadOnly = 1;

    private readonly MainWindowHolder _window;
    private readonly ILogger<MacScreenProtection> _logger;
    private volatile bool _enabled = true;

    public MacScreenProtection(MainWindowHolder window, ILogger<MacScreenProtection> logger)
    {
        _window = window;
        _logger = logger;
        _window.Created += (_, _) => Apply();
    }

    public bool IsSupported => true;

    public bool SetEnabled(bool enabled)
    {
        _enabled = enabled;
        return Apply();
    }

    private bool Apply()
    {
        var applied = false;
        var invoked = _window.TryInvoke(_ =>
        {
            try
            {
                var app = ObjC.Send(ObjC.GetClass("NSApplication"), ObjC.Selector("sharedApplication"));
                var windows = ObjC.Send(app, ObjC.Selector("windows"));
                var count = ObjC.SendReturningUInt(windows, ObjC.Selector("count"));
                for (nuint i = 0; i < count; i++)
                {
                    var window = ObjC.SendUInt(windows, ObjC.Selector("objectAtIndex:"), i);
                    ObjC.SendUInt(window, ObjC.Selector("setSharingType:"), _enabled ? NSWindowSharingNone : NSWindowSharingReadOnly);
                }
                applied = count > 0;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
            {
                applied = false;
            }
        });

        if (invoked && !applied)
        {
            _logger.LogWarning("macOS window sharing type could not be changed.");
        }
        return applied || !invoked;
    }
}
