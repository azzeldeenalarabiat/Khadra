namespace Khadra.Tests.Support;

/// <summary>
/// The repository the tests are running from, for the few tests that hold two projects to one fact
/// — the app's version against the API's minimum, both sides against one vector file.
/// </summary>
internal static class RepositoryRoot
{
    public static string Path { get; } = Find();

    public static string File(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "Khadra.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException(
            $"No Khadra.slnx above {AppContext.BaseDirectory}; these tests read files from the repository.");
    }
}
