using McMaster.Extensions.CommandLineUtils;
using WorkspaceResolution;
using WorkspaceResolution.Services;

using CommandLineApplication app = new() {
    UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.Throw,
    Description                  = "Restore all open Visual Studio projects that depend upon a given package"
};
app.Conventions.UseDefaultConventions();
app.ExtendedHelpText = $"""

                        Examples:
                          Restore all open projects that depend on the Unfucked package:
                            {app.Name} restore-dependents Unfucked
                          
                          Use a custom local package directory that isn't {Constants.DEFAULT_LOCAL_PACKAGE_DIR}:
                            {app.Name} restore-dependents Unfucked --local-package-dir C:\Users\Ben\.nuget\repository\
                            
                          Install build hooks and NuGet configuration required to make this program work:
                            {app.Name} install
                        """;

CommandOption<string?> localPackageDir = app.Option<string?>("--local-package-dir", "Directory for local packages, which should also be a NuGet package source in nuget.config",
    CommandOptionType.SingleValue, true);

app.Command("restore-dependents", restoreCommand => {
    CommandArgument<string> packageName = restoreCommand.Argument<string>("packageId", "Name of the NuGet package dependency").IsRequired();
    restoreCommand.OnExecuteAsync(async ct => await DotnetCliService.restoreOpenVisualStudioProjectsThatDependOn(packageName.ParsedValue, localPackageDir.ParsedValue, ct));
});

app.Command("install", installCommand => installCommand.OnExecuteAsync(async ct => await InstallerService.install(localPackageDir.Value(), ct)));

return await app.ExecuteAsync(args);