using System.Security;
using System.Text;
using Unfucked;

namespace WorkspaceResolution.Services;

public static class InstallerService {

    public static async Task<int> install(string? localPackageDirectory, CancellationToken cancellationToken) {
        string effectiveLocalPackageDirectory = localPackageDirectory ?? Constants.DEFAULT_LOCAL_PACKAGE_DIR;
        localPackageDirectory = Path.GetFullPath(effectiveLocalPackageDirectory);
        Directory.CreateDirectory(effectiveLocalPackageDirectory);

        (int exitCode, string stdout, string stderr) sourcesList = await Processes.ExecFile("dotnet", [
            "nuget",
            "list",
            "source",
            "--format", "short"
        ], cancellationToken: cancellationToken);

        bool sourceAlreadyAdded = sourcesList.stdout
            .Trim()
            .Split('\n')
            .Select(line => {
                string[] lineSplit = line.Trim().Split(' ', 2);
                return (isEnabled: lineSplit[0] == "E", url: lineSplit[1]);
            })
            .Any(source => source.isEnabled &&
                Path.IsPathFullyQualified(source.url) &&
                Path.GetFullPath(source.url).Equals(effectiveLocalPackageDirectory, StringComparison.OrdinalIgnoreCase));

        if (!sourceAlreadyAdded) {
            (int exitCode, string stdout, string stderr) sourceAdded = await Processes.ExecFile("dotnet", [
                "nuget",
                "add",
                "source", effectiveLocalPackageDirectory,
                "--name", "Workspace Resolution"
            ], cancellationToken: cancellationToken);

            if (sourceAdded.exitCode != 0) {
                return sourceAdded.exitCode;
            }
        }

        string msbuildHookFile = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\MSBuild\Current\Imports\Microsoft.Common.Props\ImportBefore\WorkspaceResolution.props");
        Directory.CreateDirectory(Path.GetDirectoryName(msbuildHookFile)!);
        FileStream hookFileStream;
        try {
            hookFileStream = new FileStream(msbuildHookFile, FileMode.CreateNew, FileAccess.Write);
        } catch (IOException e) when ((ushort) e.HResult == 80) {
            // File already exists, skip
            return 0;
        }

        await using StreamWriter fileWriter = new(hookFileStream, new UTF8Encoding(false, true));
        await fileWriter.WriteAsync(generateMsBuildHookFile(localPackageDirectory));
        return 0;
    }

    private static string generateMsBuildHookFile(string? localPackageDirectory) => /* language=xml */ """
        <?xml version="1.0" encoding="utf-8"?>
        <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
            
            <Target Name="_ResolveWorkspace" AfterTargets="Pack">
                <PropertyGroup>
                    <_LocalPackageDir>LOCAL_PACKAGE_DIR</_LocalPackageDir>
                </PropertyGroup>

                <ItemGroup>
                    <_WorkspacePackages Include="@(NuGetPackOutput)" Condition="$([System.String]::new('%(NuGetPackOutput.Identity)').ToLowerInvariant().EndsWith('nupkg'))" />
                </ItemGroup>

                <MakeDir Directories="$(_LocalPackageDir)" />
                <Copy SourceFiles="@(_WorkspacePackages)" DestinationFolder="$(_LocalPackageDir)" />
                <RemoveDir Directories="$(NuGetPackageRoot)\$(PackageId)\$(PackageVersion)\" />
                <Exec Command="WorkspaceResolution.exe &quot;$(PackageId)&quot; &quot;$(_LocalPackageDir)&quot;" />
                <Message Text="Installed $(PackageId) $(PackageVersion) into local package cache and restored any dependent projects currently open in Visual Studio" />
            </Target>

        </Project>
        """
        .Replace("LOCAL_PACKAGE_DIR", SecurityElement.Escape(localPackageDirectory ?? @"$(NuGetPackageRoot)\..\local\"), StringComparison.Ordinal);

}