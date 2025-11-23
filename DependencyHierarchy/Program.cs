using Dependencies;
using System.Text;
using Unfucked;

namespace DependencyHierarchy;

internal static class Program {

    private static readonly StringComparer STRING_COMPARER = StringComparer.OrdinalIgnoreCase;

    private static string resetColor => OPTIONS.noColor ? string.Empty : ConsoleControl.ResetColor;
    private static string nameColor => OPTIONS.noColor ? string.Empty : ConsoleControl.Color(ConsoleColor.Blue, ConsoleColor.Black);
    private static string transitiveNameColor => OPTIONS.noColor ? string.Empty : ConsoleControl.Color(ConsoleColor.DarkBlue, ConsoleColor.Black);
    private static string filteredNameColor => OPTIONS.noColor ? string.Empty : ConsoleControl.Color(ConsoleColor.Magenta, ConsoleColor.Black);
    private static string versionColor => OPTIONS.noColor ? string.Empty : ConsoleControl.Color(ConsoleColor.Green, ConsoleColor.Black);
    private static string transitiveVersionColor => OPTIONS.noColor ? string.Empty : ConsoleControl.Color(ConsoleColor.DarkGreen, ConsoleColor.Black);
    private static string unusedVersionColor => OPTIONS.noColor ? string.Empty : ConsoleControl.Color(ConsoleColor.DarkGray, ConsoleColor.Black);
    private static string warningColor => OPTIONS.noColor ? string.Empty : ConsoleControl.Color(ConsoleColor.Yellow, ConsoleColor.Black);

    private static readonly Options OPTIONS;

    static Program() {
        if (Options.parse() is {} opts) {
            OPTIONS = opts;
        } else {
            Environment.Exit(0); // user passed --help and usage was already printed
        }
    }

    public static async Task<int> Main() {
        ICollection<Dependency> dependencies;
        try {
            dependencies = await DependencyLister.listDependencies(OPTIONS.projectDir);
        } catch (FileNotFoundException e) {
            showFileNotFoundError(e.FileName!);
            return 1;
        } catch (DirectoryNotFoundException) {
            showFileNotFoundError(Path.Combine(OPTIONS.projectDir, "obj"));
            return 1;
        }

        Console.Write(printDependencies(dependencies).ToString());
        return 0;

        static void showFileNotFoundError(string path) => ConsoleControl.WriteLine($"Path {path} not found. " +
            "Try restoring the project first using Visual Studio or 'dotnet restore'.", ConsoleColor.DarkRed);
    }

    private static StringBuilder printDependencies(ICollection<Dependency> allDependencies) {
        StringBuilder tree = new();

        foreach (Dependency intransitiveDependency in allDependencies.Where(dependency => !dependency.isTransitive)
                     .OrderBy(dependency => dependency.name, STRING_COMPARER)) {
            tree.Append(printDependencyRecursively(intransitiveDependency));
        }

        return tree;

        static StringBuilder printDependencyRecursively(Dependency dependency, string? desiredVersion = null, uint depth = 0) {
            StringBuilder branch = new();

            bool leaf                 = dependency.dependencies.Count == 0;
            bool isMatchOrNoFiltering = OPTIONS.packageNameFilter?.IsMatch(dependency.name) ?? true;
            if (leaf) {
                if (isMatchOrNoFiltering) {
                    branch.AppendLine(serializeDependency(depth, dependency, desiredVersion));
                }
            } else {
                foreach (KeyValuePair<Dependency, string> transitiveDependency in
                         dependency.dependencies.OrderBy(dependency => dependency.Key.name, STRING_COMPARER)) {
                    StringBuilder subBranch = printDependencyRecursively(transitiveDependency.Key, transitiveDependency.Value, depth + 1);
                    if (subBranch.Length != 0 || isMatchOrNoFiltering) {
                        if (branch.Length == 0) {
                            branch.AppendLine(serializeDependency(depth, dependency, desiredVersion));
                        }
                        branch.Append(subBranch);
                    }
                }
            }

            return branch;
        }

        static string serializeDependency(uint depth, Dependency dependency, string? desiredVersion) {
            bool   isUnusedVersion = desiredVersion != null && desiredVersion != dependency.version;
            string padding         = "  ".Repeat(depth);
            bool   isFilteredName  = OPTIONS.packageNameFilter?.IsMatch(dependency.name) ?? false;
            bool   isIntransitive  = depth == 0;
            return
                $"{padding}{(isFilteredName ? filteredNameColor : isIntransitive ? nameColor : transitiveNameColor)}{dependency.name}{resetColor} : {(isUnusedVersion ? unusedVersionColor : isIntransitive ? versionColor : transitiveVersionColor)}{desiredVersion ?? dependency.version}{resetColor}{(isUnusedVersion ? $" {warningColor}(omitted for conflict with {dependency.version}){resetColor}" : string.Empty)}";
        }
    }

}