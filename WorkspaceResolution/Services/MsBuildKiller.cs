using Gapotchenko.FX.Diagnostics;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WorkspaceResolution.Services;

public interface MsBuildKiller {

    Task<int> killProcessesWithLibraryLoaded(string libraryFilename);

    protected static readonly string[] PROCESS_NAMES = ["msbuild", "dotnet"];

}

public sealed class EnvironmentVariableMsBuildKiller: MsBuildKiller {

    public Task<int> killProcessesWithLibraryLoaded(string libraryFilename) {
        int processesKilled = 0;
        foreach (var processes in MsBuildKiller.PROCESS_NAMES.SelectMany(Process.GetProcessesByName)) {
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
        return Task.FromResult(processesKilled);
    }

}

public sealed class HandleMsBuildKiller: MsBuildKiller {

    public async Task<int> killProcessesWithLibraryLoaded(string libraryFilename) {
        string[] args = ["-nobanner", "-vt", libraryFilename];
        (int exitCode, string stdOut) = await ((IEnumerable<string>) [
                .. RuntimeInformation.OSArchitecture == Architecture.Arm64 ? (ReadOnlySpan<string>) ["handle64a.exe"] : [],
                .. Environment.Is64BitOperatingSystem ? (ReadOnlySpan<string>) ["handle64.exe"] : [],
                "handle.exe"
            ]).ToAsyncEnumerable()
            .Select(async (programFilename, ct) => await Process.ExecFile(programFilename, args, cancellationToken: ct))
            .FirstOrDefaultAsync((processResult, _) => ValueTask.FromResult(processResult.IsSuccessful));

        switch (exitCode) {
            case 0:
                return stdOut.Split("\r\n")
                    .Skip(1)
                    .Select(row => new HandleRow(row))
                    .Where(handle => MsBuildKiller.PROCESS_NAMES.Contains(Process.StripExeSuffix(handle.processName), StringComparer.OrdinalIgnoreCase))
                    .Where(handle => libraryFilename.Equals(Path.GetFileName(handle.handleName), StringComparison.OrdinalIgnoreCase))
                    .Select(handle => {
                        try {
                            using Process processToKill = Process.GetProcessById(handle.processId);
                            processToKill.Kill();
                            return 1;
                        } catch (InvalidOperationException) {} catch (Win32Exception) {} catch (ArgumentException) {} // return 0 below
                        return 0;
                    })
                    .Sum();
            case 1:
                return 0; // no matching handles found
            case -1:
                Console.WriteLine(
                    "handle64.exe was not found. To help kill MSBuild when it holds a lock on task libraries that need to be overwitten by Workspace Resolution, please download it from https://learn.microsoft.com/sysinternals/downloads/handle and extract it to your PATH.");
                return 0;
            default:
                Console.WriteLine($"handle exited with code {exitCode}: {stdOut}");
                return 0; // some other error from handle.exe
        }
    }

    private readonly record struct HandleRow {

        public readonly string processName;
        public readonly int    processId;
        public readonly string handleName;

        public HandleRow(string row) {
            string[] columns = row.Split('\t', 5);
            processName = columns[0];
            processId   = int.Parse(columns[1]);
            handleName  = columns[4];
        }

    }

}