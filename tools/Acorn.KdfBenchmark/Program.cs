// Measures Argon2id cost on this machine to help choose header defaults (SPEC 4.2).
// Usage: dotnet run -c Release --project tools/Acorn.KdfBenchmark
using System.Diagnostics;
using System.Security.Cryptography;
using Acorn.Core.Crypto;

Argon2idParameters[] candidates =
[
    new(32 * 1024, 3, 2),
    Argon2idParameters.Recommended,
    new(64 * 1024, 4, 2),
    new(96 * 1024, 3, 2),
    Argon2idParameters.Strong,
    new(256 * 1024, 3, 4),
];

var password = RandomNumberGenerator.GetBytes(24);
var salt = RandomNumberGenerator.GetBytes(Kdf.SaltSize);

// Warm up JIT so the first row is not inflated.
Secrets.Zero(Kdf.DerivePasswordKey(password, salt, new Argon2idParameters(Argon2idParameters.MinMemoryKiB, 1, 1)));

Console.WriteLine($"{Environment.ProcessorCount} logical CPUs, {RuntimeInformationText()}");
Console.WriteLine("memory    iter  par   median    min");
foreach (var p in candidates)
{
    var samples = new List<double>();
    for (var i = 0; i < 5; i++)
    {
        var sw = Stopwatch.StartNew();
        Secrets.Zero(Kdf.DerivePasswordKey(password, salt, p));
        samples.Add(sw.Elapsed.TotalMilliseconds);
    }
    samples.Sort();
    Console.WriteLine($"{p.MemoryKiB / 1024,4} MiB  {p.Iterations,4}  {p.Parallelism,3}  {samples[2],6:F0} ms  {samples[0],5:F0} ms");
}

static string RuntimeInformationText() =>
    $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim()}, {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}";
