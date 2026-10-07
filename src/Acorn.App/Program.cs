using Acorn.App.Platform;
using Acorn.App.Shell;
using Acorn.Application;
using Acorn.Application.Abstractions;
using Acorn.Application.Controllers;
using Acorn.Application.Services;
using Acorn.Core;
using Acorn.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Photino.Blazor;

namespace Acorn.App;

/// <summary>
/// Composition root: wires Model → Controllers → Views, creates the Photino window and
/// guarantees that keys are zeroed and the clipboard cleared on every way out of the process.
/// Content-Security-Policy lives in wwwroot/index.html.
/// </summary>
internal static class Program
{
    // WebView2 (Windows) flags: no background networking, crash uploads or form-data features.
    private const string WindowsBrowserArguments =
        "--disable-background-networking --disable-component-update --disable-domain-reliability " +
        "--disable-sync --no-pings --disable-breakpad --disable-crash-reporter " +
        "--disable-features=msSmartScreenProtection,AutofillServerCommunication,Translate";

    [STAThread]
    private static void Main(string[] args)
    {
        ProcessHardening.Apply();

        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        var services = builder.Services;

        services.AddLogging(logging =>
        {
            // Release builds log nowhere. Debug logs go to the console; no secret is ever logged.
            logging.ClearProviders();
#if DEBUG
            logging.AddConsole();
#endif
        });

        // Model
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => VaultPaths.ForCurrentUser());
        services.AddSingleton<VaultStore>();
        services.AddSingleton<VaultSession>(sp => new VaultSession(sp.GetRequiredService<VaultStore>()));
        services.AddSingleton<IVaultSession>(sp => sp.GetRequiredService<VaultSession>());

        // Controllers and their services
        services.AddSingleton<AppState>();
        services.AddSingleton<INavigator, AppStateNavigator>();
        services.AddSingleton<ClipboardGuard>();
        services.AddSingleton<IAutoLockTimer, AutoLockTimer>();
        services.AddSingleton<VaultController>();
        services.AddSingleton<UnlockController>();
        services.AddSingleton<EntryController>();
        services.AddSingleton<SettingsController>();
        services.AddSingleton<BackupController>();

        // Platform implementations of the abstractions
        PlatformServices.Register(services);
#if DEBUG
        // Developer-only: allow screenshots of the UI (e.g. for docs). Compiled out of Release builds.
        if (Environment.GetEnvironmentVariable("ACORN_DEV_ALLOW_CAPTURE") == "1")
        {
            services.AddSingleton<IScreenProtection, Platform.Common.UnsupportedScreenProtection>();
        }
#endif

        builder.RootComponents.Add<Root>("#app");
        var app = builder.Build();

        // Photino logs every web message (including rendered UI) to stdout at its default verbosity.
        // Turn that off first so decrypted content can never reach a console or redirected log.
        var window = app.MainWindow
            .SetLogVerbosity(0)
            .SetTitle("Acorn")
            .SetUseOsDefaultSize(false)
            .SetSize(1440, 920)
            .SetMinSize(1000, 640)
            .SetGrantBrowserPermissions(false);
#if DEBUG
        window.SetDevToolsEnabled(true).SetContextMenuEnabled(true);
#else
        window.SetDevToolsEnabled(false).SetContextMenuEnabled(false);
#endif
        if (OperatingSystem.IsWindows())
        {
            // Own WebView2 profile instead of the "Photino" folder shared with other Photino apps.
            // It holds browser caches only; no vault data is stored there.
            var webViewData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Acorn", "WebView2");
            window.SetTemporaryFilesPath(webViewData)
                  .SetIconFile(Path.Combine(AppContext.BaseDirectory, "wwwroot", "favicon.ico"))
                  .SetBrowserControlInitParameters(WindowsBrowserArguments);
        }
        window.Center();

        var provider = app.Services;
        var holder = provider.GetRequiredService<MainWindowHolder>();
        var vault = provider.GetRequiredService<VaultController>();
        var sessionEvents = provider.GetRequiredService<ISessionEvents>();
        holder.Attach(window);
        // Start OS listeners on the UI thread once the native window (and its message loop) exists.
        holder.Created += (_, _) => sessionEvents.Start();

        var shutdownOnce = 0;
        void Shutdown()
        {
            if (Interlocked.Exchange(ref shutdownOnce, 1) == 0)
            {
                try
                {
                    vault.ShutdownAsync().GetAwaiter().GetResult();
                }
                catch (Exception)
                {
                    provider.GetRequiredService<IVaultSession>().Lock();
                }
            }
        }

        window.RegisterWindowClosingHandler((_, _) =>
        {
            Shutdown();
            return false; // allow the window to close
        });
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Shutdown(); // never display or write exception details
        TaskScheduler.UnobservedTaskException += (_, e) => e.SetObserved();

        provider.GetRequiredService<UnlockController>().Initialize();

        app.Run();

        Shutdown();
        // Release the vault lock explicitly. The container itself is not disposed: Photino's web view
        // manager only supports async disposal, which cannot complete once the UI loop has stopped.
        // The OS would release the lock on exit anyway; this just makes it immediate.
        provider.GetRequiredService<VaultSession>().Dispose();
        provider.GetRequiredService<VaultStore>().Dispose();
    }
}
