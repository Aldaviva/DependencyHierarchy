using Dependencies;
using System.Diagnostics;
using WorkspaceResolution.Data;
using Processes = Unfucked.Processes;

namespace WorkspaceResolution.Services;

public static class DotnetCliService {

    public static async Task<int> restoreOpenVisualStudioProjectsThatDependOn(string packageName, string? localPackageDir, CancellationToken cancellationToken) {
        int exitCode = 0;

        await Parallel.ForEachAsync(VisualStudioService.getOpenVisualStudioCsProjects(), cancellationToken, async (project, ct) => {
            ICollection<Dependency> dependencies = await DependencyLister.listDependencies(project.directory, ct);

            if (dependencies.FirstOrDefault(dep => dep.name.Equals(packageName, StringComparison.OrdinalIgnoreCase)) is { } dependency) {
                Stopwatch restoreStopwatch = Stopwatch.StartNew();
                int       restoreExitCode  = await restore(project, localPackageDir);
                restoreStopwatch.Stop();

                Interlocked.CompareExchange(ref exitCode, restoreExitCode, 0);
                Console.WriteLine("{0} {1} (in {2:N0} ms).", restoreExitCode == 0 ? "Restored" : "Failed to restore", project.absoluteFilename, restoreStopwatch.ElapsedMilliseconds);
            }
        });

        return exitCode;
    }

    private static async Task<int> restore(CsProject project, string? packageSource) {
        (int exitCode, string standardOutput, string standardError)? result = await Processes.ExecFile("dotnet.exe",
            "restore",
            project.absoluteFilename,
            "--force",
            "--force-evaluate",
            "--source",
            Path.GetFullPath(packageSource ?? Constants.DEFAULT_LOCAL_PACKAGE_DIR));
        return result?.exitCode ?? 1;
    }

}