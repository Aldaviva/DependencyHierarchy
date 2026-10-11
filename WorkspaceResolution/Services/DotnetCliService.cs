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

    public static async Task restoreOpenVisualStudioProjectsThatDependOn(string packageName, string? excludedSolutionFile, bool buildAfterRestore, string localPackageDir,
                                                                         CancellationToken cancellationToken = default) {
        excludedSolutionFile = excludedSolutionFile is { Length: not 0 } ? Path.GetFullPath(excludedSolutionFile) : null;

        ParallelOptions parallelOptions = new() { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount };
        await Parallel.ForEachAsync(VisualStudioService.findOpenCsProjects(cancellationToken), parallelOptions, async (project, ct) => {
            // don't restore in same solution, it slows down unit testing cycles, and you can use conditional project references instead
            if (project.solutionAbsoluteFilename.Equals(excludedSolutionFile, CASE_INSENSITIVE)) return;

            IReadOnlyCollection<Dependency> dependencies = await DependencyLister.listDependencies(Path.GetDirectoryName(project.absoluteFilename)!, ct);

            if (!dependencies.Any(dep => dep.name.Equals(packageName, CASE_INSENSITIVE))) return; // project doesn't depend on package

            Stopwatch     restoreStopwatch = Stopwatch.StartNew();
            ProcessResult restoreResult    = await restore(project, localPackageDir);
            restoreStopwatch.Stop();

            if (restoreResult.ExitCode != 0) {
                Console.WriteLine("Failed to restore {0}: exit code {1}", project.absoluteFilename, restoreResult.ExitCode);
                Console.WriteLine(restoreResult.StdOut);
                Console.WriteLine(restoreResult.StdErr);
                return;
            }

            Console.WriteLine("Restored {0} because it depends on {1} (in {2:N0} ms).", project.absoluteFilename, packageName, restoreStopwatch.ElapsedMilliseconds);
            if (buildAfterRestore && await isPackable(project)) {
                await build(project);
                Console.WriteLine("Built {0} because it is a package.", project.absoluteFilename);
            }
        });
    }

    private static async Task<ProcessResult> restore(CsProject project, string packageSource) =>
        await Process.ExecFile(DOTNET,
            "restore",
            project.absoluteFilename,
            "-v:q",
            "--force",
            "--force-evaluate",
            "--source",
            Path.GetFullPath(packageSource));

    private static async Task<int> build(CsProject project) =>
        (await Process.ExecFile(DOTNET,
            "build",
            project.absoluteFilename,
            "-v:q",
            "--no-restore"))
        .ExitCode;

    public static async Task<IEnumerable<(string url, bool isEnabled)>> listPackageSources(CancellationToken cancellationToken = default) =>
        (await Process.ExecFile(DOTNET, [
            "nuget",
            "list",
            "source",
            "--format", "short"
        ], cancellationToken: cancellationToken))
        .StdOut
        .Trim()
        .Split('\n')
        .Select(static line => {
            string[] lineSplit = line.Trim().Split(' ', 2);
            return (url: lineSplit[1], isEnabled: lineSplit[0] == "E");
        });

    /// <exception cref="ApplicationException"></exception>
    public static async Task addPackageSource(string name, string url, CancellationToken cancellationToken = default) {
        ProcessResult addResult = await Process.ExecFile(DOTNET, [
            "nuget",
            "add",
            "source", url,
            "--name", name
        ], cancellationToken: cancellationToken);

        if (addResult.ExitCode != 0) {
            throw new ApplicationException(addResult.StdOut + '\n' + addResult.StdErr) { Data = { { "exitCode", addResult.ExitCode } } };
        }
    }

    private static async Task<bool> isPackable(CsProject project) {
        ProcessResult propertyResult = await Process.ExecFile(DOTNET,
            "msbuild",
            project.absoluteFilename,
            "-getProperty:" + ((ReadOnlySpan<string>) [
                "ispackable",
                "istestproject",
                "symbolpackageformat",
                ..PACKAGE_PROPERTY_NAMES
            ]).Join(','));

        if (propertyResult.ExitCode != 0) return false;

        try {
            IDictionary<string, string> projectProperties = JsonNode.Parse(propertyResult.StdOut)!["Properties"]!.AsObject()
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