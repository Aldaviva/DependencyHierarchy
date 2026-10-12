using Dependencies;
using McMaster.Extensions.CommandLineUtils;
using NuGet.Frameworks;
using NuGet.Versioning;
using NuGetGalleryClient;
using ProvidedDependencies;
using ProvidedDependencies.Data;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

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

string csprojDir = Path.GetDirectoryName(csprojFile)!;
Environment.CurrentDirectory = csprojDir;

Task<ProcessResult> cacheDirOutput = Process.ExecFile("dotnet", "nuget", "locals", "global-packages", "--list");

Console.Write("Restoring {0}... ", Path.GetFileName(csprojFile));
if (await Process.ExecFile("dotnet", "restore", csprojFile, "--force", "--force-evaluate", "-p:RestoreEnablePackagePruning=false") is { ExitCode: not 0 } restore) {
    Console.WriteLine("\nRestore failed: {0}", restore.StdOut);
    return restore.ExitCode;
}
Console.WriteLine("done.");

Encoding                              utf8                      = new UTF8Encoding(false, true);
using UnfuckedHttpClient              http                      = new() { Timeout = TimeSpan.FromMinutes(10) };
using NuGetGalleryService             nuGetGallery              = new(http);
XmlSerializer                         frameworkListDeserializer = new(typeof(FrameworkFileList));
Task<IReadOnlyCollection<Dependency>> projectDependenciesTask   = DependencyLister.listDependencies(csprojDir);
Task<ProcessResult> projectPropertiesTask = Process.ExecFile("dotnet", "msbuild", csprojFile, "-getProperty:" + ((ReadOnlySpan<string>) [
    "managepackageversionscentrally",
    "directorypackagespropspath"
]).Join(','));

string    projectFileContents = await File.ReadAllTextAsync(csprojFile);
XDocument projectDoc          = XDocument.Parse(projectFileContents, LoadOptions.None);
bool      isAspNetCoreProject = projectDoc.Root!.Attribute("Sdk")?.Value.Equals("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase) ?? false;

string propsFilename       = outputFile.Value() ?? Path.Combine(csprojDir, DEFAULT_PROPS_FILENAME);
string expectedPropsImport = Path.GetRelativePath(csprojDir, propsFilename);
bool projectImportedProps = projectDoc.Root!.Elements("Import")
    .Any(import => Path.Dos2UnixSlashes(import.Attribute("Project")?.Value) is {} actual && actual.Equals(Path.Dos2UnixSlashes(expectedPropsImport), StringComparison.OrdinalIgnoreCase));

IList<NuGetFramework> projectTfms = projectDoc.Descendants("TargetFrameworks")
    .Concat(projectDoc.Descendants("TargetFramework"))
    .Single().Value
    .Split(';', StringSplitOptions.TrimEntries)
    .Select(NuGetFramework.Parse)
    .Where(framework => framework.Framework == ".NETCoreApp").ToList();
ISet<string> projectTfmNames = projectTfms.Select(framework => framework.GetShortFolderName()).ToHashSet();

Task<IDictionary<string, PackageVersion>>  runtimeRefVersions           = fetchReleaseVersionNumbers(false);
Task<IDictionary<string, PackageVersion>>? aspNetCoreRuntimeRefVersions = isAspNetCoreProject ? fetchReleaseVersionNumbers(true) : null;

string userPackageCacheDir = (await cacheDirOutput).StdOut.TrimStart("global-packages: ");
IDictionary<Version, ISet<string>> providedPackagesByRuntimeVersion = (await Task.WhenAll(projectTfms.DistinctBy(tfm => tfm.Version).Select(async projectTfm => {
    Task<ISet<string>?> runtimePackagesTask = listRuntimeProvidedPackages(projectTfm, await runtimeRefVersions);
    Task<ISet<string>?> aspNetCoreRuntimePackagesTask =
        aspNetCoreRuntimeRefVersions != null ? listRuntimeProvidedPackages(projectTfm, await aspNetCoreRuntimeRefVersions) : Task.FromResult<ISet<string>?>(null);

    ISet<string> allProvidedPackages = await runtimePackagesTask ?? new HashSet<string>();
    allProvidedPackages.UnionWith(await aspNetCoreRuntimePackagesTask ?? ImmutableHashSet<string>.Empty);

    return new KeyValuePair<Version, ISet<string>>(projectTfm.Version, allProvidedPackages);
}))).ToDictionary();

IReadOnlyCollection<Dependency> resolvedProjectDependencies = await projectDependenciesTask;

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
                                       tfms: (ISet<string>) packages.Select(dependency => dependency.tfm.GetShortFolderName()).ToHashSet()))
    .OrderBy(providedPackage => providedPackage.packageName, StringComparer.OrdinalIgnoreCase)
    .ToList();

