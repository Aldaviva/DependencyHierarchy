using Gapotchenko.FX.Diagnostics;
using System.Diagnostics;

namespace WorkspaceResolution.Services;

public static class MsBuildService {

    public static uint killProcessesWithTaskLibraryLoaded(string libraryFilename) {
        uint processesKilled = 0;
        foreach (var msbuildProcess in Process.GetProcessesByName("msbuild")) {
            using (msbuildProcess) {
                try {
                    bool shouldKill = msbuildProcess
                        .ReadEnvironmentVariables()["MSBUILD_TASKS"]? // from Unfucked.BuildInfoGenerator.GenerateBuildInfo
                        .Split(';')
                        .Contains(libraryFilename, StringComparer.OrdinalIgnoreCase) ?? false;

                    if (shouldKill) {
                        string message = $"Killed {msbuildProcess.ProcessName}:{msbuildProcess.Id} because it held a filesystem lock on {libraryFilename}";
                        msbuildProcess.Kill();
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