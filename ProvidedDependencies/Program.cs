using Dependencies;
using McMaster.Extensions.CommandLineUtils;
using NuGet.Frameworks;
using NuGet.Versioning;
using NuGetGalleryClient;
using ProvidedDependencies;
using ProvidedDependencies.Data;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using Unfucked.HTTP.Exceptions;

const string DEFAULT_PROPS_FILENAME = "providedDependencies.g.props";

using CommandLineApplication app = new() {
    UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.Throw,
    Description =
        "Generate a C# .props file that excludes runtime-provided dependencies from single-file framework-dependent builds, so they don't crash when rolling forward to new major runtime versions."
};
app.ExtendedHelpText = $"""

    Examples:
      Generate .props file and import it into the .csproj file in the current directory
        {app.Name}
        
      Specify project directory
        {app.Name} .\MyProject
        
      Specify project file
        {app.Name} MyProject.csproj
        
      Specify .props filename
        {app.Name} --output myProvidedDependencies.props
    """;
app.Conventions.UseDefaultConventions();

CommandArgument csprojOrDir = app.Argument("project", ".csproj file, or directory containing one");
CommandOption   outputFile  = app.Option("-o|--output", $".props file to generate, defaults to {DEFAULT_PROPS_FILENAME}", CommandOptionType.SingleValue);

app.Parse(args);

if (getCsprojFile(csprojOrDir.Value) is not {} csprojFile) {
    return 1;
}

Console.Write("Restoring {0}... ", Path.GetFileName(csprojFile));
if (await Processes.ExecFile("dotnet", "restore", csprojFile) is { exitCode: not 0 } restore) {
    Console.WriteLine("\nRestore failed: {0}", restore.stdout);
    return restore.exitCode;
}
Console.WriteLine("done.");

Encoding                      utf8                      = new UTF8Encoding(false, true);
using UnfuckedHttpClient      http                      = new();
using NuGetGalleryService     nuGetGallery              = new(http);
XmlSerializer                 frameworkListDeserializer = new(typeof(FrameworkFileList));
Task<ICollection<Dependency>> projectDependenciesTask   = DependencyLister.listDependencies(Path.GetDirectoryName(csprojFile)!);

