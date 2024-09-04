🌳 DependencyHierarchy
===

[![Nuget](https://img.shields.io/nuget/v/deps?logo=nuget)](https://www.nuget.org/packages/deps/)

Command-line tool that shows and filters a tree of a C# project's effective NuGet package dependencies and their versions, including transitive dependencies.

<!-- MarkdownTOC autolink="true" bracket="round" autoanchor="false" levels="1,2,3" bullets="-" -->

- [Prerequisites](#prerequisites)
- [Installation](#installation)
    - [Update](#update)
- [Usage](#usage)
    - [Version conflict highlighting](#version-conflict-highlighting)
- [Options](#options)
    - [Filter to only show one package](#filter-to-only-show-one-package)
    - [Project directory](#project-directory)
    - [Disable text colors](#disable-text-colors)
- [Acknowledgements](#acknowledgements)

<!-- /MarkdownTOC -->

## Prerequisites
- [.NET Runtime 6 or later](https://dotnet.microsoft.com/en-us/download/dotnet)
- A C# project's source code to inspect

## Installation
This will install the tool for your user, to be run from any directory.
```sh
dotnet tool install --global deps
```

### Update
To update an existing installation to the latest version.
```sh
dotnet tool update --global deps
```

## Usage
1. Go to a C# project directory. This is the directory that has a `.csproj` file and an `obj` subdirectory.
    ```sh
    cd solution/project/
    ```
1. If you haven't restored this project since changing its settings, cleaning, or cloning it, restore it to ensure that `obj/project.assets.json` is up-to-date. Opening the project in Visual Studio or building it will also restore it automatically.
    ```sh
    dotnet restore
    ```
1. Run the `deps` tool.
    ```sh
    deps
    ```

This will print all of the effective NuGet dependencies of your project.
```text
CSCore : 1.2.1.2
PowerMate : 1.1.1
  HidClient : 1.0.1
    HidSharp : 2.1.0
```
The leftmost, least indented packages are top-level/direct dependencies, manually defined in your `.csproj` or `packages.config` file. Indented packages underneath them are their transitive dependencies that are automatically included when you declare the top-level dependency. The most indented packages are leaf dependencies.

The above example shows that the [project](https://github.com/Aldaviva/PowerMate/tree/master/PowerMateVolume) has two direct dependencies: [`CSCore`](https://www.nuget.org/packages/CSCore) and [`PowerMate`](https://www.nuget.org/packages/PowerMate). The `PowerMate` package also has a transitive dependency on [`HidClient`](https://www.nuget.org/packages/HidClient), which in turn has a transitive dependency on [`HidSharp`](https://www.nuget.org/packages/HidSharp), which has no other dependencies.

### Version conflict highlighting
C# projects can only install one version of any given package. If different versions of the same package are all dependencies of your project and its transitive dependencies, the compiler can't use all of them, so NuGet will perform [automatic version conflict resolution](https://learn.microsoft.com/en-us/nuget/concepts/dependency-resolution) to pick one version to actually import.

This tool will show all of the candidate versions of dependencies, and will highlight when that version differs from the effective version that NuGet chose. This can help you figure out why a specific version of a package is in your project.
```text
BouncyCastle.Cryptography : 2.3.1
MailKit : 4.5.0
  MimeKit : 4.5.0
    BouncyCastle.Cryptography : 2.3.0 (omitted for conflict with 2.3.1)
    System.Security.Cryptography.Pkcs : 8.0.0
      System.Formats.Asn1 : 8.0.0
```
In the above example, a project depends on [`MailKit`](https://www.nuget.org/packages/MailKit), which transitively depends on [`BouncyCastle.Cryptography`](https://www.nuget.org/packages/BouncyCastle.Cryptography/). At the time, the latest version of `MailKit` depended on the version 2.3.0 of `BouncyCastle.Cryptography`, which is vulnerable to [several](https://github.com/advisories/GHSA-8xfc-gm6g-vgpv) [security](https://github.com/advisories/GHSA-m44j-cfrm-g8qc) [issues](https://github.com/advisories/GHSA-v435-xc8x-wvr9). To resolve these, the newer, fixed 2.3.1 version of `BouncyCastle.Cryptography` was added as a direct, top-level dependency of the project. Doing so [overrode](https://learn.microsoft.com/en-us/nuget/concepts/dependency-resolution#direct-dependency-wins) the version that `MailKit` transitively depended upon, forcing `MailKit` to use the fixed version. This tool highlights the version conflict and how it was resolved.

## Options
### Filter to only show one package
By default, the output shows all dependencies of the project. To only show one package and its dependents, while hiding all unrelated packages, pass the `--filter` or `-f` option. This can help you figure out why a specific package is in your project.
```sh
deps --filter HidClient
```
```text 
PowerMate : 1.1.1
  HidClient : 1.0.1
```

### Project directory
By default, this tool looks for a C# project (that contains an `obj` subdirectory) in the current working directory. To work with a project in a different directory, you can do one of the following.
- Change to the project directory using `cd`:
    ```sh
    cd solution/project/
    ```
- Specify the project directory as a command-line argument:
    ```sh
    deps solution/project/
    ```

### Disable text colors
By default, the output text is colored to make it easier to distinguish different package names, versions, and conflict highlights. To disable this coloring and not print any ANSI escape sequences, for example if you want to process the output in a text editor or CI build tool, you can pass the `--no-color` option.
```sh
deps --no-color
```

## Acknowledgements
- [Eclipse m2e](https://eclipse.dev/m2e/) for implementing this essential functionality a very long time ago, in a GUI
- [`npm ls`](https://docs.npmjs.com/cli/v10/commands/npm-ls) for implementing this essential functionality in a command-line interface
- [DotNetWhy](https://www.nuget.org/packages/DotNetWhy) for inspiring me to create a .NET Tool that could answer my questions