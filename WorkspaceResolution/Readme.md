WorkspaceResolution
===

<!-- MarkdownTOC autolink="true" bracket="round" autoanchor="false" levels="1,2" bullets="-" -->

- [Prerequisites](#prerequisites)
- [Problem](#problem)
- [Solution](#solution)
- [Installation](#installation)
- [Usage](#usage)
- [Configuration](#configuration)
- [Considerations](#considerations)
- [Acknowledgements](#acknowledgements)

<!-- /MarkdownTOC -->

## Prerequisites
- Visual Studio for Windows (tested with Community 2022)
- .NET Runtime 9 or later
- C# projects with `PackageReference` dependencies on NuGet packages

## Problem

When simultaneously developing both a .NET library NuGet package as well as another library or program that depends on it, propagating changes from the dependency package to its dependents is slow and difficult. It makes updates, refactoring, and debugging painful. The pain increases with the depth of the dependency tree.

All previous techniques that I've tried and rejected:

1. Using a `PackageReference` to a package hosted in NuGet Gallery prevents changes from 
being propagated at all until you pack and upload, which takes at least 5 minutes per change and can seriously clutter up NuGet Gallery. Even if you use an alternative server like GitHub Packages or Sonatype Nexus, you still have to pack, deploy, and restore each time.
    - Using a local folder as an additional NuGet source for package development avoids the Gallery delay and clutter, but still requires extra steps for changes to propagate in a dependent project after the dependency build.
    - You could write scripts or functions to simplify this to 2 commands, but then you have multiple terminal windows open, which you have to focus and hit <kbd>↑</kbd> <kbd>Enter</kbd> on every change.
        1. Pack dependency into NuGet package (or set `<GeneratePackageOnBuild>` to `true`)
        1. Copy package and symbol sidecar file into local NuGet source folder
        1. Delete old package version folder from global packages folder
        1. Restore each dependent project, forcing evaluation if package lock files are used
1. Using a `ProjectReference` updates quickly on dependency build, but loses transitive dependencies, which often breaks your build due to missing packages. Every package reference in every dependent project file must be modified. You could manually add those transitive dependencies, but this becomes unmaintainable and rife with version mismatches. Also, this cannot be published to NuGet Gallery, because the `ProjectReference` will break for consumers, and build machines probably won't fare any better.
1. [NuGet Reference Switcher](https://marketplace.visualstudio.com/items?itemName=RicoSuter.NuGetReferenceSwitcherforVisualStudio2017) requires manual menu clicking, has not been updated since 2017, and does not work with Visual Studio 2019, 2022, or 2026.
1. Conditional `PackageReference` and `ProjectReference` switching using `Choose` based on `Configuration` works well, but requires constant annoying, non-obvious configuration switching and adds at least 11 lines of boilerplate XML per dependency. Every package reference in every dependent project file must be modified. Requires that all team members have all dependencies checked out and have the same relative folder structure for projects.
    ```xml
    <Choose>
        <When Condition="'$(Configuration)' == 'Release'">
            <ItemGroup>
                <PackageReference Include="Unfucked" />
            </ItemGroup>
        </When>
        <Otherwise>
            <ItemGroup>
                <ProjectReference Include="..\Unfucked\Unfucked.csproj" />
            </ItemGroup>
        </Otherwise>
    </Choose>
    ```
    This technique works very well for single solutions with multiple package projects that depend upon each other. It does not work well when projects depend on package projects in other solutions. I recommend `Choose` for intra-solution dependencies, and Workspace Resolution for inter-solution dependencies.
    - [NuGet dependency resolution](https://learn.microsoft.com/en-us/nuget/concepts/dependency-resolution) breaks with error [NU1106](https://learn.microsoft.com/en-us/nuget/reference/errors-and-warnings/nu1106) when a transitive dependency has multiple candidate versions, even if they should be solvable, such as "≥1.0.1 and ≥1.0.2." You also need to manually restore quite often to avoid Visual Studio errors.
    - The `GenerateDepsFile` build task fails with an `ArgumentException` for some reason.

Overall, Microsoft does not seem to understand or take this problem seriously, possibly because no one who works there develops software where they extract shared code into a package hosted on NuGet. Maybe everyone there works on exactly one project and leaves the other projects to other teams, slowly throwing every change over the wall.

|Technique|User&nbsp;inputs (b=builds, n=dependents)|Dependency project changes|Dependent project changes|CI/team safe|Transitive dependencies|
|:-|:-|:-|:-|:-|:-|
|1.&nbsp;PackageReference|❌ 2b(1+n)|✅ false|✅ false|✅ true|✅ true|
|2.&nbsp;ProjectReference|❌ 1|✅ false|❌ true|❌ false|⚠&nbsp;sometimes<sup>1</sup>|
|3.&nbsp;NuGet&nbsp;Ref&nbsp;Switcher|❌ 1|✅ false|✅ false|✅ true|⚠&nbsp;sometimes<sup>1</sup>|
|4.&nbsp;Choose|❌ 1|✅ false|❌ true|❌ false|⚠&nbsp;sometimes<sup>1</sup>|
|5.&nbsp;Workspace&nbsp;Resolution|✅ 0|✅ optional<sup>2</sup>|✅ false|✅ true|✅ true|

1. `ProjectReference` dependencies on package projects
    - are transitive in SDK-style dependent projects, regardless of which frameworks they target. It is intransitive in older Framework-only, non-SDK-style dependent projects. The project type of the dependency package is irrelevant.
    - fail to solve transitive package version resolution that should be solvable, so all dependencies must use exact versions like `[1.0.0]` instead of the default greater than or equal to behavior of `1.0.0`, which is `[1.0.0,)` or `≥1.0.0`.
    - require frequent manual restorations to make Visual Studio stop throwing warnings when the dependency is in a different solution.
2. You may already be using it in your package projects, but if not, enabling `GeneratePackageOnBuild` makes development faster by quickly updating dependents. Otherwise, if you want to avoid any package project changes, you can also pack other ways, such as adding this property to a user-wide MSBuild props file, running `dotnet pack`, or assigning a keyboard shortcut to `Build.Packselection`.

Workspace Resolution automates the local folder `PackageReference` technique to avoid changing any project files, breaking other cloners, or breaking transitive dependencies.

## Solution

Java developers face this same problem with Maven packages, but [m2e](https://eclipse.dev/m2e/) easily solves it with its Workspace Resolution feature, which is preinstalled in Eclipse for Java EE Developers and is enabled by default. It always works, takes no thinking, and saves an enormous amount of time and effort. This may be news to .NET developers, but better builds are possible.

Trying to do this with Carthage or Swift Package Manager will throw you into a murderous rage, so don't attempt it for the safety of yourself and others.

This project automates technique 5 to get update propagation from NuGet packages to dependent local projects down to one hotkey press, zero project changes, and zero workflow changes. It's not quite as fast as m2e because Roslyn cannot automatically build as you type, unlike Eclipse's industry-leading ECJ realtime incremental compiler, but it can still update dependents within a few seconds, which is significantly faster, easier, and less aggravating than all other .NET alternatives.

### How it works

### Installation time
1. Add a program to a folder on the `PATH`. Its job is to restore open projects that depend on the newly packed package.
1. Use the program to add a local NuGet package source folder and an MSBuild `props` file to your user's home directory. This adds a target to all builds in your user account that runs after all projects are packed to start the process of workspace resolution.
1. Optionally set `GeneratePackageOnBuild` to `true` in your package project to make packing faster and easier with <kbd>Ctrl</kbd>+<kbd>B</kbd>.

### Compile time
1. Build or explicitly pack the package project. This will automatically restore dependent, open C# projects to point to the dependency's new build artifact. To accomplish this, WorkspaceResolution will
    1. Copy the new package file to the local Nuget source folder
    1. Delete the old cached version of this package from the NuGet global package cache
    1. Launch the Workspace Resolution program, which will:
    1. Find all running Visual Studio instances
    1. Find all C# projects currently open in Visual Studio
    1. List the NuGet dependencies of each project based on the last time the project was restored
    1. If the project depends on the recently updated package, restore the dependent by running `dotnet restore`
    1. Visual Studio will notice that the dependent project was restored and will reanalyze it to update any build errors, such as a renamed method

## Installation
1. Download `WorkspaceResolution.exe` to a folder on your `PATH`.
1. Configure the local package source folder and user-wide MSBuild post-pack target by running
    ```ps1
    WorkspaceResolution.exe install
    ```
    *You only have to do this once per Windows user account, even if you later upgrade Visual Studio.*
1. Optionally enable the `GeneratePackageOnBuild` property in your package project file to enable single-key package updates.
    ```xml
    <PropertyGroup>
        <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    </PropertyGroup>
    ```

## Usage
1. Pack the dependency package project by pressing <kbd>Ctrl</kbd>+<kbd>B</kbd>.
    - If you have [JetBrains dotCover Continuous Testing](https://www.jetbrains.com/help/dotcover/Continuous_Testing.html) enabled, you can just save with <kbd>Ctrl</kbd>+<kbd>S</kbd> to automatically build the package and restore dependent projects with Workspace Resolution.
    - If you didn't enable `GeneratePackageOnBuild`, run `dotnet pack` in your package project, or right-click the project in Solution Explorer and click Pack.

## Configuration

### Disable Workspace Resolution
If you want to skip Workspace Resolution for a specific project or build, you can set the `WorkspaceResolution` property to `false` in a project file, `-p` argument, or a `props` file for a directory, user, or computer.

### Change local NuGet package source
By default, a new local NuGet package source folder is added at `~\.nuget\local`. Packed dependency package `nupkg` and `snupkg` files are copied here so that the NuGet client can find them and copy them to `~\.nuget\packages` while restoring dependent projects.

To change this directory, you can pass it as the `--local-package-dir` command-line option when installing `WorkspaceResolution`.
```ps1
WorkspaceResolution.exe install --local-package-dir "C:\Users\Ben\AppData\Local\.nuget\custom-local"
```

### Exclude projects in package's same solution from restoration
If other projects in the package's solution depend on your package using a hybrid configuration-sensitive `PackageReference` and `ProjectReference` technique, restoring the dependent projects each time can do nothing more than delay unit test executions.

You may still want workspace resolution for other projects in other solutions which always depend upon your package using NuGet `PackageReference`. To do this, disable automatic restores of projects in the same solution by setting the `SkipSameSolutionWorkspaceResolution` property to `true`, either in the dependency project file, a [global properties file](https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-your-local-build?view=vs-2022#use-msbuildextensionspath-and-msbuilduserextensionspath), or on the command line during builds. This property is safe to check into version control because setting it doesn't change or break the build for consumers who don't have Workspace Resolution installed, including build machines.
```xml
<PropertyGroup>
    <SkipSameSolutionWorkspaceResolution>true</SkipSameSolutionWorkspaceResolution>
</PropertyGroup>
```
*or*
```ps1
dotnet build -p:SkipSameSolutionWorkspaceResolution=true
```

## Considerations
- Enabling `GeneratePackageOnBuild` disables the implicit build during `dotnet pack` to avoid an infinite loop, so if you relied on that, you will need to either `dotnet build; dotnet pack` or `dotnet pack -p:GeneratePackageOnBuild=false`.
- Concurrent reads of `project.assets.json` can cause the following benign warning, which can be ignored.
    > Unable to use package assets cache due to I/O error. This can occur when the same project is built more than once in parallel. Performance may be degraded, but the build result will not be impacted.
- Using a local NuGet package source folder with package lock files can lead to mismatched package hashes when you locally pack a package, then build it on a CI machine and deploy the CI artifact to NuGet Gallery, then restore a project that depends on the package and uses a lock file. The NuGet global package cache folder will still have the development package cached, with a different hash than the CI package uploaded to NuGet Gallery, so the dependent lock file will have the wrong hash too, which will break the build when the dependent project is built on a different computer like a build machine.
    - To solve this, clear the local cache by deleting `~\.nuget\packages\$packageName\$packageVersion\` and `~\.nuget\local\$packageName.$packageVersion.nupkg`, then restore the dependent project with `dotnet restore --force-evaluate` after uploading the package to NuGet Gallery.
    - I automate this by always pushing to NuGet Gallery using a PowerShell function that automatically builds, packs, and pushes the package, then clears these local development caches. The biggest benefit is not having to type the package filename each time, but preventing hash mismatches is convenient too.

## Acknowledgements
- [**m2eclipse**](https://eclipse.dev/m2e/) for doing this the right way since 2010 or earlier
- [**Andrew Brindamour**](https://github.com/abrindam) for writing scripts to wrap Carthage in an attempt to reduce this flow from 5 CLI commands to 2, like technique 1