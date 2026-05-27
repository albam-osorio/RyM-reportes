namespace RymReportes.Tests;

public class ConfigurationSecretsTests
{
    private static readonly string[] FilePatterns = ["*.json", "*.md", "*.ps1", "*.rds", "*.rptproj"];
    private static readonly string[] ExcludedDirectories = [".git", "bin", "obj", "artifacts"];
    private static readonly string[] ForbiddenSnippets =
    [
        "Password" + "=remesas",
        "User Id" + "=remesas",
        "Password" + "=;",
        "Password" + "=",
        "ec2-" + "52-203-6-228"
    ];

    [Fact]
    public void ConfigurationAndDocsDoNotContainSqlPasswords()
    {
        var root = FindRepositoryRoot();
        var files = FilePatterns
            .SelectMany(pattern => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            .Where(file => !IsInExcludedDirectory(root, file));

        var matches = files
            .SelectMany(file => ForbiddenSnippets
                .Where(snippet => File.ReadAllText(file).Contains(snippet, StringComparison.OrdinalIgnoreCase))
                .Select(snippet => $"{Path.GetRelativePath(root, file)} contiene {snippet}"))
            .ToArray();

        Assert.Empty(matches);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RymReportes.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }

    private static bool IsInExcludedDirectory(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => ExcludedDirectories.Contains(part, StringComparer.OrdinalIgnoreCase));
    }
}
