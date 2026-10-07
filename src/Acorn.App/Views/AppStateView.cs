using Acorn.Application;
using Microsoft.AspNetCore.Components;

namespace Acorn.App.Views;

/// <summary>
/// Base for views and components that render from <see cref="AppState"/>. Subscribes on init,
/// unsubscribes on dispose, and gives derived views a hook to drop any secret they were holding
/// (typed passwords) when they leave the screen — which always happens on lock.
/// </summary>
public abstract class AppStateView : ComponentBase, IDisposable
{
    [Inject]
    protected AppState State { get; set; } = default!;

    protected override void OnInitialized()
    {
        State.Changed += OnStateChanged;
        base.OnInitialized();
    }

    public void Dispose()
    {
        State.Changed -= OnStateChanged;
        ClearSecrets();
        GC.SuppressFinalize(this);
    }

    /// <summary>Clears fields that held typed secrets. Called on dispose.</summary>
    protected virtual void ClearSecrets()
    {
    }

    // AppState may change on a timer thread (auto-lock, reveal timeout).
    private void OnStateChanged() => _ = InvokeAsync(StateHasChanged);
}
