namespace DependencyHierarchy;

public class Dependency(string name, string version): IEquatable<Dependency> {

    public string name { get; } = name;
    public string version { get; } = version;

    private readonly Dictionary<Dependency, string> _dependencies = [];
    private readonly Dictionary<Dependency, string> _dependents   = [];

    /// <summary>
    /// Key is a package dependency, value is the desired version (may be overwritten by version conflict resolution)
    /// </summary>
    public IReadOnlyDictionary<Dependency, string> dependencies => _dependencies;

    /// <summary>
    /// Key is a package dependency, value is the desired version (may be overwritten by version conflict resolution)
    /// </summary>
    public IReadOnlyDictionary<Dependency, string> dependents => _dependents;

    public Dependency dependsOn(Dependency dependency, string desiredVersion) {
        if (_dependencies.TryAdd(dependency, desiredVersion)) {
            dependency._dependents.Add(this, desiredVersion);
        }
        return this;
    }

    /// <inheritdoc />
    public bool Equals(Dependency? other) => other is not null && (ReferenceEquals(this, other) || name == other.name /*&& version == other.version*/);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is not null && (ReferenceEquals(this, obj) || (obj.GetType() == GetType() && Equals((Dependency) obj)));

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(name /*, version*/);

}