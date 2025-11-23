namespace Dependencies;

public class Dependency(string name, string version): IEquatable<Dependency> {

    public string name { get; } = name;
    public string version { get; } = version;
    public bool isProject { get; set; } = false;

    /// <summary>
    /// All of the project's TFMs in which it depends on this package
    /// </summary>
    public ISet<string> frameworks { get; } = new HashSet<string>();

    private bool transitive;
    public bool isTransitive {
        get => transitive || _dependencies.Count != 0;
        set => transitive = value;
    }

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

    public void dependOn(Dependency dependency, string desiredVersion) {
        if (_dependencies.TryAdd(dependency, desiredVersion)) {
            dependency._dependents.Add(this, desiredVersion);
        }
    }

    public static bool operator ==(Dependency? left, Dependency? right) => Equals(left, right);

    public static bool operator !=(Dependency? left, Dependency? right) => !Equals(left, right);

    /// <inheritdoc />
    public bool Equals(Dependency? other) => other is not null && (ReferenceEquals(this, other) || string.Equals(name, other.name, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is not null && (ReferenceEquals(this, obj) || (obj.GetType() == GetType() && Equals((Dependency) obj)));

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(name);

    /// <inheritdoc />
    public override string ToString() => $"{name} : {version}";

}