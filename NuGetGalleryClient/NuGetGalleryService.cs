using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NuGetGalleryClient;

public sealed class NuGetGalleryService: IDisposable {

    private static readonly Uri                   SERVICE_INDEX_URL = new("https://api.nuget.org/v3/index.json");
    private static readonly JsonSerializerOptions JSON_OPTIONS      = new(JsonSerializerDefaults.Web);

    private static readonly RetryOptions RETRY_OPTIONS = new() {
        MaxAttempts    = 3,
        IsRetryAllowed = static (exception, _) => exception is ProcessingException or ServerErrorException,
        AfterFailure   = static (exception, attemptNum) => Console.WriteLine("API request to NuGet Gallery failed (attempt #{0:N0}): {1}", attemptNum + 1, exception.Message)
    };

    private readonly HttpClient      http;
    private readonly bool            disposeHttpClient;
    private readonly Lazy<Task<Uri>> metadataBaseUrl;
    private readonly Lazy<Task<Uri>> flatContainerUrl;
    private readonly Lazy<Task<Uri>> searchUrl;

    private readonly InMemoryCache<string, IReadOnlyList<PackageVersion>> packageVersionsCache;

    public NuGetGalleryService(HttpClient? httpClient = null) {
        http = (httpClient ?? new UnfuckedHttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All }))
            .Property(PropertyKey.JsonSerializerOptions, JSON_OPTIONS);
        disposeHttpClient = httpClient == null;

        var serviceIndex = new Lazy<Task<JsonNode>>(() => http.Target(SERVICE_INDEX_URL).Get<JsonNode>(), LazyThreadSafetyMode.ExecutionAndPublication);

        metadataBaseUrl = new Lazy<Task<Uri>>(async () =>
            new Uri((await serviceIndex.Value)["resources"]!.AsArray().First(obj => obj!["@type"]!.GetValue<string>() == "RegistrationsBaseUrl/3.6.0")!["@id"]!.GetValue<string>()));
        flatContainerUrl = new Lazy<Task<Uri>>(async () =>
            new Uri((await serviceIndex.Value)["resources"]!.AsArray().First(obj => obj!["@type"]!.GetValue<string>() == "PackageBaseAddress/3.0.0")!["@id"]!.GetValue<string>()));
        searchUrl = new Lazy<Task<Uri>>(async () =>
            new Uri((await serviceIndex.Value)["resources"]!.AsArray().First(obj => obj!["@type"]!.GetValue<string>() == "SearchQueryService/3.5.0")!["@id"]!.GetValue<string>()));

        packageVersionsCache = new InMemoryCache<string, IReadOnlyList<PackageVersion>>(loader: async (packageName, ct) => await fetchAllVersionsUncached(packageName, ct));
    }

    public async Task<IReadOnlyList<PackageVersion>> fetchAllVersions(string packageName) =>
        await packageVersionsCache.Get(packageName);

    private async Task<IReadOnlyList<PackageVersion>> fetchAllVersionsUncached(string packageName, CancellationToken cancellationToken = default) {
        try {
            return await Retrier.Attempt(async _ => {
                UrlBuilder flatContainerPackage = new UrlBuilder(await flatContainerUrl.Value)
                    .Path("{name}")
                    .ResolveTemplate("name", packageName.ToLowerInvariant());

                return (await http.Target(flatContainerPackage)
                        .Path("index.json")
                        .Get<JsonNode>(cancellationToken))
                    ["versions"]!.AsArray().Select(versionText => {
                        string version = versionText!.GetValue<string>();
                        return new PackageVersion {
                            name    = packageName,
                            version = version,
                            content = flatContainerPackage
                                .Path("{version}/{name}.{version}.nupkg")
                                .ResolveTemplate("version", version.ToLowerInvariant())
                        };
                    }).ToList();
            }, RETRY_OPTIONS with { CancellationToken = cancellationToken });
        } catch (NotFoundException) {
            return [];
        } catch (WebApplicationException e) {
            Console.WriteLine("Error {0} while fetching version numbers of package {1} from NuGet Gallery API", e.StatusCode, packageName);
            return [];
        } catch (ProcessingException e) {
            Console.WriteLine(e.Message);
            return [];
        }
    }

    public async Task<(IReadOnlyList<PackageVersion> packages, int totalPackages)> searchForPackages(string? query, int offset = 0, int pageSize = 20, bool includePrerelease = false,
                                                                                                     string? packageType = null, CancellationToken cancellationToken = default) {
        JsonNode response = await http.Target(await searchUrl.Value)
            .QueryParam(new Dictionary<string, object?> {
                ["q"]           = query,
                ["skip"]        = offset,
                ["take"]        = pageSize,
                ["prerelease"]  = includePrerelease,
                ["packageType"] = packageType
            })
            .Get<JsonNode>(cancellationToken);

        return (packages: response["data"]?.Deserialize<IReadOnlyList<PackageVersion>>() ?? [], totalPackages: response["totalHits"]!.GetValue<int>());
    }

    /*private static void test(object queryParams) {
        queryParams.GetType().GetProperties().
    }*/

    public void Dispose() {
        if (disposeHttpClient) {
            http.Dispose();
        }
    }

}

/*public enum PackageType {

    DEPENDENCY,
    DOTNET_TOOL,
    TEMPLATE,
    MCP_SERVER

}*/