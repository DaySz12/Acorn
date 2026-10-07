namespace Acorn.Application.Services;

/// <summary>Runs a slow controller action (KDF, disk I/O) with <see cref="AppState.IsBusy"/> set, mapping failures to messages.</summary>
internal static class BusyRunner
{
    public static async Task<Result> RunAsync(AppState state, Func<Task> action, string authenticationMessage, Action? onFailure = null)
    {
        if (state.IsBusy)
        {
            return Result.Fail(Messages.Busy);
        }

        state.IsBusy = true;
        state.NotifyChanged();
        try
        {
            await action().ConfigureAwait(false);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            onFailure?.Invoke();
            return Result.Fail(ErrorMapper.ToMessage(ex, authenticationMessage));
        }
        finally
        {
            state.IsBusy = false;
            state.NotifyChanged();
        }
    }
}
