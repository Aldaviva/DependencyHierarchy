namespace PackageTools.Publish;

public class PublishApp: AbstractApp {

    public static Task<int> Main() => CommandLineApplicationFactory.execute<PublishApp>("publish!", app =>
        app.Description =
            "Improved version of `dotnet publish` that defaults to single-file app bundling without setting that in the project file, which breaks in CI when package lock files are used and the SDK patch version differs across installations. Also always uses Release configuration, even with old TFMs.");

    public override Task<int> OnExecuteAsync(CancellationToken ct) =>
        Process.StartAndWaitForExit("dotnet", ["publish", "--configuration", "Release", "-p:PublishSingleFile=false", .. RemainingArguments], ct);

}