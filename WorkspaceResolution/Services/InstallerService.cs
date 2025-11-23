using System.Reflection;
using System.Security;
using System.Text;

namespace WorkspaceResolution.Services;

public static class InstallerService {

    public static async Task<int> install(string? localPackageDirectory, CancellationToken cancellationToken) {
        localPackageDirectory = localPackageDirectory != null ? Path.GetFullPath(localPackageDirectory) : Constants.DEFAULT_LOCAL_PACKAGE_DIR;
        Directory.CreateDirectory(localPackageDirectory);

        bool sourceAlreadyAdded = (await DotnetCliService.listPackageSources(cancellationToken))
            .Any(source => source.isEnabled &&
                Path.IsPathFullyQualified(source.url) &&
                Path.GetFullPath(source.url).Equals(localPackageDirectory, StringComparison.OrdinalIgnoreCase));

        if (!sourceAlreadyAdded) {
            try {
                await DotnetCliService.addPackageSource("Workspace Resolution", localPackageDirectory, cancellationToken);
                Console.WriteLine("Added local NuGet package source: {0}", localPackageDirectory);
            } catch (ApplicationException e) {
                return e.Data["exitCode"] as int? ?? 1;
            }
        }

        string msbuildHookFile = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\MSBuild\Current\Imports\Microsoft.Common.Props\ImportBefore\WorkspaceResolution.props");
        Directory.CreateDirectory(Path.GetDirectoryName(msbuildHookFile)!);
        await using FileStream   hookFileStream  = new(msbuildHookFile, FileMode.Create, FileAccess.Write);
        await using StreamWriter fileWriter      = new(hookFileStream, new UTF8Encoding(false, true));
        using StreamReader       propsFileReader = new(Assembly.GetExecutingAssembly().GetManifestResourceStream("WorkspaceResolution.props")!, Encoding.UTF8);
        // ReSharper disable once MethodHasAsyncOverloadWithCancellation - it's already in memory as part of the assembly
        string propsFileContents = propsFileReader.ReadToEnd();
        propsFileContents = propsFileContents.Replace("<!--$LOCAL_PACKAGE_DIR$-->", SecurityElement.Escape(localPackageDirectory.TrimEnd('\\')), StringComparison.Ordinal);
        await fileWriter.WriteAsync(propsFileContents);
        Console.WriteLine("Hooked MSBuild pack: {0}", msbuildHookFile);
        Console.WriteLine("To make it faster and easier to package dependencies, you may want to set the GeneratePackageOnBuild property to true in your dependency projects.");
        return 0;
    }

}