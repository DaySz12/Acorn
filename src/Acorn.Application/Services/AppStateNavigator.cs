using Acorn.Application.Abstractions;

namespace Acorn.Application.Services;

/// <summary>Default <see cref="INavigator"/>: the current screen lives in <see cref="AppState"/>.</summary>
public sealed class AppStateNavigator : INavigator
{
    private readonly AppState _state;

    public AppStateNavigator(AppState state) => _state = state;

    public void GoTo(Screen screen)
    {
        if (_state.Screen != screen)
        {
            // Revealed secrets never survive a screen change (SPEC R7).
            _state.HideAllSecrets();
        }
        _state.Screen = screen;
        _state.NotifyChanged();
    }
}