IDictionary<string, string> projectProperties = JsonNode.Parse((await projectPropertiesTask).StdOut)!["Properties"]!.AsObject()
    .ToDictionary(static pair => pair.Key.ToLowerInvariant(), static pair => pair.Value!.GetValue<string>().EmptyToNull).Compact();
bool centralPackageManagementEnabled = projectProperties.TryGetValue("managepackageversionscentrally", out string? managePackageVersionsCentrally) &&
    "true".Equals(managePackageVersionsCentrally, StringComparison.OrdinalIgnoreCase);

await using FileStream propsFileStream = File.Create(propsFilename);

string    generatedComment = $"Generated by {nameof(ProvidedDependencies)} {Assembly.GetEntryAssembly()!.GetName().Version!.ToString(3)} on {DateTimeOffset.UtcNow:O}";
Project   propsFile        = new() { comment = generatedComment };
ItemGroup itemGroup        = new() { label   = "Provided by runtime", condition = "'$(_IsPublishing)' == 'true'" };
propsFile.itemGroups.Add(itemGroup);
itemGroup.packageReferences.AddAll(providedPackages.SelectMany(providedPackage => {
    bool conditionalDependency = !providedPackage.tfms.SetEquals(projectTfmNames);
    PackageReference packageReference = new() {
        include       = providedPackage.packageName,
        excludeAssets = "all",
        version       = centralPackageManagementEnabled ? null : providedPackage.latestPatchForProvidedMinorVersion.ToNormalizedString(),
        privateAssets = "all"
    };
    return conditionalDependency ? providedPackage.tfms.Select(tfm => packageReference with { condition = $"'(TargetFramework)' == '{tfm}'" }) : [packageReference];
}));
// show all dependencies in project.assets.json so this can exclude them, which is more robust than RestoreEnablePackagePruning that still crashes in single-file framework-dependent roll-forward apps
propsFile.propertyGroup.Add("RestoreEnablePackagePruning", "false");

string xmlFileIndentation = projectIndentation.Match(projectFileContents).Value;

XmlSerializer projectSerializer = new(typeof(Project));
string        xmlLineEnding     = projectFileContents[Math.Max(0, projectFileContents.IndexOf('\n') - 1)] == '\r' ? "\r\n" : "\n";
XmlWriterSettings xmlWriterSettings = new() {
    Indent             = true,
    IndentChars        = xmlFileIndentation,
    Async              = true,
    OmitXmlDeclaration = true,
    Encoding           = utf8,
    NewLineChars       = xmlLineEnding
};
await using XmlWriter xmlWriter = XmlWriter.Create(propsFileStream, xmlWriterSettings);

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

