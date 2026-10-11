using DotNet.Globbing;
using McMaster.Extensions.CommandLineUtils;
using System.Reflection;
using System.Text.RegularExpressions;

namespace DependencyHierarchy;

public class Options {

    public Options() {
        packageNameFilterLazy = new Lazy<Glob?>(() => packageNameFilterPattern is {} pattern && Regex.IsMatch(pattern, @"[^\s*?]")
            ? Glob.Parse(pattern, new GlobOptions { Evaluation = new EvaluationOptions { CaseInsensitive = true } })
            : null);
    }

    [Argument(0, "PROJECTDIR", "Directory of a C# project, which should contain an obj subdirectory. Typically contains a .csproj file. Defaults to current working directory.")]
    public string projectDir { get; set; } = string.Empty;

    [Option("-f|--filter <PACKAGE>",
        "Only show the given package and its dependents, hiding unrelated packages. Pass a name like System.Text.Json or glob like '*json*', or omit to show all transitive dependencies. Case insensitive.",
        CommandOptionType.SingleValue)]
    private string? packageNameFilterPattern { get; set; }

    private readonly Lazy<Glob?> packageNameFilterLazy;
    public Glob? packageNameFilter => packageNameFilterLazy.Value;

    [Option("--no-color", "Disable output text colors.", CommandOptionType.NoValue)]
    public bool noColor { get; set; } = false;

    [Option("--no-restore", "Skip restoring project before analyzing dependencies.", CommandOptionType.NoValue)]
    public bool noRestoration { get; set; } = false;

    public static Options? parse(string[]? args = null) {
        using CommandLineApplication<Options> optionsParser = new() {
            FullName                     = "DependencyHierarchy",
            UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.Throw,
            Description                  = "Show hierarchy of transitive NuGet package dependencies in a C# project, like Eclipse m2e or 'npm ls' do."
        };
        optionsParser.Conventions.UseDefaultConventions();
        optionsParser.VersionOptionFromAssemblyAttributes("-v|--version", Assembly.GetEntryAssembly() ?? typeof(Options).Assembly);
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

        parsed.projectDir = parsed.projectDir.HasText ? Path.GetFullPath(parsed.projectDir.TrimEnd('"')) : Environment.CurrentDirectory;
        if (Path.GetExtension(parsed.projectDir).Equals(".csproj", StringComparison.OrdinalIgnoreCase)) {
            parsed.projectDir = Path.GetDirectoryName(parsed.projectDir)!;
        }

        if (optionsParser.IsShowingInformation) {
            return null;
        } else if (optionsParser.GetValidationResult()?.ErrorMessage is {} validationError) {
            Console.WriteLine(validationError);
            return null;
        } else {
            return parsed;
        }
    }

}