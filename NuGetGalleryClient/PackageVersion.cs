using System.Text.Json.Serialization;

namespace NuGetGalleryClient;

public record PackageVersion {

    [JsonPropertyName("id")]
    public required string name { get; init; }

    public required string version { get; init; }

    [JsonPropertyName("packageContent")]
    public required Uri content { get; init; }

}