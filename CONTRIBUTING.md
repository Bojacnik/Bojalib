# Contributing

## Layout

```
Bojalib.slnx                 solution
Directory.Build.props        shared build settings, analyzers, NuGet metadata
Directory.Packages.props     all package versions (central package management)
global.json                  .NET SDK version + test runner (Microsoft.Testing.Platform)
src/Bojalib/                 the library — folders match namespaces
tests/Bojalib.Tests/         NUnit tests — mirrors src/Bojalib
benchmarks/Bojalib.Benchmarks/  BenchmarkDotNet
```

## Before opening a pull request

```sh
dotnet format Bojalib.slnx              # apply .editorconfig style
dotnet build Bojalib.slnx -c Release    # warnings are errors in Release
dotnet test --project tests/Bojalib.Tests --coverlet
```

CI runs the same steps and fails if formatting is off, if there is any warning, or if line or branch coverage
drops below 90%.

## Conventions

- Every public type and member has XML docs (enforced by the build).
- Types are `sealed` unless designed for inheritance.
- Test names follow `Method_Scenario_ExpectedResult`.
- Performance claims come with a benchmark.
- Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `chore:` …).

## Releasing

1. Update `CHANGELOG.md`.
2. Tag the commit: `git tag v2.1.0 && git push origin v2.1.0`.

The `Release` workflow builds, tests, publishes to nuget.org and creates a GitHub release. The version comes from
the tag ([MinVer](https://github.com/adamralph/minver)); untagged builds are `-alpha` prereleases.
