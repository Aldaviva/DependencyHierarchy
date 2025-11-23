using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NuGetGalleryClient;

public class NuGetGalleryService: IDisposable {

    private static readonly Uri                   SERVICE_INDEX_URL = new("https://api.nuget.org/v3/index.json");
    private static readonly JsonSerializerOptions JSON_OPTIONS      = new(JsonSerializerDefaults.Web);

    private static readonly RetryOptions RETRY_OPTIONS = new() {
        MaxAttempts    = 3,
        IsRetryAllowed = (exception, _) => exception is ProcessingException or ServerErrorException,
        AfterFailure   = (exception, attemptNum) => Console.WriteLine("API request to NuGet Gallery failed (attempt #{0:N0}): {1}", attemptNum + 1, exception.Message)
    };

    private readonly HttpClient      http;
    private readonly bool            disposeHttpClient;
    private readonly Lazy<Task<Uri>> metadataBaseUrl;
    private readonly Lazy<Task<Uri>> flatContainerUrl;

    private readonly Cache<string, IEnumerable<PackageVersion>> packageVersionsCache;

    public NuGetGalleryService(HttpClient? httpClient = null) {
        http = (httpClient ?? new UnfuckedHttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All }))
            .Property(PropertyKey.JsonSerializerOptions, JSON_OPTIONS);
        disposeHttpClient = httpClient == null;

        var serviceIndex = new Lazy<Task<JsonNode>>(() => http.Target(SERVICE_INDEX_URL).Get<JsonNode>(), LazyThreadSafetyMode.ExecutionAndPublication);

        metadataBaseUrl = new Lazy<Task<Uri>>(async () =>
            new Uri((await serviceIndex.Value)["resources"]!.AsArray().First(obj => obj!["@type"]!.GetValue<string>() == "RegistrationsBaseUrl/3.6.0")!["@id"]!.GetValue<string>()));
        flatContainerUrl = new Lazy<Task<Uri>>(async () =>
            new Uri((await serviceIndex.Value)["resources"]!.AsArray().First(obj => obj!["@type"]!.GetValue<string>() == "PackageBaseAddress/3.0.0")!["@id"]!.GetValue<string>()));

        packageVersionsCache = new InMemoryCache<string, IEnumerable<PackageVersion>>(loader: async packageName => await fetchAllVersionsUncached(packageName));
    }

    public async Task<IEnumerable<PackageVersion>> fetchAllVersions(string packageName) => await packageVersionsCache.Get(packageName);

    private async Task<IEnumerable<PackageVersion>> fetchAllVersionsUncached(string packageName, CancellationToken cancellationToken = default) {
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
                    });

                /*JsonNode packageVersionIndex = await http.Target(await metadataBaseUrl.Value)
                    .Path(packageName.ToLowerInvariant(), false)
                    .Path("index.json")
                    .Get<JsonNode>(cancellationToken);

                return (await Task.WhenAll(packageVersionIndex["items"]!.AsArray().Select(async item => {
                    JsonArray packageVersions;
                    if (item!["items"] is {} versionItem) {
                        packageVersions = versionItem.AsArray();
                    } else {
                        JsonNode page = await http.Target(item["@id"]!.GetValue<string>()).Get<JsonNode>(cancellationToken);
                        packageVersions = page["items"]!.AsArray();
                    }

                    return packageVersions.Select(packageVersion => packageVersion!["catalogEntry"]!.Deserialize<PackageVersion>(JSON_OPTIONS)!);
                }))).SelectMany(nodes => nodes);*/

                // .SelectMany(catalogPage => catalogPage!["items"]!
                // .AsArray().Select(package => package!["catalogEntry"]!["version"]!.GetValue<string>()));

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

    public void Dispose() {
        if (disposeHttpClient) {
            http.Dispose();
        }
        GC.SuppressFinalize(this);
    }

}