XDocument projectDoc;
string    projectFileContents;
await using (FileStream projectFileStream = File.OpenRead(csprojFile)) {
    using StreamReader projectFileReader = new(projectFileStream, utf8);
    projectFileContents = await projectFileReader.ReadToEndAsync();
    projectDoc          = XDocument.Parse(projectFileContents, LoadOptions.None);
}
bool isAspNetCoreProject = projectDoc.Root!.Attribute("Sdk")?.Value.Equals("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase) ?? false;

string propsFilename       = outputFile.Value() ?? Path.Combine(Path.GetDirectoryName(csprojFile)!, DEFAULT_PROPS_FILENAME);
string expectedPropsImport = Path.GetRelativePath(Path.GetDirectoryName(csprojFile)!, propsFilename);
bool projectImportedProps = projectDoc.Root!.Elements("Import")
    .Any(import => Paths.Dos2UnixSlashes(import.Attribute("Project")?.Value) is {} actual && actual.Equals(Paths.Dos2UnixSlashes(expectedPropsImport), StringComparison.OrdinalIgnoreCase));

IList<NuGetFramework> projectTfms = projectDoc.Descendants("TargetFrameworks")
    .Concat(projectDoc.Descendants("TargetFramework"))
    .Single().Value
    .Split(';', StringSplitOptions.TrimEntries)
    .Select(NuGetFramework.Parse)
    .Where(framework => framework.Framework == ".NETCoreApp").ToList();
ISet<string> projectTfmNames = projectTfms.Select(framework => framework.GetShortFolderName()).ToHashSet();

Task<IDictionary<string, PackageVersion>>  runtimeRefVersions           = fetchReleaseVersionNumbers(false);
Task<IDictionary<string, PackageVersion>>? aspNetCoreRuntimeRefVersions = isAspNetCoreProject ? fetchReleaseVersionNumbers(true) : null;

Console.WriteLine("Downloading reference assemblies for .NET runtimes...");
IDictionary<Version, ISet<string>> providedPackagesByRuntimeVersion = (await Task.WhenAll(projectTfms.DistinctBy(tfm => tfm.Version).Select(async projectTfm => {
    Task<ISet<string>?> runtimePackagesTask = fetchRuntimeProvidedPackages(projectTfm, await runtimeRefVersions);
    Task<ISet<string>?> aspNetCoreRuntimePackagesTask =
        aspNetCoreRuntimeRefVersions != null ? fetchRuntimeProvidedPackages(projectTfm, await aspNetCoreRuntimeRefVersions) : Task.FromResult<ISet<string>?>(null);

    ISet<string> allProvidedPackages = await runtimePackagesTask ?? new HashSet<string>();
    allProvidedPackages.UnionWith(await aspNetCoreRuntimePackagesTask ?? ImmutableHashSet<string>.Empty);

    return new KeyValuePair<Version, ISet<string>>(projectTfm.Version, allProvidedPackages);
}))).ToDictionary();

ICollection<Dependency> resolvedProjectDependencies = await projectDependenciesTask;

IDictionary<NuGetFramework, ISet<string>> intersectingProvidedPackageNamesByTfm = projectTfms.ToDictionary(tfm => tfm, tfm => {
    ISet<string> intersection = new HashSet<string>(providedPackagesByRuntimeVersion[tfm.Version]);
    intersection.IntersectWith(resolvedProjectDependencies
        .Where(dependency => dependency.isTransitive && dependency.frameworks.Contains(tfm.WithNormalizedPlatformVersion().GetShortFolderName()))
        .Select(dependency => dependency.name));
    return intersection;
});

IEnumerable<(string packageName, NuGetVersion latestPatchForProvidedMinorVersion, ISet<string> tfms)> providedPackages = (await Task.WhenAll(
        intersectingProvidedPackageNamesByTfm.Select(async tfmProvidedPackages => {
            VersionRange tfmPatchFloatingVersion = tfmProvidedPackages.Key.Version.Float(NuGetVersionFloatBehavior.Patch);
            return (await Task.WhenAll(
                tfmProvidedPackages.Value.Select(async Task<(string packageName, NuGetVersion latestPatchForProvidedMinorVersion, NuGetFramework tfm)?> (providedPackageName) => {
                    IEnumerable<PackageVersion> providedPackageVersions = await nuGetGallery.fetchAllVersions(providedPackageName);
                    NuGetVersion? latestPatchForProvidedMinorVersion =
                        tfmPatchFloatingVersion.FindBestMatch(providedPackageVersions.Select(version => version.nuGetVersion).Where(version => !version.IsPrerelease));
                    return latestPatchForProvidedMinorVersion != null ? (providedPackageName, latestPatchForProvidedMinorVersion, tfmProvidedPackages.Key) : null;
                }))).Compact();
        })))
    .SelectMany(providedPackage => providedPackage)
    .GroupBy(package => (package.packageName, package.latestPatchForProvidedMinorVersion),
        (nameAndVersion, packages) => (nameAndVersion.packageName, nameAndVersion.latestPatchForProvidedMinorVersion,
                                       tfms: (ISet<string>) packages.Select(dependency => dependency.tfm.GetShortFolderName()).ToHashSet()));

await using FileStream propsFileStream = File.Create(propsFilename);

Project   propsFile = new() { comment = $"Generated by {nameof(ProvidedDependencies)} {Assembly.GetEntryAssembly()!.GetName().Version!.ToString(3)} on {DateTimeOffset.UtcNow:O}" };
ItemGroup itemGroup = new() { label   = "Provided by runtime", condition = "'$(_IsPublishing)' == 'true'" };
propsFile.itemGroup.Add(itemGroup);
itemGroup.packageReferences.AddAll(providedPackages.OrderBy(providedPackage => providedPackage.packageName, StringComparer.OrdinalIgnoreCase).SelectMany(providedPackage => {
    bool conditionalDependency = !providedPackage.tfms.SetEquals(projectTfmNames);
    PackageReference packageReference = new() {
        include       = providedPackage.packageName,
        version       = providedPackage.latestPatchForProvidedMinorVersion.ToNormalizedString(),
        privateAssets = "all"
    };
    return conditionalDependency ? providedPackage.tfms.Select(tfm => packageReference with { condition = $"'(TargetFramework)' == '{tfm}'" }) : [packageReference];
}));

string xmlFileIndentation = projectIndentation.Match(projectFileContents).Value;

XmlSerializer projectSerializer = new(typeof(Project));
string        xmlLineEnding     = projectFileContents[Math.Max(0, projectFileContents.IndexOf('\n') - 1)] == '\r' ? "\r\n" : "\n";
await using XmlWriter xmlWriter = XmlWriter.Create(propsFileStream, new XmlWriterSettings {
    Indent       = true, IndentChars = xmlFileIndentation, Async = true, OmitXmlDeclaration = true, Encoding = utf8,
    NewLineChars = xmlLineEnding
});

XmlSerializerNamespaces namespaces = new([new XmlQualifiedName(string.Empty)]); // remove unnecessary xsi and xsd namespaces
projectSerializer.Serialize(xmlWriter, propsFile, namespaces);
Console.WriteLine("Wrote {0}", Path.GetFullPath(propsFilename));

if (!projectImportedProps) {
    await using FileStream   projectFileStream = new(csprojFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    using StreamReader       projectFileReader = new(projectFileStream, utf8);
    await using StreamWriter projectFileWriter = new(projectFileStream, utf8);

    string csprojContents = await projectFileReader.ReadToEndAsync();

    csprojContents = projectEndTag.Replace(csprojContents, $"{xmlFileIndentation}<Import Project=\"{SecurityElement.Escape(expectedPropsImport)}\" />{xmlLineEnding}{xmlLineEnding}");

    projectFileStream.SetLength(0);
    await projectFileWriter.WriteAsync(csprojContents);
    Console.WriteLine("Imported {0} into {1}", Path.GetFileName(propsFilename), Path.GetFileName(csprojFile));
}

return 0;

async Task<IDictionary<string, PackageVersion>> fetchReleaseVersionNumbers(bool aspNetCoreRuntime) =>
    (await nuGetGallery.fetchAllVersions(aspNetCoreRuntime ? "Microsoft.AspNetCore.App.Ref" : "Microsoft.NETCore.App.Ref"))
    .Where(packageVersion => !packageVersion.nuGetVersion.IsPrerelease)
    .ToDictionary(version => version.version);

async Task<ISet<string>?> fetchRuntimeProvidedPackages(NuGetFramework tfm, IDictionary<string, PackageVersion> availableRuntimeVersions) {
    if (tfm.Version.Float(NuGetVersionFloatBehavior.Patch).FindBestMatch(availableRuntimeVersions.Select(pair => pair.Value.nuGetVersion)) is {} bestMatch) {
        PackageVersion refPackageToDownload = availableRuntimeVersions[bestMatch.OriginalVersion!];
        try {
            await using Stream     zipStream           = await http.Target(refPackageToDownload.content).Get<Stream>();
            await using ZipArchive zipArchive          = new(zipStream, ZipArchiveMode.Read);
            await using Stream     frameworkListStream = await zipArchive.GetEntry("data/FrameworkList.xml")!.OpenAsync();
            FrameworkFileList      frameworkList       = (FrameworkFileList) frameworkListDeserializer.Deserialize(frameworkListStream)!;
            Console.WriteLine("Downloaded {0} {1}", refPackageToDownload.name, refPackageToDownload.version);
            return frameworkList.files.Select(file => file.assemblyName).ToHashSet();
        } catch (HttpException e) {
            Console.WriteLine($"Failed to download .NET runtime reference package {refPackageToDownload.name} {refPackageToDownload.version} from {refPackageToDownload.content}: {e.MessageChain()}");
            throw;
        }
    } else {
        return null;
    }
}

static string? getCsprojFile(string? csprojOrDir) {
    if (csprojOrDir != null && Path.GetExtension(csprojOrDir).Equals(".csproj", StringComparison.OrdinalIgnoreCase)) {
        if (!File.Exists(csprojOrDir)) {
            Console.WriteLine("File {0} does not exist", Path.GetFullPath(csprojOrDir));
            return null;
        }
        return csprojOrDir;
    } else {
        string dirName = csprojOrDir ?? Environment.CurrentDirectory;
        try {
            switch (Directory.EnumerateFiles(dirName, "*.csproj").ToList()) {
                case []:
                    Console.WriteLine("No .csproj files in {0}", Path.GetFullPath(dirName));
                    return null;
                case [var fileName]:
                    return fileName;
                case var fileNames:
                    Console.WriteLine("Found multiple .csproj files in {0} ({1}), specify which one should be used with `{2} \"MyProject.csproj\"`", dirName, fileNames.Join(", "),
                        Path.GetFileName(Environment.ProcessPath));
                    return null;
            }
        } catch (DirectoryNotFoundException) {
            Console.WriteLine("Directory {0} does not exist", Path.GetFullPath(dirName));
            return null;
        }
    }
}

internal static partial class Program {

    [GeneratedRegex(@"(?=</Project\s*>\s*$)", RegexOptions.IgnoreCase)]
    private static partial Regex projectEndTag { get; }

    [GeneratedRegex(@"(?<=\n)[ \t]+?(?=\S)")]
    private static partial Regex projectIndentation { get; }

}