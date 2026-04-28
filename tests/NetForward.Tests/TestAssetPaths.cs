namespace NetForward.Tests;

internal static class TestAssetPaths
{
    /// <summary>
    /// Walks up from the test binary directory to locate the repo's tests/TestAssets folder.
    /// Works whether tests run from `bin/Debug/net8.0` or wherever.
    /// </summary>
    public static string Root
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null)
            {
                var candidate = Path.Combine(dir, "tests", "TestAssets");
                if (Directory.Exists(candidate)) return candidate;

                // Also try sibling-walk for the case where 'tests' is the current segment.
                var parent = Directory.GetParent(dir);
                dir = parent?.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate tests/TestAssets folder.");
        }
    }

    public static string LegacyMvcSolution => Path.Combine(Root, "LegacyMvcApp.sln");
    public static string LegacyMvcCsproj => Path.Combine(Root, "LegacyMvcApp", "LegacyMvcApp.csproj");
}
