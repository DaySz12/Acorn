using Photino.NET;

namespace Acorn.App.Platform;

/// <summary>
/// Gives platform services access to the Photino window (native handle, created/minimized events)
/// without the controllers knowing Photino exists.
/// </summary>
public sealed class MainWindowHolder
{
    public PhotinoWindow? Window { get; private set; }

    public bool IsCreated { get; private set; }

    public event EventHandler? Created;

    public event EventHandler? Minimized;

    public void Attach(PhotinoWindow window)
    {
        Window = window;
        window.RegisterWindowCreatedHandler((_, _) =>
        {
            IsCreated = true;
            Created?.Invoke(this, EventArgs.Empty);
        });
        window.RegisterMinimizedHandler((_, _) => Minimized?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Runs <paramref name="action"/> on the window's UI thread once the window exists.</summary>
    public bool TryInvoke(Action<PhotinoWindow> action)
    {
        var window = Window;
        if (window is null || !IsCreated)
        {
            return false;
        }
        window.Invoke(() => action(window));
        return true;
    }
}
