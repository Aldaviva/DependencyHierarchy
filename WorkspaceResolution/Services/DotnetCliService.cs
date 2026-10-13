using Dependencies;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkspaceResolution.Data;

namespace WorkspaceResolution.Services;

/*
 * My god, the NuGet Client SDK is impossible to use. None of the types are compatible, some important packages have been unlisted, and absolutely none of it is documented.
 * Just fork a process.
 */
public static class DotnetCliService {

    private const string           DOTNET           = "dotnet.exe";
    private const StringComparison CASE_INSENSITIVE = StringComparison.OrdinalIgnoreCase;

    private static readonly string[] PACKAGE_PROPERTY_NAMES = [
        "description", "includesource", "includesymbols", "packageicon", "packagelicenseexpression", "packageprojecturl", "packagereadmefile", "packagetags", "publishrepositoryurl", "repositorytype",
        "repositoryurl"
    ];

    public static async Task restoreOpenVisualStudioProjectsThatDependOn(string packageName, string? excludedSolutionFile, bool buildAfterRestore, CancellationToken cancellationToken = default) {
        excludedSolutionFile = excludedSolutionFile is { Length: not 0 } ? Path.GetFullPath(excludedSolutionFile) : null;

        ParallelOptions parallelOptions = new() { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount };
        await Parallel.ForEachAsync(VisualStudioService.findOpenCsProjects(cancellationToken), parallelOptions, async (project, ct) => {
            // don't restore in same solution, it slows down unit testing cycles, and you can use conditional project references instead
            if (project.solutionAbsoluteFilename.Equals(excludedSolutionFile, CASE_INSENSITIVE)) return;

            IReadOnlyCollection<Dependency> dependencies = await DependencyLister.listDependencies(Path.GetDirectoryName(project.absoluteFilename)!, ct);

            if (!dependencies.Any(dep => dep.name.Equals(packageName, CASE_INSENSITIVE))) return; // project doesn't depend on package

            Stopwatch restoreStopwatch = Stopwatch.StartNew();
            try {
                (await restore(project)).EnsureSuccessful($"Failed to restore {project.absoluteFilename}", ProcessResult.Details.NoCommandLine);
            } catch (ProcessException e) {
                Console.WriteLine(e.Message);
                return;
            }

            restoreStopwatch.Stop();
            Console.WriteLine("Restored {0} because it depends on {1} (in {2:N0} ms).", project.absoluteFilename, packageName, restoreStopwatch.ElapsedMilliseconds);
            if (buildAfterRestore && await isPackable(project)) {
                await build(project);
                Console.WriteLine("Built {0} because it is a package.", project.absoluteFilename);
            }
        });
    }

    private static async Task<ProcessResult> restore(CsProject project /*, string packageSource*/) =>
        await Process.ExecFile(DOTNET,
            "restore",
            project.absoluteFilename,
            "-v:q",
            "--force",
            "--force-evaluate",
            "--source", "https://0.0.0.0" // does this help?
            /*, "--source",
            Path.GetFullPath(packageSource)*/);

    private static async Task<int> build(CsProject project) =>
        (await Process.ExecFile(DOTNET,
            "build",
            project.absoluteFilename,
            "-v:q",
            "--no-restore"))
        .ExitCode;

    // This is technically only in the NuGet CLI program, not dotnet, so maybe it deserves its own service.
    /// <exception cref="ProcessException"></exception>
    public static async Task installLocalPackage(string packageFilePath, string destinationDirectory, CancellationToken cancellationToken = default) =>
        (await Process.ExecFile("nuget", ["add", packageFilePath, "-Source", destinationDirectory, "-Expand", "-NonInteractive"], cancellationToken: cancellationToken))
        .EnsureSuccessful("Installation failed to copy package to local cache", ProcessResult.Details.All);

    /// <exception cref="ProcessException"></exception>
    public static async Task<string> getPackageCacheDir(CancellationToken cancellationToken = default) =>
        Path.GetFullPath((await Process.ExecFile("dotnet", ["nuget", "locals", "global-packages", "--list"], cancellationToken: cancellationToken))
            .EnsureSuccessful(null, ProcessResult.Details.All).StdOut.TrimStart("global-packages: "));

    private static async Task<bool> isPackable(CsProject project) {
        (int exitCode, string stdOut) = await Process.ExecFile(DOTNET,
            "msbuild",
            project.absoluteFilename,
            "-getProperty:" + ((ReadOnlySpan<string>) [
                "ispackable",
                "istestproject",
                "symbolpackageformat",
                .. PACKAGE_PROPERTY_NAMES
            ]).Join(','));

        if (exitCode != 0) return false;

        try {
            IDictionary<string, string> projectProperties = JsonNode.Parse(stdOut)!["Properties"]!.AsObject()
                .ToDictionary(static pair => pair.Key.ToLowerInvariant(), static pair => pair.Value!.GetValue<string>().EmptyToNull).Compact();

            return !"false".Equals(projectProperties.GetValueOrNull("ispackable"), CASE_INSENSITIVE) &&
                !"true".Equals(projectProperties.GetValueOrNull("istestproject"), CASE_INSENSITIVE) &&
                ("snupkg".Equals(projectProperties.GetValueOrNull("symbolpackageformat"), CASE_INSENSITIVE) ||
                    PACKAGE_PROPERTY_NAMES.Any(packagePropertyName => projectProperties.GetValueOrNull(packagePropertyName).HasText));
        } catch (JsonException) {
            return false;
        }
    }

}