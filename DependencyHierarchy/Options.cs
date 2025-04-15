using McMaster.Extensions.CommandLineUtils;
using Unfucked;

namespace DependencyHierarchy;

public class Options {

    [Argument(0, "PROJECTDIR", "Directory of a C# project, which should contain an obj subdirectory. Typically contains a .csproj file. Defaults to current working directory.")]
    public string projectDir { get; set; } = string.Empty;

    [Option("-f|--filter <PACKAGE>",
        "Only show the given package and its dependents, hiding unrelated packages. Pass a name like System.Text.Json, or omit to show all transitive dependencies.",
        CommandOptionType.SingleValue)]
    public string? packageNameFilter { get; set; }

    [Option("--no-color", "Disable output text colors.", CommandOptionType.NoValue)]
    public bool noColor { get; set; } = false;

    public static Options? parse(string[]? args = null) {
        var optionsParser = new CommandLineApplication<Options> {
            UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.Throw,
            Description                  = "Show hierarchy of transitive NuGet package dependencies in a C# project, like Eclipse m2e or 'npm ls' do."
        };
        optionsParser.Conventions.UseDefaultConventions();
        optionsParser.ExtendedHelpText =
            $"""

             Examples:
               Show dependency hierarchy for the C# project in the current directory:
                 {optionsParser.Name}
                 
               Show dependency hierarchy for the C# project in a specified directory:
                 {optionsParser.Name} "C:\Path\To\Solution\Project\"
             
               Show only one specified leaf package and its dependents:
                 {optionsParser.Name} --filter System.Text.Json
             """;
        optionsParser.Parse(args ?? Environment.GetCommandLineArgs().Skip(1).ToArray());
        Options parsed = optionsParser.Model;

        parsed.projectDir = parsed.projectDir.HasText() ? Path.GetFullPath(parsed.projectDir.TrimEnd('"')) : Environment.CurrentDirectory;
        if (Path.GetExtension(parsed.projectDir).Equals(".csproj", StringComparison.OrdinalIgnoreCase)) {
            parsed.projectDir = Path.GetDirectoryName(parsed.projectDir)!;
        }

        return optionsParser.OptionHelp?.HasValue() ?? false ? null : parsed;
    }

}