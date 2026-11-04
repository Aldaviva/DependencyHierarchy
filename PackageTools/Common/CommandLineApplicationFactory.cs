using System.Reflection;

namespace PackageTools.Common;

public static class CommandLineApplicationFactory {

    public static async Task<int> execute<APP>(string title, Action<CommandLineApplication<APP>>? configure = null) where APP: AbstractApp {
        using CommandLineApplication<APP> app = new() {
            Name                         = $"dotnet {title.ToLowerInvariant()}",
            FullName                     = $"dotnet-{title.ToLowerInvariant()}",
            UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.StopParsingAndCollect
        };
        app.Conventions.UseDefaultConventions();
        app.VersionOptionFromAssemblyAttributes("--version", Assembly.GetEntryAssembly()!); // -v conflicts with dotnet's verbosity option

        configure?.Invoke(app);

        return await app.ExecuteAsync(Environment.GetCommandLineArgs().Skip(1).ToArray(), CancellationToken.None);
    }

}