namespace Acorn.Application.Abstractions;

/// <summary>Moves the app between screens. Controllers call it; views render <see cref="AppState.Screen"/>.</summary>
public interface INavigator
{
    void GoTo(Screen screen);
}
