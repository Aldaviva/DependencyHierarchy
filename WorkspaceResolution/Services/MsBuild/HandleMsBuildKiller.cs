using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WorkspaceResolution.Services.MsBuild;

public sealed class HandleMsBuildKiller: MsBuildKiller {

    public override async Task<int> killProcessesWithLibraryLoaded(string libraryFilename) {
        string handleProgramFilename = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "handle64a.exe" : "handle64.exe";
        (int exitCode, string stdOut) = await Process.ExecFile(handleProgramFilename, "-nobanner", "-vt", libraryFilename);

        switch (exitCode) {
            case 0:
                return stdOut.Split("\r\n")
                    .Skip(1)
                    .Select(row => new HandleRow(row))
                    .Where(handle => PROCESS_NAMES.Contains(Process.StripExeSuffix(handle.processName), StringComparer.OrdinalIgnoreCase))
                    .Where(handle => libraryFilename.Equals(Path.GetFileName(handle.handleName), StringComparison.OrdinalIgnoreCase))
                    .Where(handle => {
                        bool parentOfCurrentProcess = isParentOfCurrentProcess(handle.processId);
                        if (parentOfCurrentProcess) {
                            Console.WriteLine(
                                $"Not killing {handle.processName}:{handle.processId} because it is a parent of the current process {Path.GetFileName(Environment.ProcessPath)}:{Environment.ProcessId}");
                        }
                        return !parentOfCurrentProcess;
                    })
                    .Sum(handle => {
                        try {
                            using Process processToKill = Process.GetProcessById(handle.processId);
                            string        message       = $"Killed {processToKill.ProcessName}:{processToKill.Id} because it held a filesystem lock on {libraryFilename} (by handle)";
                            processToKill.Kill();
                            Console.WriteLine(message);
                        } catch (Exception e) when (e is InvalidOperationException or Win32Exception or ArgumentException) {}
                        return 1;
                    });
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