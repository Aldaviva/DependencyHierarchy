using System.Diagnostics;
using Unfucked.Windows;

namespace WorkspaceResolution.Services.MsBuild;

public abstract class MsBuildKiller {

    public abstract Task<int> killProcessesWithLibraryLoaded(string libraryFilename);

    protected static readonly string[] PROCESS_NAMES = ["msbuild", "dotnet"];

    /// <summary>Don't kill ourselves.</summary>
    /// <param name="potentialAncestorOrSelfPid">MSBuild PID, which may or may not be in the parent PID chain of the current process</param>
    /// <returns><c>true</c> if <paramref name="potentialAncestorOrSelfPid"/> is the PID of the current process, its parent, grandparent, or any ancestors; or <c>false</c> if not.</returns>
    protected static bool isParentOfCurrentProcess(int potentialAncestorOrSelfPid) {
        Stack<Process?> ancestors = new([Process.GetCurrentProcess()]);
        try {
            for (; ancestors.Peek() != null; ancestors.Push(ancestors.Peek()?.Parent)) {
                if (ancestors.Peek()?.Id == potentialAncestorOrSelfPid) {
                    return true;
                }
            }
            return false;
        } finally {
            foreach (Process? process in ancestors) {
                process?.Dispose();
            }
        }
    }

}