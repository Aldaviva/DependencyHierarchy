using System.Text.Json;
using System.Text.Json.Nodes;

namespace PackageTools.Common;

public static class BuildHelpers {

    public static async Task<string> getPackageCacheDir(CancellationToken ct = default) =>
        Path.GetFullPath((await Process.ExecFile("dotnet", ["nuget", "locals", "global-packages", "--list"], cancellationToken: ct)).StdOut.TrimStart("global-packages: "));

    /// <exception cref="ApplicationException"></exception>
    public static async Task<PackageVersion> getPackageNameAndVersion(string projectDir, string? buildConfiguration = "Release", CancellationToken ct = default) {
        ProcessResult output = (await Process.ExecFile("dotnet", ["msbuild", projectDir, $"-p:Configuration={buildConfiguration}", "-getProperty:PackageId,Version"], cancellationToken: ct))
            .EnsureSuccessful("Failed to get package ID and version from project");

        JsonObject? properties = JsonSerializer.Deserialize<JsonObject>(output.StdOut)?["Properties"]!.AsObject();
        if (properties?["PackageId"]?.GetValue<string>() is { HasText: true } packageName) {
            return new PackageVersion { name = packageName, version = properties["Version"]!.GetValue<string>() };
        } else {
            throw new ApplicationException("Not a package project, please specify a project instead of a solution");
        }
    }

}