using McMaster.Extensions.CommandLineUtils;
using NuGetGalleryClient;
using System.Reflection;
using System.Text.RegularExpressions;
using WorkspaceResolution;
using WorkspaceResolution.Services;

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

CommandOption localPackageDir = app.Option("--local-package-dir",
    $"Directory for local packages, which should also be a NuGet package source in nuget.config; defaults to {Constants.DEFAULT_LOCAL_PACKAGE_DIR}", CommandOptionType.SingleValue, true);

app.Command("restore-dependents", restoreCommand => {
    CommandArgument packageName     = restoreCommand.Argument("packageId", "Name (ID) of the NuGet package").IsRequired();
    CommandArgument packageVersion  = restoreCommand.Argument("packageVersion", "Version of the NuGet package").IsRequired();
    CommandArgument packageFilename = restoreCommand.Argument("packageFile", "Filename of .nupkg package").IsRequired();
    CommandOption skipSolutionFile = restoreCommand.Option("--skip-solution", "When restoring dependent projects, skip projects in the solution defined by this .sln filename",
        CommandOptionType.SingleValue);
    CommandOption buildAfterRestore = restoreCommand.Option("--build-dependent-packages",
        "After restoring a dependent package project, build it, which is useful if its transitive dependents need to be restored too", CommandOptionType.NoValue);

    restoreCommand.OnExecuteAsync(async ct => {
        string localPackageDirectory = localPackageDir.Value() is {} dir ? Path.GetFullPath(dir) : Constants.DEFAULT_LOCAL_PACKAGE_DIR;
        Directory.CreateDirectory(localPackageDirectory);

        using NuGetGalleryService        nuGetGalleryService    = new();
        NuGetGalleryPackageVersionsCache galleryPackageVersions = new(nuGetGalleryService);
        if (await galleryPackageVersions.doesPackageVersionExist(packageName.Value!, packageVersion.Value!, localPackageDirectory, ct)) {
            Console.WriteLine("Package {0} {1} already exists in NuGet Gallery, so not installing locally-built package into user package cache.", packageName.Value, packageVersion.Value);
            return;
        }

        File.Copy(packageFilename.Value!, Path.Combine(localPackageDirectory, Path.GetFileName(packageFilename.Value!)), true);
        try {
            File.Copy(Path.ChangeExtension(packageFilename.Value!, ".snupkg"), Path.Combine(localPackageDirectory, Path.GetFileNameWithoutExtension(packageFilename.Value!) + ".snupkg"), true);
        } catch (FileNotFoundException) {} // no symbol package created, so nothing to copy
        deletePackageDir(Path.Combine(Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\.nuget\packages"), packageName.Value!));
        Console.WriteLine("Installed {0} {1} into local NuGet package cache.", packageName.Value, packageVersion.Value);

        await DotnetCliService.restoreOpenVisualStudioProjectsThatDependOn(packageName.Value!, skipSolutionFile.Value(), buildAfterRestore.HasValue(), localPackageDirectory, ct);
    });
});

app.Command("install", installCommand => installCommand.OnExecuteAsync(async ct => await InstallerService.install(localPackageDir.Value(), ct)));

return await app.ExecuteAsync(args);

static void deletePackageDir(string packageDirToDelete) {
    try {
        Directory.Delete(packageDirToDelete, true);
    } catch (UnauthorizedAccessException e) {
        string lockedFilename = Regex.Match(e.Message, @"^Access to the path '(.+)' is denied\.$").Groups[1].Value; // does not include a path
        if (MsBuildService.killProcessesWithTaskLibraryLoaded(lockedFilename) != 0) {
            deletePackageDir(packageDirToDelete);
        } else {
            throw new UnauthorizedAccessException($"Could not find any running MSBuild processes to kill with {lockedFilename} loaded.", e);
        }
    }
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
                        $"Failed to delete old {lockedFilename} because it is in use by msbuild.exe, which we could not locate and kill because SysInternals ListDLLs could not be found. Please install ListDlls64.exe from https://learn.microsoft.com/en-us/sysinternals/downloads/listdlls to your PATH.");
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

}