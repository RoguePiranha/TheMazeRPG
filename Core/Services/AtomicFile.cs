using System.IO;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Crash-safe file replacement for the save system. A plain WriteAllText over the only copy of a
/// permadeath save (or the world delta) can leave a truncated file if the process dies mid-write;
/// writing to a sibling temp file and renaming over the target means the file on disk is always
/// either the old complete version or the new complete version, never half of one.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Set aside a file that failed to parse (as <c>path + ".corrupt"</c>, replacing any older
    /// set-aside copy) so a later save can't silently overwrite the only evidence. Best-effort:
    /// failure to preserve must never turn a read problem into a crash.
    /// </summary>
    public static void TryPreserveCorrupt(string path)
    {
        try
        {
            if (File.Exists(path)) File.Copy(path, path + ".corrupt", overwrite: true);
        }
        catch
        {
            // Preservation is best-effort by contract.
        }
    }
}
