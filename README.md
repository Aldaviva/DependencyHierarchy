🌳 DependencyHierarchy
===

[![Nuget](https://img.shields.io/nuget/v/deps?logo=nuget)](https://www.nuget.org/packages/deps/) [![GitHub Actions](https://img.shields.io/github/actions/workflow/status/Aldaviva/DependencyHierarchy/dotnetpackage.yml?branch=master&logo=github)](https://github.com/Aldaviva/DependencyHierarchy/actions/workflows/dotnetpackage.yml)

Command-line tool that shows and filters a tree of a .NET project's effective NuGet package dependencies and their versions, including transitive dependencies.

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

![Screenshot](https://raw.githubusercontent.com/Aldaviva/DependencyHierarchy/master/.github/images/screenshot1.png)

## Prerequisites
- [.NET Runtime 6 or later](https://dotnet.microsoft.com/en-us/download/dotnet)
- A .NET project's source code you want to inspect

## Installation
This will install the tool for your user, to be run from any directory.
```ps1
dotnet tool install --global deps
```

### Update
To update an existing installation to the latest version.
```ps1
dotnet tool update --global deps
```

## Usage
1. Go to a .NET project directory. This is the directory that has an `obj` subdirectory. For C# projects, this is usually the same directory that contains your `.csproj` file.
    ```ps1
    cd mysolution/myproject/
    ```
1. If you haven't restored this project since changing its settings, cleaning, or cloning it, restore its depedencies it to ensure that `obj/project.assets.json` is up-to-date. Opening the project in Visual Studio or building it with `dotnet build` will also restore it automatically.
    ```ps1
    dotnet restore
    ```
    This tool does not automatically restore your project for you, so that you can specify [all the options to `dotnet restore`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-restore#options) which you need.
1. Run the `deps` tool.
    ```ps1
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

The above example shows that the [project](https://github.com/Aldaviva/PowerMate/tree/master/PowerMateVolume) has two direct dependencies: [`CSCore`](https://www.nuget.org/packages/CSCore) and [`PowerMate`](https://www.nuget.org/packages/PowerMate). The `PowerMate` package also has a transitive dependency on [`HidClient`](https://www.nuget.org/packages/HidClient), which in turn has a transitive dependency on the leaf [`HidSharp`](https://www.nuget.org/packages/HidSharp).

### Version conflict highlighting
.NET projects can only install one version of any given package. If different versions of the same package are all dependencies of your project, the compiler can't use all of them, so NuGet will perform [automatic version conflict resolution](https://learn.microsoft.com/en-us/nuget/concepts/dependency-resolution) to pick the one version to actually import.

This tool will show all of the candidate versions of dependencies, and will highlight when that version differs from the effective version that NuGet chose. This can help you figure out why a specific version of a package is in your project.
```text
BouncyCastle.Cryptography : 2.3.1
MailKit : 4.5.0
  MimeKit : 4.5.0
    BouncyCastle.Cryptography : 2.3.0 (omitted for conflict with 2.3.1)
    System.Security.Cryptography.Pkcs : 8.0.0
      System.Formats.Asn1 : 8.0.0
```
In the above example, a [project](https://github.com/Aldaviva/mailsender-cs/tree/master/MailSender-NetCore) depends on [`MailKit`](https://www.nuget.org/packages/MailKit), which transitively depends on [`BouncyCastle.Cryptography`](https://www.nuget.org/packages/BouncyCastle.Cryptography/). At the time, the latest version of `MailKit` depended on version 2.3.0 of `BouncyCastle.Cryptography`, which is vulnerable to [several](https://github.com/advisories/GHSA-8xfc-gm6g-vgpv) [security](https://github.com/advisories/GHSA-m44j-cfrm-g8qc) [issues](https://github.com/advisories/GHSA-v435-xc8x-wvr9). To resolve these, the newer, fixed 2.3.1 version of `BouncyCastle.Cryptography` was added as a direct, top-level dependency of the project. Doing so [overrode](https://learn.microsoft.com/en-us/nuget/concepts/dependency-resolution#direct-dependency-wins) the version that `MailKit` transitively depended upon, forcing `MailKit` to use the fixed version. This tool highlights the version conflict and how it was resolved.

## Options
### Filter to only show one package
By default, the output shows all dependencies of the project. To only show one package and its dependents, while hiding all unrelated packages, pass the `--filter` or `-f` option. This can help you figure out why a specific package is in your project.
```ps1
deps --filter HidClient
```
```text 
PowerMate : 1.1.1
  HidClient : 1.0.1
```
This example shows that `HidClient` is in the project because it is a transitive dependency of the `PowerMate` top-level dependency.

### Project directory
By default, this tool looks for a .NET project (that contains an `obj` subdirectory) in the current working directory. To work with a project in a different directory, you can do one of the following.
- Change to the project directory using `cd`:
    ```ps1
    cd mysolution/myproject/
    ```
- Specify the project directory (absolute or relative to the current working directory) as a command-line argument:
    ```ps1
    deps mysolution/myproject/
    ```

### Disable text colors
By default, the output text is colored to make it easier to distinguish different package names, versions, and conflict highlights. To disable this coloring and not print any ANSI escape sequences, for example if you want to process the output in a text editor or CI build tool, you can pass the `--no-color` option.
```ps1
deps --no-color
```
This program will also attempt to automatically disable color output if the console doesn't support it, but this argument allows you to force it off if the heuristic fails.

## Acknowledgements
- [Eclipse m2e](https://eclipse.dev/m2e/) for implementing this essential functionality a very long time ago in a GUI, better than any other IDE or tool ever has
- [`npm ls`](https://docs.npmjs.com/cli/v10/commands/npm-ls) for implementing this essential functionality in a command-line interface
- [DotNetWhy](https://www.nuget.org/packages/DotNetWhy) for inspiring me to create a .NET Tool that could answer my questions and that looked good
- [DependencyGraph.App](https://www.nuget.org/packages/DependencyGraph.App/) for doing the same thing as my program, but NuGet Gallery search is so ineffective that it didn't show me this tool when I searched for ["dependencies"](https://www.nuget.org/packages?q=dependencies&includeComputedFrameworks=true&packagetype=dotnettool&prerel=false&sortby=relevance), and I only found it after I had already built and published my program
- [`dotnet list package`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-list-package) and [`dotnet nuget why`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-why) for being crappy too-little-too-late first-party attempts that don't solve this problem (not transitive and not full project, respectively)