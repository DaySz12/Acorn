namespace Acorn.Core.Storage;

/// <summary>Points in <see cref="AtomicFileWriter.Write"/> where tests can inject failures.</summary>
public enum AtomicWriteStage
{
    TempWritten,
    TempFlushed,
    Verified,
}

/// <summary>
/// Write-to-temp, flush to disk, read back and verify, then atomically replace (SPEC R3).
/// If any step fails the target file is left untouched and the temp file is removed.
/// </summary>
public sealed class AtomicFileWriter
{
    /// <summary>Test hook: invoked at each stage; throwing simulates a crash or I/O error there.</summary>
    internal Action<AtomicWriteStage>? FaultInjector { get; set; }

    /// <param name="verify">Receives the bytes read back from disk and must throw if they are not valid.</param>
    public void Write(string targetPath, string tempPath, ReadOnlySpan<byte> content, Action<byte[]> verify)
    {
        ArgumentNullException.ThrowIfNull(verify);
        try
        {
            using (var stream = new FileStream(tempPath, FilePermissions.CreateNewOptions()))
            {
                stream.Write(content);
                FaultInjector?.Invoke(AtomicWriteStage.TempWritten);
                stream.Flush(flushToDisk: true);
                FaultInjector?.Invoke(AtomicWriteStage.TempFlushed);
            }
            FilePermissions.RestrictToOwner(tempPath);

            verify(File.ReadAllBytes(tempPath));
            FaultInjector?.Invoke(AtomicWriteStage.Verified);

            Replace(tempPath, targetPath);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void Replace(string tempPath, string targetPath)
    {
        if (OperatingSystem.IsWindows() && File.Exists(targetPath))
        {
            // ReplaceFile swaps the files in one operation and keeps the target's ACL.
            File.Replace(tempPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            // rename(2) is atomic on the same filesystem; temp and target share a directory.
            File.Move(tempPath, targetPath, overwrite: true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
