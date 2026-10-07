using Acorn.App.Platform.Common;
using Acorn.App.Platform.MacOS;
using Acorn.App.Platform.Windows;
using Acorn.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Acorn.App.Platform;

/// <summary>Registers the OS-specific implementations of the controller abstractions.</summary>
internal static class PlatformServices
{
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<MainWindowHolder>();

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IClipboardService, WindowsClipboardService>();
            services.AddSingleton<ISessionEvents, WindowsSessionEvents>();
            services.AddSingleton<IScreenProtection, WindowsScreenProtection>();
        }
        else if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<IClipboardService, TextCopyClipboardService>();
            services.AddSingleton<ISessionEvents, MacSessionEvents>();
            services.AddSingleton<IScreenProtection, MacScreenProtection>();
        }
        else
        {
            services.AddSingleton<IClipboardService, TextCopyClipboardService>();
            services.AddSingleton<ISessionEvents, MinimizeOnlySessionEvents>();
            services.AddSingleton<IScreenProtection, UnsupportedScreenProtection>();
        }
    }
}
