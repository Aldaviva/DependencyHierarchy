using EnvDTE;
using Microsoft.VisualStudio.OLE.Interop;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using WorkspaceResolution.Data;

namespace WorkspaceResolution.Services;

public static partial class VisualStudioService {

    private const string DESIRED_DISPLAY_NAME_PREFIX = "!VisualStudio";

    // https://stackoverflow.com/a/53485177/979493
    private static readonly FrozenSet<Guid> CS_PROJECT_KINDS = [new("FAE04EC0-301F-11D3-BF4B-00C04F79EFBC"), new("9A19103F-16F7-4668-BE54-9A1E7A4F7556")];

    public static async IAsyncEnumerable<CsProject> findOpenCsProjects([EnumeratorCancellation] CancellationToken cancellationToken = default) {
        var projectQueue = Channel.CreateUnbounded<CsProject>(new UnboundedChannelOptions { SingleReader = true });

        _ = Parallel.ForEachAsync(findRunningInstances(cancellationToken), cancellationToken, async (vs, ct) => {
            Solution solution                 = vs.Solution;
            string   solutionAbsoluteFilename = Path.GetFullPath(solution.FullName);
            foreach (Project proj in solution.Projects.Cast<Project>()) {
                Guid projectKind = new(proj.Kind);
                if (CS_PROJECT_KINDS.Contains(projectKind)) {
                    CsProject project = new(proj.Name, Path.GetFullPath(proj.FullName), projectKind, solutionAbsoluteFilename);
                    await projectQueue.Writer.WriteAsync(project, ct);
                }
            }
        }).ContinueWith(task => projectQueue.Writer.Complete(task.Exception?.GetBaseException()), cancellationToken);

        await foreach (CsProject project in projectQueue.Reader.ReadAllAsync(cancellationToken)) {
            yield return project;
        }
    }

    // https://stackoverflow.com/a/14205934
    private static IEnumerable<DTE> findRunningInstances(CancellationToken ct = default) {
        Marshal.ThrowExceptionForHR(GetRunningObjectTable(0, out IRunningObjectTable rot));
        rot.EnumRunning(out IEnumMoniker enumMoniker);

        var moniker = new IMoniker[1];
        while (!ct.IsCancellationRequested && enumMoniker.Next(1, moniker, out uint fetched) == 0) {
            Marshal.ThrowExceptionForHR(CreateBindCtx(0, out IBindCtx bindCtx));

            string displayName;
            try {
                moniker[0].GetDisplayName(bindCtx, null, out displayName);
            } catch (UnauthorizedAccessException) {
                continue;
            }

            bool isVisualStudio = displayName.StartsWith(DESIRED_DISPLAY_NAME_PREFIX, StringComparison.Ordinal);
            if (isVisualStudio) {
                rot.GetObject(moniker[0], out object obj);
                if (obj is DTE dte) {
                    yield return dte;
                }
            }
        }
    }

// LibraryImport breaks build due to interface parameters, and I can't figure out ComWrappers, even with the source generator, because I keep getting "ArgumentException: Value does not fall within the expected range"
#pragma warning disable SYSLIB1054

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(uint reserved, out IBindCtx ppbc);

    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable prot);

#pragma warning restore SYSLIB1054

}