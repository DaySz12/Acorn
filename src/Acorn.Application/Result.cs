namespace Acorn.Application;

/// <summary>
/// Outcome of a controller action. <see cref="Error"/> is a user-facing message that never
/// contains secrets or raw exception text.
/// </summary>
public class Result
{
    protected Result(bool succeeded, string? error)
    {
        Succeeded = succeeded;
        Error = error;
    }

    public bool Succeeded { get; }

    public string? Error { get; }

    public static Result Ok() => new(true, null);

    public static Result Fail(string error) => new(false, error);
}

public sealed class Result<T> : Result
{
    private Result(bool succeeded, T? value, string? error) : base(succeeded, error) => Value = value;

    public T? Value { get; }

    public static Result<T> Ok(T value) => new(true, value, null);

    public static new Result<T> Fail(string error) => new(false, default, error);
}
