using McMaster.Extensions.CommandLineUtils;
using NuGetGalleryClient;
using System.Reflection;
using System.Text.RegularExpressions;
using WorkspaceResolution.Services;
using WorkspaceResolution.Services.MsBuild;

using CommandLineApplication app = new() {
    UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.Throw,
    Description                  = "Restore all C# projects currently open in Visual Studio which depend upon a given NuGet package"
};
app.VersionOptionFromAssemblyAttributes("-v|--version", Assembly.GetEntryAssembly() ?? typeof(Program).Assembly);
app.Conventions.UseDefaultConventions();
app.ExtendedHelpText = $"""

    Examples:
      Restore all open C# projects that depend on the Unfucked package:
        {app.Name} restore-dependents Unfucked 1.0.0 "C:\Users\Ben\Documents\Projects\Unfucked\Unfucked\bin\Release\Unfucked.1.0.0.nupkg"
      
      Install build hooks and NuGet configuration required to make this program work:
        {app.Name} install
    """;

app.Command("restore-dependents", restoreCommand => {
    CommandArgument packageName             = restoreCommand.Argument("packageId", "Name (ID) of the NuGet package").IsRequired();
    CommandArgument packageVersion          = restoreCommand.Argument("packageVersion", "Version of the NuGet package").IsRequired();
    CommandArgument packageFilename         = restoreCommand.Argument("packageFile", "Filename of .nupkg package").IsRequired();
    CommandOption   nugetPackageCacheDirOpt = restoreCommand.Option("--nuget-package-cache-dir", @"Directory for local packages, defaults to ~\.nuget\packages", CommandOptionType.SingleValue);
    CommandOption skipSolutionFile = restoreCommand.Option("--skip-solution", "When restoring dependent projects, skip projects in the solution defined by this .sln filename",
        CommandOptionType.SingleValue);
    CommandOption buildAfterRestore = restoreCommand.Option("--build-dependent-packages",
        "After restoring a dependent package project, build it, which is useful if its transitive dependents need to be restored too", CommandOptionType.NoValue);

    restoreCommand.OnExecuteAsync(async ct => {
        using NuGetGalleryService        nuGetGalleryService    = new();
        NuGetGalleryPackageVersionsCache galleryPackageVersions = new(nuGetGalleryService);
        string                           nugetPackageCacheDir   = nugetPackageCacheDirOpt.Value() ?? await DotnetCliService.getPackageCacheDir(ct);
        if (await galleryPackageVersions.doesPackageVersionExist(packageName.Value!, packageVersion.Value!, nugetPackageCacheDir, ct)) {
            Console.WriteLine("Package {0} {1} already exists in NuGet Gallery, so not installing locally-built package into user package cache.", packageName.Value, packageVersion.Value);
            return;
        }

        await deletePackageDir(Path.Combine(nugetPackageCacheDir, packageName.Value!, packageVersion.Value!));
        await NuGetCliService.installLocalPackage(packageFilename.Value!, nugetPackageCacheDir, cancellationToken: ct);
        Console.WriteLine("Installed {0} {1} into local NuGet package cache.", packageName.Value, packageVersion.Value);

        await DotnetCliService.restoreOpenVisualStudioProjectsThatDependOn(packageName.Value!, skipSolutionFile.Value(), buildAfterRestore.HasValue(), ct);
    });
});

app.Command("install", installCommand => installCommand.OnExecuteAsync(async ct => await InstallerService.install(ct)));

return await app.ExecuteAsync(args);

/*
 * Another fallback idea: if the file still can't be deleted, then walk through all files to delete until we find the locked one, then compare its date, size, and hash to the corresponding file inside the nupkg, and if they're identical, then leave the locked file and just let `nuget add` expand around it. This shouldn't have too much of a downside because Unfucked.BuildInfoGenerator.dll changes extremely infrequently compared to Unfucked.dll, but just to be sure, we can log a message saying we skipped it. If the file did change, we should probably fail the build like before and let the user find and kill msbuild themselves.
 */
static async Task deletePackageDir(string packageDirToDelete) {
    Queue<MsBuildKiller>         msBuildKillers      = new([new EnvironmentVariableMsBuildKiller(), new HandleMsBuildKiller(), new LibraryMsBuildKiller()]);
    string?                      lockedFilename      = null; // does not include a path
    UnauthorizedAccessException? mostRecentException = null;

    do {
        int? killCount = null;
        if (lockedFilename != null) {
            MsBuildKiller msBuildKiller = msBuildKillers.Dequeue();
            killCount = await msBuildKiller.killProcessesWithLibraryLoaded(lockedFilename);
            if (killCount > 0) {
                Console.WriteLine($"{msBuildKiller.GetType().Name} killed {killCount:N0} MSBuild {(killCount == 1 ? "process" : "processes")}");
            }
        }

        if (killCount != 0) {
            try {
                Directory.TryDelete(packageDirToDelete, true);
                return;
            } catch (UnauthorizedAccessException e) {
                mostRecentException = e;
                lockedFilename      = unauthorizedAccessFilenamePattern.Match(e.Message).Groups[1].Value;
            }
        }
    } while (msBuildKillers.Count != 0);

    throw new UnauthorizedAccessException($"Could not find any running MSBuild processes to kill with {lockedFilename} loaded.", mostRecentException);
}

