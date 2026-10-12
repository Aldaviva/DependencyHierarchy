using Gapotchenko.FX.Diagnostics;
using System.Diagnostics;

namespace WorkspaceResolution.Services;

public static class MsBuildService {

    public static uint killProcessesWithTaskLibraryLoaded(string libraryFilename) {
        uint processesKilled = 0;
        foreach (var processes in ((string[]) ["msbuild", "dotnet"]).SelectMany(Process.GetProcessesByName)) {
            using (processes) {
                try {
                    bool shouldKill = processes
                        .ReadEnvironmentVariables()
                        .GetValueOrDefault("MSBUILD_TASKS")? // from Unfucked.BuildInfoGenerator.GenerateBuildInfo
                        .Split(';')
                        .Contains(libraryFilename, StringComparer.OrdinalIgnoreCase) ?? false;

                    if (shouldKill) {
                        string message = $"Killed {processes.ProcessName}:{processes.Id} because it held a filesystem lock on {libraryFilename}";
                        processes.Kill();
                        processesKilled++;
                        Console.WriteLine(message);
                    }
                } catch (InvalidOperationException) {
                    processesKilled++;
                    // race: process already exited before we could inspect or kill it
                }
            }
        }
        return processesKilled;
    }

}