if (centralPackageManagementEnabled) {
    await using FileStream directoryPackagesPropsFileStream = new(projectProperties["directorypackagespropspath"], FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    await using XmlWriter  directoryPackagesPropsFileWriter = XmlWriter.Create(directoryPackagesPropsFileStream, xmlWriterSettings);

    Project directoryPackagesProps = directoryPackagesPropsFileStream.Length != 0 ? (Project) projectSerializer.Deserialize(directoryPackagesPropsFileStream)! : new Project {
        propertyGroup = {
            ["ManagePackageVersionsCentrally"] = "true"
        }
    };
    directoryPackagesProps.comment = generatedComment;

    string itemGroupLabel = "Provided by runtime to " + Path.GetFileNameWithoutExtension(csprojFile);
    if (directoryPackagesProps.itemGroups.FirstOrDefault(group => itemGroupLabel.Equals(group.label, StringComparison.OrdinalIgnoreCase)) is not {} projectSpecificCpmItemGroup) {
        projectSpecificCpmItemGroup = new ItemGroup { label = itemGroupLabel };
        directoryPackagesProps.itemGroups.Add(projectSpecificCpmItemGroup);
    }

    projectSpecificCpmItemGroup.condition = "'$(_IsPublishing)' == 'true'";
    projectSpecificCpmItemGroup.packageVersions.Clear();
    projectSpecificCpmItemGroup.packageVersions.AddAll(providedPackages.Select(providedPackage => new PackageReference {
        include = providedPackage.packageName,
        version = providedPackage.latestPatchForProvidedMinorVersion.ToNormalizedString()
    }));

    ISet<string> includedPackageVersions = new HashSet<string>();
    foreach (PackageReference packageVersion in directoryPackagesProps.itemGroups.SelectMany(group => group.packageVersions)) {
        if (packageVersion.include is {} packageName && !includedPackageVersions.Add(packageName.ToLowerInvariant())) {
            packageVersion.update  = packageName;
            packageVersion.include = null;
        }
    }

    directoryPackagesPropsFileStream.SetLength(0);

    projectSerializer.Serialize(directoryPackagesPropsFileWriter, directoryPackagesProps, namespaces);
    Console.WriteLine("Wrote {0}", directoryPackagesPropsFileStream.Name);
}

return 0;

async Task<IDictionary<string, PackageVersion>> fetchReleaseVersionNumbers(bool aspNetCoreRuntime) =>
    (await nuGetGallery.fetchAllVersions(aspNetCoreRuntime ? "Microsoft.AspNetCore.App.Ref" : "Microsoft.NETCore.App.Ref"))
    .Where(packageVersion => !packageVersion.nuGetVersion.IsPrerelease)
    .ToDictionary(version => version.version);

async Task<ISet<string>?> listRuntimeProvidedPackages(NuGetFramework tfm, IDictionary<string, PackageVersion> availableRuntimeVersions) {
    const string FRAMEWORK_LIST_FILENAME = "FrameworkList.xml";
    if (tfm.Version.Float(NuGetVersionFloatBehavior.Patch).FindBestMatch(availableRuntimeVersions.Select(pair => pair.Value.nuGetVersion)) is {} bestMatch) {
        PackageVersion refPackageToFetch = availableRuntimeVersions[bestMatch.ToNormalizedString()];
        string         appRefDir         = Path.Combine(userPackageCacheDir, refPackageToFetch.name, refPackageToFetch.version);
        Stream         frameworkListStream;
        try {
            frameworkListStream = File.OpenRead(Path.Combine(appRefDir, "data", FRAMEWORK_LIST_FILENAME));
        } catch (IOException) {
            string cachedFrameworkListFilePath = Path.Combine(userPackageCacheDir, refPackageToFetch.name, "FrameworkLists for ProvidedDependencies",
                refPackageToFetch.version, FRAMEWORK_LIST_FILENAME);
            try {
                frameworkListStream = File.OpenRead(cachedFrameworkListFilePath);
            } catch (IOException) {
                Console.Write("Downloading .NET runtime reference assembly {0} {1}... ", refPackageToFetch.name, refPackageToFetch.version);
                await using Stream     packageStream            = await nuGetGallery.downloadPackage(refPackageToFetch);
                await using ZipArchive packageZip               = await ZipArchive.CreateAsync(packageStream, ZipArchiveMode.Read, false, null);
                ZipArchiveEntry        frameworkDataFileInZip   = packageZip.GetEntry(Path.Dos2UnixSlashes(Path.Combine("data", FRAMEWORK_LIST_FILENAME)))!;
                await using Stream     frameworkDataStreamInZip = await frameworkDataFileInZip.OpenAsync();
                MemoryStream           frameworkDataFileBuffer  = new((int) frameworkDataFileInZip.Length);
                await frameworkDataStreamInZip.CopyToAsync(frameworkDataFileBuffer);
                frameworkDataFileBuffer.Position = 0;
                Directory.CreateDirectory(Path.GetDirectoryName(cachedFrameworkListFilePath)!);
                await using FileStream cachedAppRefFrameworkListStream = File.Open(cachedFrameworkListFilePath, FileMode.CreateNew, FileAccess.Write);
                await frameworkDataFileBuffer.CopyToAsync(cachedAppRefFrameworkListStream);
                frameworkDataFileBuffer.Position = 0;
                frameworkListStream              = frameworkDataFileBuffer;
                Console.WriteLine("done.");
            }
        }
        await using (frameworkListStream) {
            FrameworkFileList frameworkList = (FrameworkFileList) frameworkListDeserializer.Deserialize(frameworkListStream)!;
            return frameworkList.files.Select(file => file.assemblyName).ToHashSet();
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

    [GeneratedRegex(@"(?=</\s*Project\s*>\s*$)", RegexOptions.IgnoreCase)]
    private static partial Regex projectEndTag { get; }

    [GeneratedRegex(@"(?<=\n)[ \t]+?(?=\S)")]
    private static partial Regex projectIndentation { get; }

}