using NuGet.Frameworks;
using NuGet.Versioning;
using NuGetGalleryClient;

namespace ProvidedDependencies;

public static class Extensions {

    private static readonly Version versionZero = new(0, 0, 0, 0);

    extension(PackageVersion version) {

        public NuGetVersion nuGetVersion => NuGetVersion.Parse(version.version);

    }

    extension(Version version) {

        public VersionRange Float(NuGetVersionFloatBehavior amount) {
            NuGetVersion baseVersion = new(version);
            return new VersionRange(baseVersion, new FloatRange(amount, baseVersion));
        }

    }

    extension(NuGetFramework framework) {

        public NuGetFramework WithNormalizedPlatformVersion() {
            if (framework.HasPlatform && framework.PlatformVersion == versionZero) {
                Version implicitPlatformVersion = framework.Platform switch {
                    "windows" => new Version(7, 0, 0, 0),
                    "android" => framework.Version switch {
                        { Major: 8 } => new Version(34, 0, 0, 0),
                        { Major: 9 } => new Version(35, 0, 0, 0),
                        _            => new Version(36, 0, 0, 0)
                    },
                    "ios" => framework.Version switch {
                        { Major: 8 } => new Version(17, 2, 0, 0),
                        { Major: 9 } => new Version(18, 0, 0, 0),
                        _            => new Version(18, 7, 0, 0)
                    },
                    _ => versionZero
                };
                return new NuGetFramework(framework.Framework, framework.Version, framework.Platform, implicitPlatformVersion);
            } else {
                return framework;
            }
        }

    }

}