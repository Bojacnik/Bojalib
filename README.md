# Bojalib

[![CI](https://github.com/Bojacnik/Bojalib/actions/workflows/ci.yml/badge.svg)](https://github.com/Bojacnik/Bojalib/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Bojalib.svg)](https://www.nuget.org/packages/Bojalib)

Allocation-conscious data structures, span-based sorting algorithms and safe recursive file-system search for .NET 10.
Trimming and Native AOT compatible; no dependencies.

```sh
dotnet add package Bojalib
```

## What's inside

| Namespace | Type | What it's for |
|---|---|---|
| `Bojalib.Collections` | `Trie<TValue>` | String-keyed map with fast prefix queries (autocomplete, tokenizers, routing) |
| | `TreeNode<T>` | General n-ary tree with safe parent/child management and traversals |
| | `LinkedStack<T>`, `LinkedQueue<T>` | O(1) stack/queue that never resize or copy |
| `Bojalib.Sorting` | `QuickSort` | Introsort: O(n log n) worst case, in place |
| | `HeapSort`, `InsertionSort`, `BubbleSort` | Classic algorithms with the same API |
| `Bojalib.IO` | `Locate` | Lazy recursive directory/file search that can't hang on symlink loops |

### Trie

```csharp
var keywords = new Trie<Token> { ["fn"] = Token.Fn, ["for"] = Token.For, ["struct"] = Token.Struct };

keywords.TryGetValue("for", out Token token);           // true
keywords.TryGetValue(source.AsSpan(start, length), out token); // look up a slice: no substring allocated
keywords.ContainsPrefix("st");                           // true
keywords.EnumeratePrefix("f");                           // ("fn", Fn), ("for", For) — ordinal order
```

`Trie` implements `IReadOnlyDictionary<string, TValue>`. Use it when you need prefix queries;
for exact-match lookups only, `Dictionary<string, TValue>` is faster (see [benchmarks](#benchmarks)).

### Sorting

All algorithms sort `Span<T>` in place, so arrays, slices and stack memory all work:

```csharp
int[] numbers = [5, 3, 9, 1];
QuickSort.Sort(numbers);                                    // IComparable<T>
QuickSort.Sort(numbers.AsSpan(1, 2));                       // just a slice
QuickSort.Sort(people, (a, b) => a.Age.CompareTo(b.Age));   // Comparison<T>
QuickSort.Sort(people, StringComparer.OrdinalIgnoreCase);   // IComparer<T>
QuickSort.Sort(numbers.AsSpan(), new MyStructComparer());   // struct comparer: fully inlined
```

`null` elements sort first. `InsertionSort` and `BubbleSort` are stable; `QuickSort` and `HeapSort` are not.

### TreeNode

```csharp
var root = new TreeNode<string>("root", new TreeNode<string>("a"), new TreeNode<string>("b"));
root.Children[0].AddChild("a1");

root.DepthFirst();   // root, a, a1, b
root.BreadthFirst(); // root, a, b, a1

var comparer = TreeNode<string>.CreateStructuralComparer();
comparer.Equals(treeA, treeB); // same values and shape?
```

Adding a node that already has a parent, or that would create a cycle, throws.

### Locate

```csharp
foreach (string dir in Locate.Directories("src")) { ... }          // all subdirectories, lazily
foreach (string file in Locate.Files("src", recursive: true)) { ... }
```

Symbolic links are listed but never followed, unreadable directories are skipped, hidden entries are included.

## Building

```sh
dotnet build Bojalib.slnx
dotnet test --project tests/Bojalib.Tests
dotnet run -c Release --project benchmarks/Bojalib.Benchmarks   # BenchmarkDotNet
```

Requires the .NET 10 SDK. See [CONTRIBUTING.md](CONTRIBUTING.md) for the repo layout and conventions.

## Benchmarks

Indicative results from a short BenchmarkDotNet run (.NET 10, x64, shared cloud VM — expect ±15% noise;
run them yourself for real numbers). Ratio is relative to the baseline in each group.

**Sorting random `int`s** — baseline: the runtime's `Span<T>.Sort()`

| Length | `QuickSort` | `HeapSort` |
|---:|---:|---:|
| 100 | 0.92× | 1.27× |
| 10,000 | 1.30× | 1.55× |
| 1,000,000 | 0.84× | 2.00× |

No allocations for any of them.

**`Trie<int>` with 10,000 random keys** — baseline: `Dictionary<string, int>` (ordinal)

| Operation | Ratio |
|---|---:|
| Prefix search (all keys starting with `"ab"`) | **0.05×** (≈20× faster) |
| Exact lookup of slices of a string | 2.0× |
| Exact lookup of whole keys | 8× |

Exact lookups chase one pointer per character, so a hash table wins there; prefix queries are what a trie is for.

## License

[MIT](LICENSE)
