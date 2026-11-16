# Bojalib

Personal .NET library of data structures, sorting algorithms and small utilities.

## Layout

```
Bojalib.slnx                 # solution (XML .slnx format, .NET SDK 9.0.200+)
Directory.Build.props        # shared build settings + NuGet metadata
Directory.Packages.props     # central package versions
global.json                  # pinned SDK (rolls forward to newer majors)
src/
  Bojalib/                   # the library (PackageId: Bojalib)
    Collections/             # LinkedStack, CustomQueue, TrieTree, Node
    IO/                      # Locate helpers
    Sorting/                 # Quicksort, Bubblesort (generic over IComparable<T>)
tests/
  Bojalib.Tests/             # NUnit tests, folders mirror src/Bojalib
```

## Build & test

```sh
dotnet build Bojalib.slnx
dotnet test Bojalib.slnx
```

## Publishing

Copy `.env-template` to `.env` and set `NUGET_API_KEY`, then:

```sh
dotnet pack Bojalib.slnx -c Release -o artifacts
dotnet nuget push "artifacts/*.nupkg" --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json
```

## License

[MIT](LICENSE)
