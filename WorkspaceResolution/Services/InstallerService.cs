using System.Reflection;

namespace WorkspaceResolution.Services;

public static class InstallerService {

    public static async Task<int> install(CancellationToken cancellationToken) {
        string msbuildHookFile = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\MSBuild\Current\Imports\Microsoft.Common.Props\ImportBefore\WorkspaceResolution.props");
        Directory.CreateDirectory(Path.GetDirectoryName(msbuildHookFile)!);
        await using FileStream hookFileStream      = new(msbuildHookFile, FileMode.Create, FileAccess.Write);
        await using Stream     propsResourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WorkspaceResolution.props")!;
        await propsResourceStream.CopyToAsync(hookFileStream, cancellationToken);
        Console.WriteLine("Hooked MSBuild pack: {0}", msbuildHookFile);
        Console.WriteLine("To make it faster and easier to package dependencies, you may want to set the GeneratePackageOnBuild property to true in your dependency projects.");
        return 0;
    }

}