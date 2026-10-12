ProvidedDependencies
===

[![GitHub Actions](https://img.shields.io/github/actions/workflow/status/Aldaviva/DependencyHierarchy/dotnetpackage.yml?branch=master&logo=github)](https://github.com/Aldaviva/DependencyHierarchy/actions/workflows/dotnetpackage.yml)

<!-- MarkdownTOC autolink="true" bracket="round" autoanchor="false" levels="1,2,3" bullets="-" -->

- [Prerequisites](#prerequisites)
- [Problem](#problem)
- [Solution](#solution)
- [Usage](#usage)

<!-- /MarkdownTOC -->

## Prerequisites
- [.NET SDK 10 or later](https://dotnet.microsoft.com/download)
- A .NET project's source code you want to edit

## Problem
1. Build a single-file, framework-dependent, major&ndash;roll-forward .NET program that targets not the latest runtime version, such as .NET 9.
1. Dependencies, even those that would have been provided by the runtime like `System.Diagnostics.DiagnosticSource@9.0.11`, are bundled and linked inside the app's executable.
1. Run that program on a newer runtime version, like .NET 10.
1. The runtime's assembly resolver will load dependencies from inside the executable at a higher priority than the runtime installation directory.
1. When the runtime tries to also use a dependency, it will get the old version bundled in the app instead of the new version in its installation directory, and this version downgrade causes a crash at runtime with a `FileNotFoundException`.

This problem is easier to encounter with ASP.NET Core Runtime, which provides a strict superset of the libraries from .NET Runtime.

[**Detailed explanation**](https://gist.github.com/Aldaviva/8d2256604b0b129dcaf761d583b2df29)

## Solution
This project automates the [solution described here](https://gist.github.com/Aldaviva/8d2256604b0b129dcaf761d583b2df29#solution).

It adds `PackageReference`s to your project which only take effect during publishing, and that mark any dependencies which are also provided by the runtime as excluded, so they won't be bundled into your app and the possibly newer versions provided in the runtime installation directory will be loaded instead. This is similar to setting a dependency's `scope` to `provided` in Maven.

This is a more correct and comprehensive alternative to .NET SDK 10's `<RestoreEnablePackagePruning>true</RestoreEnablePackagePruning>`, which sounds like it would be useful, but is actually counterproductive, doesn't fix all crashes, and causes build failures when excluding dependencies, especially when multi-targeting runtimes both <10 and ≥10.

## Usage
Go to your project directory and run:

```ps1
ProvidedDependencies.exe
```

This will:

1. create or update a [`providedDependencies.g.props`](https://github.com/Aldaviva/GamesDoneQuickCalendarFactory/blob/06773e11dedf44d8084887b68ddad0758820cc89/GamesDoneQuickCalendarFactory/providedDependencies.g.props) file in your project that excludes all dependencies of your project which are provided by the runtime
1. [import](https://github.com/Aldaviva/GamesDoneQuickCalendarFactory/blob/06773e11dedf44d8084887b68ddad0758820cc89/GamesDoneQuickCalendarFactory/GamesDoneQuickCalendarFactory.csproj#L63) that properties file into your C# project file

You generally don't need to re-run this unless you either add dependencies to additional packages in your project or target a different minor runtime (TFM) version.