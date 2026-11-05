using System.Diagnostics;

namespace WorkspaceResolution.Services;

public static class NuGetCliService {

    private const string NUGET = "nuget";

    /// <exception cref="ProcessException"></exception>
    public static async Task installLocalPackage(string packageFilePath, string destinationDirectory, CancellationToken cancellationToken = default) =>
        (await Process.ExecFile(NUGET, ["add", packageFilePath, "-Source", destinationDirectory, "-Expand", "-NonInteractive"], cancellationToken: cancellationToken))
        .EnsureSuccessful("Installation failed to copy package to local cache", ProcessResult.Details.All);

}