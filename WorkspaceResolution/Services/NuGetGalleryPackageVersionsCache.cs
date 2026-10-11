using NuGetGalleryClient;
using System.Text.Json;

namespace WorkspaceResolution.Services;

public sealed class NuGetGalleryPackageVersionsCache(NuGetGalleryService nuGetGallery) {

    public async Task<bool> doesPackageVersionExist(string packageName, string packageVersion, string localPackageDirectory, CancellationToken cancellationToken = default) {
        using Semaphore semaphore = new(1, 1, "WorkspaceResolution.galleryPackageExistanceCache");
        semaphore.WaitOne();
        try {
            await using FileStream galleryPackageExistenceCacheStream = new(Path.Combine(localPackageDirectory, "nugetGalleryExistingPackageVersions.json"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            Dictionary<string, HashSet<string>> galleryPackageExistenceCache;
            try {
                galleryPackageExistenceCache = await JsonSerializer.DeserializeAsync<Dictionary<string, HashSet<string>>>(galleryPackageExistenceCacheStream, cancellationToken: cancellationToken) ??
                    throw new JsonException("null");
            } catch (JsonException) {
                galleryPackageExistenceCache = new Dictionary<string, HashSet<string>>();
            }

            HashSet<string> knownGalleryPackageVersions = galleryPackageExistenceCache.GetOrAdd(packageName.ToLowerInvariant(), [], out bool _);
            if (doesPackageVersionExistInGallery()) {
                return true;
            } else {
                // Console.WriteLine("Fetching list of version numbers for package {0} in NuGet Gallery to check for version {1}.", packageName, packageVersion);
                IEnumerable<PackageVersion> allGalleryPackageVersions = await nuGetGallery.fetchAllVersions(packageName);
                knownGalleryPackageVersions.AddAll(allGalleryPackageVersions.Select(static version => version.version.ToLowerInvariant()));

                galleryPackageExistenceCacheStream.Position = 0;
                galleryPackageExistenceCacheStream.SetLength(0);
                await JsonSerializer.SerializeAsync(galleryPackageExistenceCacheStream, galleryPackageExistenceCache, cancellationToken: cancellationToken);

                if (doesPackageVersionExistInGallery()) {
                    return true;
                }
            }
            return false;

            bool doesPackageVersionExistInGallery() => knownGalleryPackageVersions.Contains(packageVersion.ToLowerInvariant());
        } finally {
            semaphore.Release();
        }
    }

}