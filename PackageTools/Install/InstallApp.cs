namespace PackageTools.Install;

public class InstallApp: AbstractApp {

    public static Task<int> Main() => CommandLineApplicationFactory.execute<InstallApp>("install", app =>
        app.Description = "Pack a project into a package and copy it to the local package cache directory.");

    public override async Task<int> OnExecuteAsync(CancellationToken ct) {
        string         localRepo = await BuildHelpers.getPackageCacheDir(ct);
        PackageVersion package   = await BuildHelpers.getPackageNameAndVersion(projectDir, "Debug", ct);

        Console.WriteLine("Restoring...");
        (await Process.ExecFile("dotnet", ["restore", projectDir, "-p:Configuration=Debug", .. RemainingArguments], cancellationToken: ct))
            .EnsureSuccessful("Restoration failed");
        Console.WriteLine("Building...");
        (await Process.ExecFile("dotnet", ["build", projectDir, "--configuration", "Debug", "--no-restore", "-p:GeneratePackageOnBuild=false", .. RemainingArguments], cancellationToken: ct))
            .EnsureSuccessful("Build failed");

        DirectoryInfo tempPackDir = Directory.CreateTempSubdirectory("PackageTools.Install.");
        try {
            Console.WriteLine("Packing...");
            (await Process.ExecFile("dotnet", ["pack", projectDir, "--configuration", "Debug", "--output", tempPackDir.FullName, "--no-build", "-p:WorkspaceResolution=false", .. RemainingArguments],
                    cancellationToken: ct))
                .EnsureSuccessful("Pack failed");

            string tempPackageFilePath = Path.Combine(tempPackDir.FullName, $"{package.name}.{package.version}.nupkg");

            Directory.TryDelete(Path.Combine(localRepo, package.name, package.version), true);

            Console.WriteLine("Installing...");
            (await Process.ExecFile("nuget", ["add", tempPackageFilePath, "-Source", localRepo, "-Expand", "-NonInteractive"], cancellationToken: ct))
                .EnsureSuccessful("Installation failed to copy package to local cache");

            Console.WriteLine($"Installed {package.name} {package.version} to local package cache. Remember to `dotnet restore!` in dependent projects.");
        } finally {
            tempPackDir.Delete(true);
        }
        return 0;
    }

}