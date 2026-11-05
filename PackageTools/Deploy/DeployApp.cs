using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PackageTools.Deploy;

[Subcommand(typeof(DiscoverPackageLockFile))]
public partial class DeployApp: AbstractApp {

    private static readonly UrlBuilder            NUGET_GALLERY_PACKAGE_PAGE = UrlBuilder.FromTemplate("https://www.nuget.org/packages/{name}/{version}");
    private static readonly JsonSerializerOptions JSON_OPTIONS               = new() { WriteIndented = true };

    private static string packageLockFileSetFilename = null!;

    [Option("--source", "Package destination (URL, UNC/folder path, or package source name) to push to. Defaults to DefaultPushSource if specified in NuGet.Config.", CommandOptionType.SingleValue,
        ValueName = "source")]
    public string? packagePushDestination { get; init; }

    public static async Task<int> Main() {
        packageLockFileSetFilename = Path.GetFullPath(Path.Combine(await BuildHelpers.getPackageCacheDir(), "..", "packages.locks.json"));
        return await CommandLineApplicationFactory.execute<DeployApp>("deploy", app =>
            app.Description = "Pack a project into a package and upload it to NuGet Gallery.");
    }

    public override async Task<int> OnExecuteAsync(CancellationToken ct) {
        string         localRepo            = await BuildHelpers.getPackageCacheDir(ct);
        PackageVersion package              = await BuildHelpers.getPackageNameAndVersion(projectDir, ct: ct);
        string         localPackageCacheDir = Path.Combine(localRepo, package.name, package.version);

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

            Directory.TryDelete(localPackageCacheDir, true);

            (await Process.ExecFile("nuget", "add", tempPackageFilePath, "-Source", localRepo, "-Expand", "-NonInteractive"))
                .EnsureSuccessful("Deployment failed to copy package to local cache");

            Console.WriteLine($"Installed {package.name} {package.version} to local package cache. Remember to `dotnet restore!` in dependent projects.");
            Console.WriteLine("Deploying to NuGet Gallery...");

            foreach (string nugetPackageListCacheFile in Directory.EnumerateFiles(Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\NuGet\v3-cache"),
                         $"list_{package.name.ToLowerInvariant()}.dat", SearchOption.AllDirectories)) {
                Files.Delete(nugetPackageListCacheFile);
            }

            (await Process.ExecFile("dotnet", [
                "nuget", "push", tempPackageFilePath,
                .. packagePushDestination != null ? (ReadOnlySpan<string>) ["--source", packagePushDestination] : [],
            ])).EnsureSuccessful("Push failed");

            Console.WriteLine($"Deployed {package.name} {package.version} to NuGet Gallery: {NUGET_GALLERY_PACKAGE_PAGE.ToUrl(package.name, package.version).AbsoluteUri}");

            await updatePackageCacheMetadataSourceUrl(localPackageCacheDir, ct);

            await forceRestoreDependentProjects(package.name, package.version, localPackageCacheDir, ct);

        } finally {
            tempPackDir.Delete(true);
        }
        return 0;
    }

    private async Task updatePackageCacheMetadataSourceUrl(string localPackageCacheDir, CancellationToken ct = default) {
        string pushDestinationUrl = packagePushDestination ??
            (await Process.ExecFile("dotnet", "nuget", "config", "get", "defaultPushSource"))
            .EnsureSuccessful("Getting NuGet default push source failed").StdOut.EmptyToNull ??
            "https://api.nuget.org/v3/index.json";

        if (!Uri.TryCreate(pushDestinationUrl, UriKind.Absolute, out _)) {
            pushDestinationUrl = (await Process.ExecFile("dotnet", "nuget", "list", "source", "--format", "Detailed", "--force-english-output"))
                .EnsureSuccessful("Failed to list NuGet sources")
                .StdOut
                .Split('\n')
                .Chunk(2)
                .Select(lines => {
                    Match match = nuGetDetailedSource.Match(lines[0]);
                    return (isEnabled: "Enabled".Equals(match.Groups["enablement"].Value, StringComparison.CurrentCulture),
                            name: match.Groups["name"].Value,
                            url: lines[1].Trim());
                })
                .FirstOrNull(dest => dest.isEnabled && pushDestinationUrl.Equals(dest.name, StringComparison.CurrentCulture)) is
                {} p
                    ? p.url
                    : throw new ApplicationException($"Unknown package push destination {pushDestinationUrl}");
        }

        await using FileStream localPackageCacheMetadataStream = File.Open(Path.Combine(localPackageCacheDir, ".nupkg.metadata"), FileMode.Open, FileAccess.ReadWrite);

        JsonNode localPackageCacheMetadata = (await JsonSerializer.DeserializeAsync<JsonNode>(localPackageCacheMetadataStream, JSON_OPTIONS, ct))!;
        localPackageCacheMetadata["source"] = pushDestinationUrl;

        localPackageCacheMetadataStream.SetLength(0);
        await JsonSerializer.SerializeAsync(localPackageCacheMetadataStream, localPackageCacheMetadata, JSON_OPTIONS, ct);
    }

    private static async Task forceRestoreDependentProjects(string dependencyName, string dependencyVersion, string localPackageCacheDir, CancellationToken cancellationToken = default) {
        string contentHash;
        await using (FileStream packageMetadataStream = File.OpenRead(Path.Combine(localPackageCacheDir, ".nupkg.metadata"))) {
            contentHash = (await JsonSerializer.DeserializeAsync<JsonNode>(packageMetadataStream, JSON_OPTIONS, cancellationToken))!["contentHash"]!.GetValue<string>();
        }

        HashSet<string>? lockFilePaths;
        await using (FileStream packageLockFileSetStream = File.OpenRead(packageLockFileSetFilename)) {
            if ((lockFilePaths = await JsonSerializer.DeserializeAsync<HashSet<string>>(packageLockFileSetStream, JSON_OPTIONS, cancellationToken)) is null) return;
        }

        int restoredProjects = 0;
        await Parallel.ForEachAsync(lockFilePaths, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken }, async (lockFilePath, ct) => {
            if (lockFilePath.StartsWith('-')) return; //let users ignore specific lock files

            JsonNode packageLockObject;
            try {
                await using FileStream packageLockStream = File.OpenRead(lockFilePath);
                packageLockObject = (await JsonSerializer.DeserializeAsync<JsonNode>(packageLockStream, JSON_OPTIONS, ct))!;
            } catch (FileNotFoundException) {
                return;
            } catch (DirectoryNotFoundException) {
                return;
            }

            bool restorationNeeded = packageLockObject["dependencies"]?.AsObject().Any(tfmDependencies => {
                JsonNode? dependency = tfmDependencies.Value?[dependencyName];
                return dependencyVersion.Equals(dependency?["resolved"]?.GetValue<string>(), StringComparison.OrdinalIgnoreCase)
                    && contentHash != dependency["contentHash"]?.GetValue<string>();
            }) ?? false;

            if (restorationNeeded) {
                string projectPath = Path.GetDirectoryName(lockFilePath)!;

                // Console.WriteLine($"Restoring {projectPath} because it depends on {dependencyName} {dependencyVersion} and has a different contentHash");
                using Process restoreProcess = Process.Start(new ProcessStartInfo("dotnet", ["restore", projectPath, "--force", "--force-evaluate", "-v:minimal"]) {
                    RedirectStandardOutput = true
                })!;

                while (await restoreProcess.StandardOutput.ReadLineAsync(ct) is {} stdoutLine) {
                    if (!stdoutLine.Equals("  Determining projects to restore...", StringComparison.CurrentCulture)) {
                        Console.WriteLine(stdoutLine.TrimStart()); // hopefully thread-safe
                    }
                }

                await restoreProcess.WaitForExitAsync(ct);
                Interlocked.Increment(ref restoredProjects);
                // Console.WriteLine($"{(restoreProcess.ExitCode == 0 ? "Restored" : "Failed to restore")} {projectPath}");
            }
        });

        if (restoredProjects != 0) {
            Console.WriteLine(
                $"Forcibly restored {restoredProjects} {(restoredProjects != 0 ? "projects" : "project")} that depended upon {dependencyName} {dependencyVersion} to update the contentHash in packages.lock.json to match that of this newly-deployed package.");
        }
    }

    [GeneratedRegex(@"^\s*\d+\.\s+(?<name>.+) \[(?<enablement>Enabled|Disabled)\]$")]
    private static partial Regex nuGetDetailedSource { get; }

    [Command("discover-package-lock-file", Description =
        "Register a packages.lock.json file to be automatically updated in the future when a development dependency is released and its contentHash changes from the Debug build to Release")]
    private class DiscoverPackageLockFile {

        [Argument(0, "packageLockFilePath", "path to packages.lock.json file")]
        [Required]
        public required string packageLockFilePath {
            get;
            init => field = Path.GetFullPath(value);
        }

        public async Task<int> OnExecuteAsync() {
            if (!File.Exists(packageLockFilePath)) return 1;

            using Semaphore packageLockFileSetLock = new(1, 1, "PackageTools.Deploy.packageLockFileSet", new NamedWaitHandleOptions { CurrentUserOnly = true });
            packageLockFileSetLock.WaitOne();
            try {
                await using FileStream packageLockFileSetStream = File.Open(packageLockFileSetFilename, FileMode.OpenOrCreate, FileAccess.ReadWrite);

                HashSet<string> packageLockFilenames = packageLockFileSetStream.Length > 0
                    && await JsonSerializer.DeserializeAsync<HashSet<string>>(packageLockFileSetStream, JSON_OPTIONS) is {} s ? s : [];

                bool dirty = packageLockFilenames.Add(packageLockFilePath);

                if (dirty) {
                    packageLockFileSetStream.SetLength(0);
                    await JsonSerializer.SerializeAsync(packageLockFileSetStream, packageLockFilenames, JSON_OPTIONS);
                }

                return 0;
            } finally {
                packageLockFileSetLock.Release();
            }
        }

    }

}