/*static async Task deletePackageDir(string packageDirToDelete, CancellationToken cancellationToken) {
    packageDirToDelete = Path.GetFullPath(packageDirToDelete);
    bool handleMissing = false;
    await Retrier.Attempt(_ => {
        Directory.TryDelete(packageDirToDelete, true);
        return Task.CompletedTask;
    }, new AsyncRetryOptions {
        CancellationToken = cancellationToken,
        MaxAttempts       = 5,
        IsRetryAllowed    = (e, _) => Task.FromResult(e is UnauthorizedAccessException && !handleMissing),
        BeforeRetry = async (e, _) => {
            string lockedFilename = Regex.Match(e.Message, @"^Access to the path '(.+)' is denied\.$").Groups[1].Value; // does not include a path
            (int exitCode, string stdout, string stderr) = await Process.ExecFile("ListDlls64.exe", ["-d", lockedFilename], cancellationToken: cancellationToken);
            switch (exitCode) {
                case 0:
                    /*IEnumerable<string> lines = stdout.Split('\n', StringSplitOptions.TrimEntries)
                        .Skip(5)
                        .Where(line => !line.StartsWith("Error opening") && line != "Access is denied.");#1#

                    IEnumerable<Match> matches = listDllsOutputPattern.Matches(stdout)
                        .Where(match => "MSBuild.exe".Equals(match.Groups["programFilename"].Value, StringComparison.OrdinalIgnoreCase))
                        .Where(match => match.Groups["libraryAbsolutePath"].Captures.Any(capture => {
                            string relativePath = Path.GetRelativePath(packageDirToDelete, capture.Value);
                            return !relativePath.StartsWith(".." + Path.DirectorySeparatorChar) && !Path.IsPathRooted(relativePath);
                        }));

                    /*.Select(static line => {
                                string[] s = line.Split('\t', 5);
                                return (processFilename: s[0], pid: int.Parse(s[1]), handleType: s[2], handle: s[3], handleFilename: Path.GetFullPath(s[4]));
                            })
                            .Where(static handle => "MSBuild.exe".Equals(handle.processFilename, StringComparison.OrdinalIgnoreCase)) // handle uses substring matching, but I want exact matching
                            .Where(handle => {
                                string relativePath = Path.GetRelativePath(packageDirToDelete, handle.handleFilename);
                                return !relativePath.StartsWith(".." + Path.DirectorySeparatorChar) && !Path.IsPathRooted(relativePath);
                            });#1#

                    foreach (Match match in matches) {
                        try {
                            using Process processToKill = Process.GetProcessById(Convert.ToInt32(match.Groups["pid"].Value));
                            Console.WriteLine($"Killing {processToKill.ProcessName}:{processToKill.Id} because it holds a lock on an old build of {lockedFilename}, which needs to be deleted now.");
                            processToKill.Kill();
                        } catch (Exception exception) when (exception is not OutOfMemoryException) {
                            // program holding handle already exited
                        }
                    }
                    break;
                case -1:
                    handleMissing = true;
                    Console.WriteLine(
                        $"Failed to delete old {lockedFilename} because it is in use by msbuild.exe, which we could not locate and kill because SysInternals ListDLLs could not be found. Please install ListDlls64.exe from https://learn.microsoft.com/sysinternals/downloads/listdlls to your PATH.");
                    break;
                default:
                    Console.WriteLine($"Unhandled exit code from ListDlls64: {exitCode}");
                    break;
            }
        }
    });
}*/

internal partial class Program {

    [GeneratedRegex(
        @"-{78}\n(?<programFilename>.+?) pid: (?<pid>\d+)\nCommand line: (?:.+)\n\nBase\s+Size\s+Path(?:\n(?<baseAddress>0x[\da-f]+)\s+(?<size>0x[\da-f]+)\s+(?<libraryAbsolutePath>.+))+")]
    private static partial Regex listDllsOutputPattern { get; }

    [GeneratedRegex(@"^Access to the path '(.+)' is denied\.$")]
    private static partial Regex unauthorizedAccessFilenamePattern { get; }

}