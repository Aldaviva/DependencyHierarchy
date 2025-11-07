using System.Text.Json;
using Unfucked;

namespace Dependencies;

public static class DependencyLister {

    /// <exception cref="FileNotFoundException"><c>obj/project.assets.json</c> was not found</exception>
    /// <exception cref="DirectoryNotFoundException"><c>obj/</c> was not found</exception>
    public static async Task<ICollection<Dependency>> listDependencies(string projectDir, CancellationToken cancellationToken = default) {
        string                          assetsFilename  = Path.Combine(projectDir, "obj", "project.assets.json");
        IDictionary<string, Dependency> allDependencies = new Dictionary<string, Dependency>(); // key = name
        JsonDocument                    projectAssetsDoc;

        try {
            await using Stream assetsFileStream = File.OpenRead(assetsFilename);
            projectAssetsDoc = await JsonDocument.ParseAsync(assetsFileStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        } catch (UnauthorizedAccessException) {
            return [];
        } catch (JsonException) {
            return [];
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
                            intransitiveDependency.dependOn(transitiveDependency, desiredVersion);
                        }
                    }
                }
            }
        }

        return allDependencies.Values;

        Dependency getOrCreateDependency(string name, string version) => allDependencies.GetOrAdd(name, new Dependency(name, version), out _);
    }

}