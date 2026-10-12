// ReSharper disable InconsistentNaming - names can't be changed in McMaster.Extensions.CommandLineUtils

namespace PackageTools.Common;

public abstract class AbstractApp {

    [Argument(0, "project", "Directory or .csproj filename of project.")]
    public string projectDir {
        get;
        init => field = value.EmptyToNull ?? ".";
    } = ".";

    public IReadOnlyList<string> RemainingArguments { get; } = [];

    public abstract Task<int> OnExecuteAsync(CancellationToken ct);

}