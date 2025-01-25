using System.Text;
using System.Text.Json;
using Unfucked;

namespace DependencyHierarchy;

internal static class Program {

    private const StringComparison STRING_COMPARISON = StringComparison.OrdinalIgnoreCase;

    private static readonly StringComparer STRING_COMPARER = StringComparer.FromComparison(STRING_COMPARISON);

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
        if (Options.parse() is { } opts) {
            OPTIONS = opts;
        } else {
            Environment.Exit(0); // user passed --help and usage was already printed
        }
    }

    public static async Task<int> Main() {
        ICollection<Dependency> dependencies;
        try {
            dependencies = await parseDependencies();
        } catch (FileNotFoundException) {
            return 1;
        } catch (DirectoryNotFoundException) {
            return 1;
        }

        Console.Write(printDependencies(dependencies).ToString());
        return 0;
    }

    /// <exception cref="FileNotFoundException"><c>obj/project.assets.json</c> was not found</exception>
    /// <exception cref="DirectoryNotFoundException"><c>obj/project.assets.json</c> was not found</exception>
    private static async Task<ICollection<Dependency>> parseDependencies() {
        string assetsFilename = Path.Combine(OPTIONS.projectDir, "obj", "project.assets.json");
        Stream assetsFileStream;

        try {
            assetsFileStream = File.OpenRead(assetsFilename);
        } catch (FileNotFoundException) {
            showFileNotFoundError();
            throw;
        } catch (DirectoryNotFoundException) {
            showFileNotFoundError();
            throw;
        }

        IDictionary<string, Dependency> allDependencies = new Dictionary<string, Dependency>(); // key = name

        JsonDocument projectAssetsDoc;
        await using (assetsFileStream) {
            projectAssetsDoc = await JsonDocument.ParseAsync(assetsFileStream);
        }

        using (projectAssetsDoc) {
            ISet<string> intransitiveDependencyNames = projectAssetsDoc.RootElement.GetProperty("project").GetProperty("frameworks").EnumerateObject().SelectMany(framework => {
                try {
                    return framework.Value.GetProperty("dependencies").EnumerateObject().Select(dependency => dependency.Name);
                } catch (KeyNotFoundException) { // project has no dependencies
                    return [];
                }
            }).ToHashSet();

            foreach (JsonProperty targetFramework in projectAssetsDoc.RootElement.GetProperty("targets").EnumerateObject()) {
                IEnumerable<(Dependency, JsonProperty)> dependencies = targetFramework.Value.EnumerateObject().Select(package => {
                    string[]   splitPackageId = package.Name.Split('/', 2);
                    string     name           = splitPackageId[0];
                    string     version        = splitPackageId[1];
                    Dependency dependency     = getOrCreateDependency(name, version);
                    dependency.isIntransitive = intransitiveDependencyNames.Contains(name);
                    return (dependency, package);
                }).ToList();

                foreach ((Dependency intransitiveDependency, JsonProperty package) in dependencies) {
                    if (package.Value.TryGetProperty("dependencies", out JsonElement transitiveDependencies)) {
                        foreach (JsonProperty transitiveDependencyEl in transitiveDependencies.EnumerateObject()) {
                            string     desiredVersion       = transitiveDependencyEl.Value.GetString()!;
                            Dependency transitiveDependency = getOrCreateDependency(transitiveDependencyEl.Name, desiredVersion);
                            intransitiveDependency.dependsOn(transitiveDependency, desiredVersion);
                        }
                    }
                }
            }
        }

        return allDependencies.Values;

        void showFileNotFoundError() => ConsoleControl.WriteLine($"File {assetsFilename} not found. " +
            $"Try restoring the project first using Visual Studio or 'dotnet restore'.", ConsoleColor.DarkRed);

        Dependency getOrCreateDependency(string name, string version) => allDependencies.GetOrAdd(name, new Dependency(name, version), out _);
    }

    private static StringBuilder printDependencies(ICollection<Dependency> allDependencies) {
        StringBuilder tree = new();

        foreach (Dependency intransitiveDependency in allDependencies.Where(dependency => dependency.isIntransitive)
                     .OrderBy(dependency => dependency.name, STRING_COMPARER)) {
            tree.Append(printDependencyRecursively(intransitiveDependency));
        }

        return tree;

        static StringBuilder printDependencyRecursively(Dependency dependency, string? desiredVersion = null, uint depth = 0) {
            StringBuilder branch = new();

            if ((OPTIONS.packageNameFilter == null && dependency.dependencies.Count == 0) || dependency.name.Equals(OPTIONS.packageNameFilter, STRING_COMPARISON)) {
                branch.AppendLine(serializeDependency(depth, dependency, desiredVersion));
            } else {
                foreach (KeyValuePair<Dependency, string> transitiveDependency in
                         dependency.dependencies.OrderBy(dependency => dependency.Key.name, STRING_COMPARER)) {
                    StringBuilder subBranch = printDependencyRecursively(transitiveDependency.Key, transitiveDependency.Value, depth + 1);
                    if (subBranch.Length != 0) {
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
            bool   isFilteredName  = dependency.name.Equals(OPTIONS.packageNameFilter, STRING_COMPARISON);
            bool   isIntransitive  = depth == 0;
            return
                $"{padding}{(isFilteredName ? filteredNameColor : isIntransitive ? nameColor : transitiveNameColor)}{dependency.name}{resetColor} : {(isUnusedVersion ? unusedVersionColor : isIntransitive ? versionColor : transitiveVersionColor)}{desiredVersion ?? dependency.version}{resetColor}{(isUnusedVersion ? $" {warningColor}(omitted for conflict with {dependency.version}){resetColor}" : string.Empty)}";
        }
    }

}