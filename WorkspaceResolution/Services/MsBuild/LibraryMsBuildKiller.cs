using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WorkspaceResolution.Services.MsBuild;

public sealed partial class LibraryMsBuildKiller: MsBuildKiller {

    public override async Task<int> killProcessesWithLibraryLoaded(string libraryFilename) {
        (int exitCode, string stdOut) = await Process.ExecFile("ListDLLs64.exe", "-d", libraryFilename);

        switch (exitCode) {
            case 0:
                return listDllsOutputPattern.Matches(stdOut)
                    .Where(match => PROCESS_NAMES.Contains(match.Groups["programFilename"].Value, StringComparer.OrdinalIgnoreCase))
                    .Select(match => int.Parse(match.Groups["pid"].Value))
                    .Where(pid => !isParentOfCurrentProcess(pid))
                    .Sum(pid => {
                        try {
                            using Process processToKill = Process.GetProcessById(pid);
                            string        message       = $"Killed {processToKill.ProcessName}:{processToKill.Id} because it held a filesystem lock on {libraryFilename} (by DLL)";
                            processToKill.Kill();
                            Console.WriteLine(message);
                        } catch (Exception e) when (e is InvalidOperationException or Win32Exception or ArgumentException) {}
                        return 1;
                    });
            case -1:
                Console.WriteLine(
                    "ListDLLs64.exe was not found. To help kill MSBuild when it holds a lock on task libraries that need to be overwitten by Workspace Resolution, please download it from https://learn.microsoft.com/sysinternals/downloads/listdlls and extract it to your PATH.");
                return 0;
            default:
                Console.WriteLine($"ListDLLs64 exited with code {exitCode}: {stdOut}");
                return 0; // some other error from ListDLLs64.exe
        }
    }

    [GeneratedRegex(
        @"-{78}\n(?<programFilename>.+?) pid: (?<pid>\d+)\nCommand line: (?:.+)\n\nBase\s+Size\s+Path(?:\n(?<baseAddress>0x[\da-f]+)\s+(?<size>0x[\da-f]+)\s+(?<libraryAbsolutePath>.+))+")]
    private static partial Regex listDllsOutputPattern { get; }

}