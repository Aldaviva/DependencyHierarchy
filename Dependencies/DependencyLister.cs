using System.Text.Json;
using Unfucked;

namespace Dependencies;

public static class DependencyLister {

    /// <exception cref="FileNotFoundException"><c>obj/project.assets.json</c> was not found</exception>
    /// <exception cref="DirectoryNotFoundException"><c>obj/</c> was not found</exception>
    public static async Task<IReadOnlyCollection<Dependency>> listDependencies(string projectDir, CancellationToken cancellationToken = default) {
        string                         assetsFilename  = Path.Combine(projectDir, "obj", "project.assets.json");
        Dictionary<string, Dependency> allDependencies = new(); // key = name
        JsonDocument                   projectAssetsDoc;

        try {
            await using Stream assetsFileStream = File.OpenRead(assetsFilename);
            projectAssetsDoc = await JsonDocument.ParseAsync(assetsFileStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        } catch (UnauthorizedAccessException) {
            return [];
        } catch (JsonException) {
            return [];
        }

        using (projectAssetsDoc) {
            HashSet<string> intransitiveDependencyNames = projectAssetsDoc.RootElement.GetProperty("project").GetProperty("frameworks").EnumerateObject().SelectMany(static framework => {
                try {
                    return framework.Value.GetProperty("dependencies").EnumerateObject().Select(static dependency => dependency.Name);
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
                    dependency.isTransitive = !intransitiveDependencyNames.Contains(name);
                    dependency.isProject    = package.Value.GetProperty("type").GetString() == "project";
                    dependency.frameworks.Add(targetFramework.Name);
                    return (dependency, package);
                }).ToList();

                foreach ((Dependency dependency, JsonProperty package) in dependencies) {
                    if (package.Value.TryGetProperty("dependencies", out JsonElement transitiveDependencies)) {
                        foreach (JsonProperty transitiveDependencyEl in transitiveDependencies.EnumerateObject()) {
                            string     desiredVersion       = transitiveDependencyEl.Value.GetString()!;
                            Dependency transitiveDependency = getOrCreateDependency(transitiveDependencyEl.Name, desiredVersion);
                            dependency.dependOn(transitiveDependency, desiredVersion);
                        }
                    }
                }
            }
        }

        return allDependencies.Values;

        Dependency getOrCreateDependency(string name, string version) => allDependencies.GetOrAdd(name, new Dependency(name, version), out _);
    }

}