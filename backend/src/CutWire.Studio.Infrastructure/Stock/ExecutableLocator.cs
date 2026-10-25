namespace CutWire.Studio.Infrastructure.Stock;

public static class ExecutableLocator
{
    /// <summary>Full path of an executable given as an absolute/relative path or a bare name looked up on PATH; null when absent.</summary>
    public static string? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();

        if (Path.IsPathRooted(name) || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
            return IsExecutable(name) ? Path.GetFullPath(name) : null;

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries).Prepend("").ToArray()
            : [""];
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in extensions)
            {
                string candidate;
                try { candidate = Path.Combine(dir.Trim('"'), name + ext); }
                catch (ArgumentException) { continue; }
                if (IsExecutable(candidate)) return Path.GetFullPath(candidate);
            }
        }
        return null;
    }

    private static bool IsExecutable(string path)
    {
        if (!File.Exists(path)) return false;
        if (OperatingSystem.IsWindows()) return true;
        try
        {
            return (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
