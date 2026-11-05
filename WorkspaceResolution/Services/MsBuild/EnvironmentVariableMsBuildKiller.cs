using Gapotchenko.FX.Diagnostics;
using System.Diagnostics;

namespace WorkspaceResolution.Services.MsBuild;

public sealed class EnvironmentVariableMsBuildKiller: MsBuildKiller {

    public override Task<int> killProcessesWithLibraryLoaded(string libraryFilename) => Task.FromResult(
        PROCESS_NAMES.SelectMany(Process.GetProcessesByName).Sum(process => {
            using (process) {
                try {
                    bool shouldKill = !isParentOfCurrentProcess(process.Id) &&
                        (process.ReadEnvironmentVariables()
                            .GetValueOrDefault("MSBUILD_TASKS")? // from Unfucked.BuildInfoGenerator.GenerateBuildInfo
                            .Split(';')
                            .Contains(libraryFilename, StringComparer.OrdinalIgnoreCase) ?? false);

                    if (shouldKill) {
                        string message = $"Killed {process.ProcessName}:{process.Id} because it held a filesystem lock on {libraryFilename} (by env var)";
                        process.Kill();
                        Console.WriteLine(message);
                    } else {
                        return 0;
                    }
                } catch (InvalidOperationException) {
                    // race: process already exited before we could inspect or kill it
                }
                return 1;
            }
        }));

}