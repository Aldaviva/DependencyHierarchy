namespace PackageTools.Deploy;

public class DeployApp: AbstractApp {

    private static readonly UrlBuilder NUGET_GALLERY_PACKAGE_PAGE = new("https://www.nuget.org/packages/{name}/{version}");

    public static Task<int> Main() => CommandLineApplicationFactory.execute<DeployApp>("deploy", app =>
        app.Description = "Pack a project into a package and upload it to NuGet Gallery.");

    public override async Task<int> OnExecuteAsync(CancellationToken ct) {
        string         localRepo = await BuildHelpers.getPackageCacheDir(ct);
        PackageVersion package   = await BuildHelpers.getPackageNameAndVersion(projectDir, ct: ct);

        Console.WriteLine("Building...");
        (await Process.ExecFile("dotnet", ["build", projectDir, "--configuration", "Release", "-p:WorkspaceResolution=false", .. RemainingArguments]))
            .EnsureSuccessful("Build failed");

        DirectoryInfo tempPackDir = Directory.CreateTempSubdirectory("PackageTools.Deploy.");
        try {
            Console.WriteLine("Packing...");
            (await Process.ExecFile("dotnet", [
                "pack",
                projectDir,
                "--configuration", "Release",
                "--output", tempPackDir.FullName,
                "--no-build",
                "-p:WorkspaceResolution=false",
                .. RemainingArguments
            ])).EnsureSuccessful("Pack failed");

            string tempPackageFilePath = Path.Combine(tempPackDir.FullName, $"{package.name}.{package.version}.nupkg");
            if (!File.Exists(tempPackageFilePath)) {
                throw new FileNotFoundException("Packed file could not be found", tempPackageFilePath);
            }

            Console.WriteLine($"Packed {package.name} {package.version} to {tempPackageFilePath}");
            Console.WriteLine("Installing...");
            Directory.TryDelete(Path.Combine(localRepo, package.name, package.version), true);

            (await Process.ExecFile("nuget", "add", tempPackageFilePath, "-Source", localRepo, "-Expand", "-NonInteractive"))
                .EnsureSuccessful("Deployment failed to copy package to local cache");

            Console.WriteLine($"Installed {package.name} {package.version} to local package cache. Remember to `dotnet restore!` in dependent projects.");
            Console.WriteLine("Deploying to NuGet Gallery...");

            foreach (string nugetPackageListCacheFile in Directory.EnumerateFiles(Path.Combine(localRepo, "NuGet", "v3-cache"), $"list_{package.name.ToLowerInvariant()}.dat",
                         SearchOption.AllDirectories)) {
                Files.Delete(nugetPackageListCacheFile);
            }

            (await Process.ExecFile("dotnet", ["nuget", "push", tempPackageFilePath, .. RemainingArguments])).EnsureSuccessful("Push failed");

            Console.WriteLine($"Deployed {package.name} {package.version} to NuGet Gallery: {NUGET_GALLERY_PACKAGE_PAGE.ToUrl(package.name, package.version)}");
        } finally {
            tempPackDir.Delete(true);
        }
        return 0;
    }

}