using EnvDTE;
using System.Collections.Frozen;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using WorkspaceResolution.Data;

namespace WorkspaceResolution.Services;

public static class VisualStudioService {

    private const string DESIRED_DISPLAY_NAME_PREFIX = "!VisualStudio";

    // https://stackoverflow.com/a/53485177/979493
    private static readonly FrozenSet<Guid> CS_PROJECT_KINDS = [new("FAE04EC0-301F-11D3-BF4B-00C04F79EFBC"), new("9A19103F-16F7-4668-BE54-9A1E7A4F7556")];

    public static IEnumerable<CsProject> getOpenVisualStudioCsProjects() => getRunningVisualStudioInstances()
        .SelectMany(app => app.Solution.Projects.Cast<Project>())
        .Where(project => CS_PROJECT_KINDS.Contains(new Guid(project.Kind)))
        .Select(proj => new CsProject(
            name: proj.Name,
            directory: proj.Properties.Cast<Property>().FirstOrDefault(prop => prop.Name == "FullPath")?.Value as string ?? Path.GetDirectoryName(proj.FullName)!,
            absoluteFilename: proj.FullName,
            kind: new Guid(proj.Kind)));

    // https://stackoverflow.com/a/14205934
    private static IEnumerable<DTE> getRunningVisualStudioInstances() {
        Marshal.ThrowExceptionForHR(GetRunningObjectTable(0, out IRunningObjectTable rot));
        rot.EnumRunning(out IEnumMoniker enumMoniker);

        var moniker = new IMoniker[1];
        while (enumMoniker.Next(1, moniker, 0) == 0) {
            Marshal.ThrowExceptionForHR(CreateBindCtx(0, out IBindCtx bindCtx));

            string displayName;
            try {
                moniker[0].GetDisplayName(bindCtx, null, out displayName);
            } catch (UnauthorizedAccessException) {
                continue;
            }

            bool isVisualStudio = displayName.StartsWith(DESIRED_DISPLAY_NAME_PREFIX, StringComparison.Ordinal);
            if (isVisualStudio) {
                Marshal.ThrowExceptionForHR(rot.GetObject(moniker[0], out object obj));
                if (obj is DTE dte) {
                    yield return dte;
                }
            }
        }
    }

#pragma warning disable SYSLIB1054 // LibraryImport breaks build due to interface parameters

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(uint reserved, out IBindCtx ppbc);

    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable prot);

#pragma warning restore SYSLIB1054

}