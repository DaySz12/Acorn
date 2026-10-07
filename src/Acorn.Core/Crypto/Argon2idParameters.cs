namespace Acorn.Core.Crypto;

/// <summary>
/// Argon2id cost parameters. They are stored in the vault header and always read from there
/// on unlock; the constants below only bound what a header may ask for.
/// </summary>
public sealed record Argon2idParameters(int MemoryKiB, int Iterations, int Parallelism)
{
    // Bounds protect against corrupted or hostile headers that would exhaust RAM or hang the app.
    public const int MinMemoryKiB = 8 * 1024;       // 8 MiB
    public const int MaxMemoryKiB = 1024 * 1024;    // 1 GiB
    public const int MinIterations = 1;
    public const int MaxIterations = 20;
    public const int MinParallelism = 1;
    public const int MaxParallelism = 16;

    /// <summary>Default for new vaults (SPEC 4.2). See docs/FORMAT.md for the benchmark.</summary>
    public static Argon2idParameters Recommended { get; } = new(64 * 1024, 3, 2);

    /// <summary>Offered by "Upgrade KDF strength".</summary>
    public static Argon2idParameters Strong { get; } = new(256 * 1024, 3, 4);

    public bool IsWithinBounds =>
        MemoryKiB is >= MinMemoryKiB and <= MaxMemoryKiB &&
        Iterations is >= MinIterations and <= MaxIterations &&
        Parallelism is >= MinParallelism and <= MaxParallelism &&
        // Argon2 requires at least 8 KiB of memory per lane.
        MemoryKiB >= 8 * Parallelism;

    public void EnsureWithinBounds()
    {
        if (!IsWithinBounds)
        {
            throw new VaultFormatException("The key derivation parameters in the vault header are outside the accepted range.");
        }
    }

    /// <summary>True when memory or iterations are below <paramref name="other"/>.</summary>
    public bool IsWeakerThan(Argon2idParameters other) =>
        MemoryKiB < other.MemoryKiB || Iterations < other.Iterations;
}
