namespace PackageTools.Restore;

public class RestoreApp: AbstractApp {

    public static Task<int> Main() => CommandLineApplicationFactory.execute<RestoreApp>("restore!", app =>
        app.Description = "Improved version of `dotnet restore` that doesn't cache failures for missing packages, and handles hash changes when you rebuild the package.");

    public override Task<int> OnExecuteAsync(CancellationToken ct) =>
        Process.StartAndWaitForExit("dotnet", ["restore", projectDir, "--force", "--force-evaluate", "--no-http-cache", .. RemainingArguments], ct);

}