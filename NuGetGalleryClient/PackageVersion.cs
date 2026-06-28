using System.Text.Json.Serialization;

namespace NuGetGalleryClient;

public sealed record PackageVersion {

    [JsonPropertyName("id")]
    public required string name { get; init; }

    public required string version { get; init; }

    [JsonPropertyName("packageContent")]
    public Uri? content { get; init; }

    public long totalDownloads { get; init; }

}