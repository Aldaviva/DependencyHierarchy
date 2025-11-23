using McMaster.Extensions.CommandLineUtils;
using NuGetGalleryClient;
using Unfucked;
using WorkspaceResolution;
using WorkspaceResolution.Services;

using CommandLineApplication app = new() {
    UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.Throw,
    Description                  = "Restore all C# projects currently open in Visual Studio which depend upon a given NuGet package"
};
app.Conventions.UseDefaultConventions();
app.ExtendedHelpText = $"""

                        Examples:
                          Restore all open C# projects that depend on the Unfucked package:
                            {app.Name} restore-dependents Unfucked 1.0.0 "C:\Users\Ben\Documents\Projects\Unfucked\Unfucked\bin\Release\Unfucked.1.0.0.nupkg"
                          
                          Install build hooks and NuGet configuration required to make this program work:
                            {app.Name} install
                        """;

CommandOption localPackageDir = app.Option("--local-package-dir",
    $"Directory for local packages, which should also be a NuGet package source in nuget.config; defaults to {Constants.DEFAULT_LOCAL_PACKAGE_DIR}", CommandOptionType.SingleValue, true);

app.Command("restore-dependents", restoreCommand => {
    CommandArgument packageName     = restoreCommand.Argument("packageId", "Name (ID) of the NuGet package").IsRequired();
    CommandArgument packageVersion  = restoreCommand.Argument("packageVersion", "Version of the NuGet package").IsRequired();
    CommandArgument packageFilename = restoreCommand.Argument("packageFile", "Filename of .nupkg package").IsRequired();
    CommandOption skipSolutionFile = restoreCommand.Option("--skip-solution", "When restoring dependent projects, skip projects in the solution defined by this .sln filename",
        CommandOptionType.SingleValue);
    CommandOption buildAfterRestore = restoreCommand.Option("--build-dependent-packages",
        "After restoring a dependent package project, build it, which is useful if its transitive dependents need to be restored too", CommandOptionType.NoValue);

    restoreCommand.OnExecuteAsync(async ct => {
        string localPackageDirectory = localPackageDir.Value() is {} dir ? Path.GetFullPath(dir) : Constants.DEFAULT_LOCAL_PACKAGE_DIR;
        Directory.CreateDirectory(localPackageDirectory);

        using NuGetGalleryPackageVersionsCache galleryPackageVersions = new(new NuGetGalleryService());
        if (await galleryPackageVersions.doesPackageVersionExist(packageName.Value!, packageVersion.Value!, localPackageDirectory, ct)) {
            Console.WriteLine("Package {0} {1} already exists in NuGet Gallery, so not installing locally-built package into user package cache.", packageName.Value, packageVersion.Value);
            return;
        }

        File.Copy(packageFilename.Value!, Path.Combine(localPackageDirectory, Path.GetFileName(packageFilename.Value!)), true);
        try {
            File.Copy(Path.ChangeExtension(packageFilename.Value!, ".snupkg"), Path.Combine(localPackageDirectory, Path.GetFileNameWithoutExtension(packageFilename.Value!) + ".snupkg"), true);
        } catch (FileNotFoundException) {} // no symbol package created, so nothing to copy
        string globalNuGetCacheDir = Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\.nuget\packages");
        Directories.TryDelete(Path.Combine(globalNuGetCacheDir, packageName.Value!), true);

        await DotnetCliService.restoreOpenVisualStudioProjectsThatDependOn(packageName.Value!, skipSolutionFile.Value(), buildAfterRestore.HasValue(), localPackageDirectory, ct);
    });
});

app.Command("install", installCommand => installCommand.OnExecuteAsync(async ct => await InstallerService.install(localPackageDir.Value(), ct)));

return await app.ExecuteAsync(args);