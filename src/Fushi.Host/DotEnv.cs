namespace Fushi.Host;

/// <summary>
/// Loads a <c>.env</c> file into the process environment before configuration is
/// built.
/// </summary>
/// <remarks>
/// .NET has no <c>.env</c> provider, so the file this repository ships and
/// documents is inert on its own — it only takes effect if a shell exports it
/// first. That works exactly once, in the terminal it was done in, and not at all
/// from an IDE's run button, which is the failure this exists to stop: the file
/// is filled in correctly and the process still starts with nothing configured.
/// <br/>
/// It sets environment variables rather than adding a configuration source, so
/// the values arrive through the provider that would have carried them anyway.
/// That keeps the documented precedence — appsettings, then user secrets, then
/// environment — true, and means a value read here behaves identically to one
/// exported by hand.
/// <br/>
/// A variable already present in the environment is never overwritten. The
/// environment is the more specific instruction: a container's injected secret,
/// or a one-off override on the command line, has to beat a file checked out on
/// disk.
/// </remarks>
internal static class DotEnv
{
    /// <summary>
    /// The most directories to climb looking for the file.
    /// </summary>
    /// <remarks>
    /// The file sits at the repository root while the process starts in
    /// <c>src/Fushi.Host</c>, so climbing is required rather than optional. The
    /// limit stops a host started from somewhere unexpected walking to the drive
    /// root and reading a stranger's file.
    /// </remarks>
    private const int MAX_DEPTH = 6;

    /// <summary>
    /// Finds the nearest <c>.env</c> and applies it.
    /// </summary>
    /// <remarks>
    /// Silent when there is no file. A deployed host is configured by its
    /// platform and will never have one, so its absence is the normal case rather
    /// than a problem worth reporting — and nothing has been logged yet at the
    /// point this runs.
    /// </remarks>
    /// <param name="startDirectory">
    /// Where to begin looking, or <see langword="null"/> for the working
    /// directory.
    /// </param>
    public static void Load(string? startDirectory = null)
    {
        if (Find(startDirectory ?? Directory.GetCurrentDirectory()) is not { } path)
        {
            return;
        }

        foreach (string line in File.ReadLines(path))
        {
            Apply(line);
        }
    }

    private static string? Find(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);

        for (int depth = 0; depth < MAX_DEPTH && directory is not null; depth++)
        {
            string candidate = Path.Combine(directory.FullName, ".env");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static void Apply(string line)
    {
        ReadOnlySpan<char> trimmed = line.AsSpan().Trim();

        if (trimmed.IsEmpty || trimmed[0] == '#')
        {
            return;
        }

        // `export KEY=value` is valid in the file a shell would source, and the
        // documented workflow sources it, so it has to be understood here too.
        if (trimmed.StartsWith("export ", StringComparison.Ordinal))
        {
            trimmed = trimmed["export ".Length..].TrimStart();
        }

        int separator = trimmed.IndexOf('=');
        if (separator <= 0)
        {
            return;
        }

        string key = trimmed[..separator].TrimEnd().ToString();

        // Split on the first '=' only. A connection string is full of them, and
        // splitting on all of them would truncate every one of them at the first
        // parameter.
        string value = Unquote(trimmed[(separator + 1)..].Trim());

        if (Environment.GetEnvironmentVariable(key) is not null)
        {
            return;
        }

        Environment.SetEnvironmentVariable(key, value);
    }

    private static string Unquote(ReadOnlySpan<char> value)
    {
        bool quoted = value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''));

        return quoted ? value[1..^1].ToString() : value.ToString();
    }
}
