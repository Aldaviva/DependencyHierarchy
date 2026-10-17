using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Windows.Forms;

namespace LetsNuGet.Cli;

public record Options {

    [Argument(0, "apiKeyName", "Name of the existing API key to regenerate")]
    [Required]
    public required string apiKeyName { get; init; }

    public static Options? parse(string[] args) {
        Version.PrintProgramVersionAndExitIfRequested();

        using CommandLineApplication<Options> cli = new() {
            FullName    = "Let's NuGet",
            Description = "Regenerate NuGet Gallery API key automatically."
        };
        cli.VersionOption("-v|--version", (string?) null);
        cli.Conventions.UseDefaultConventions();
        cli.ExtendedHelpText = $"""

            Example:
              {cli.Name} "My API key name"
            """;

        bool hasError = true;
        cli.OnExecute(() => hasError = false);

        using TextWriter errorWriter = cli.Error = new MessageBoxWriter($"{cli.FullName} error", MessageBoxIcon.Error);
        using TextWriter textWriter  = cli.Out = new MessageBoxWriter(cli.FullName!, MessageBoxIcon.Information);

        try {
            cli.Execute(args);
        } catch (UnrecognizedCommandParsingException) {
            hasError = true; // already showed message box
        }

        return hasError ? null : cli.Model;

    }

}