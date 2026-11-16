# Bojalib

[![CI](https://github.com/Bojacnik/Bojalib/actions/workflows/ci.yml/badge.svg)](https://github.com/Bojacnik/Bojalib/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Bojalib.svg)](https://www.nuget.org/packages/Bojalib)

Fast algorithms and specialized data structures that .NET doesn't include: SIMD sorting, radix tries with fuzzy
search, decrease-key priority queues, deques, Bloom filters, interval trees and more. Allocation-free where it
matters, no dependencies, trimming and Native AOT compatible.

```sh
dotnet add package Bojalib
```

## What's inside

| Namespace | Type | What it's for |
|---|---|---|
| `Bojalib.Sorting` | `QuickSort` | Fastest general sort: pdqsort with AVX-512/AVX2 partitioning |
| | `RadixSort` | Integers of any width, O(n) |
| | `MergeSort` | Stable, adaptive (TimSort-style) |
| | `HeapSort`, `InsertionSort`, `BubbleSort` | Classic algorithms, same API |
| | `Selection` | n-th element (median, percentiles) and partial sort (top-k) without a full sort |
| | `SortedSpan` | Branchless `LowerBound` / `UpperBound` / `EqualRange`, SIMD `IsSorted` |
| `Bojalib.Collections` | `Trie<TValue>` | Prefix queries, longest-prefix match, wildcard and fuzzy (edit-distance) search |
| | `IndexedPriorityQueue<TKey, TPriority>` | Priority queue with O(log n) `UpdatePriority` / `Remove` (Dijkstra, A*) |
| | `Deque<T>` | Double-ended queue with O(1) indexing (.NET has none) |
| | `CircularBuffer<T>` | Fixed-capacity sliding window that overwrites the oldest item |
| | `BloomFilter<T>` | SIMD split-block Bloom filter, serializable |
| | `IntervalTree<TPoint, TValue>` | Which intervals contain a point / overlap a range |
| | `DisjointSet`, `DisjointSet<T>` | Union-find |
| | `LruCache<TKey, TValue>` | Single-threaded, allocation-free LRU cache for hot paths |
| | `Tree` | Depth-first, breadth-first, post-order and leaf traversals of *your own* node types |

## Sorting

Every algorithm sorts a `Span<T>` in place, so arrays, slices and stack memory all work, and takes an optional
`IComparer<T>` or `Comparison<T>`:

```csharp
int[] numbers = [5, 3, 9, 1];
QuickSort.Sort(numbers);                                   // pdqsort, SIMD partitioning
QuickSort.Sort(numbers.AsSpan(1, 2));                      // just a slice
QuickSort.Sort(people, (a, b) => a.Age.CompareTo(b.Age));  // Comparison<T>
QuickSort.Sort(keys.AsSpan(), values.AsSpan());            // sort values by keys
MergeSort.Sort(people, byLastName);                        // stable
RadixSort.Sort<long>(timestamps);                          // O(n) for integers

Selection.Select(scores, scores.Length / 2);               // median in O(n)
Selection.PartialSort(scores, 10);                         // top 10, sorted
int index = SortedSpan.LowerBound(sorted, 42);
```

How `QuickSort` gets its speed:
- **pdqsort.** Pattern-defeating quicksort handles already-sorted runs and many duplicates in O(n), and falls back
  to heapsort so the worst case is O(n log n).
- **Vectorized partitioning for 32/64-bit integers and floats.** AVX-512 uses `vpcompress`; AVX2 uses `vpermd`
  with a shuffle table. Other hardware gets the scalar path.
- **Comparisons are inlined.** Built-in numbers compare with a single instruction; comparer structs are inlined too.
- **Fast paths:** a SIMD check for already sorted or reversed input, and counting sort for 8- and 16-bit integers.

`null`s and NaNs sort first, matching `Array.Sort`.

## Collections

```csharp
// Trie: prefix queries, tokenizing, spelling suggestions
var words = new Trie<int>(ignoreCase: true) { ["kitten"] = 1, ["mitten"] = 2, ["sitting"] = 3 };
words.EnumeratePrefix("kit");                    // ("kitten", 1)
words.EnumerateFuzzy("kiten", maxDistance: 1);   // kitten (distance 1)
words.EnumerateMatches("?itten");                // kitten, mitten
keywords.TryMatchLongestPrefix(source.AsSpan(position), out int length, out Token token);

// Dijkstra with decrease-key
var frontier = new IndexedPriorityQueue<Node, int>();
frontier.EnqueueOrUpdate(neighbor, newDistance);

// Deques and sliding windows
var work = new Deque<Job>();
work.PushFront(urgent);
var lastSamples = new CircularBuffer<double>(100);   // keeps the 100 most recent
double average = lastSamples.Average();

// Probabilistic membership, savable to disk
var seen = new BloomFilter<string>(expectedItems: 1_000_000, falsePositiveRate: 0.01);
byte[] saved = seen.ToBytes();

// Calendars, genomic ranges, IP ranges, …
var bookings = new IntervalTree<DateTime, string>(intervals);
foreach (var booking in bookings.Query(start, end)) { … }

// Walk any tree you already have: no wrapper types, no recursion, no allocations after warm-up
foreach (Control c in Tree.DepthFirst(form, c => c.Controls.Cast<Control>())) { … }
Tree.TryFindPath(syntaxRoot, n => n.ChildNodes(), n => n is MethodDeclarationSyntax, out var path);

// Single-threaded hot-path cache (for shared caches use a concurrent library such as BitFaster.Caching)
var cache = new LruCache<string, Token>(capacity: 1000, StringComparer.Ordinal);
cache.TryGetValue(source.AsSpan(start, length), out Token token);  // span lookup, no string allocated
```

## Benchmarks

Indicative results from a short BenchmarkDotNet run (.NET 10, x64 with AVX-512, shared cloud VM — expect ±15% noise;
run `dotnet run -c Release --project benchmarks/Bojalib.Benchmarks` for your own numbers).

**Sorting random `int`s** — time relative to the runtime's `Span<T>.Sort()` (lower is better)

| Length | `QuickSort` | `RadixSort` | `MergeSort` (stable) | `HeapSort` |
|---:|---:|---:|---:|---:|
| 100 | 0.71× | 0.68× | 2.54× | 1.08× |
| 10,000 | 0.50× | 0.24× | 1.34× | 1.44× |
| 1,000,000 | **0.43×** | **0.18×** | 1.42× | 1.78× |

None of them allocate.

**`Trie<int>` with 10,000 random keys** — relative to `Dictionary<string, int>` (ordinal)

| Operation | Ratio |
|---|---:|
| Prefix search (all keys starting with `"ab"`) | **0.007×** (≈150× faster) |
| Exact lookup of slices of a string | 2.4× |
| Exact lookup of whole keys | 8× |

A trie's strength is prefix queries; for exact-match lookups only, `Dictionary` is faster.

## Building

```sh
dotnet build Bojalib.slnx
dotnet test --project tests/Bojalib.Tests
dotnet run -c Release --project benchmarks/Bojalib.Benchmarks
```

Requires the .NET 10 SDK. See [CONTRIBUTING.md](CONTRIBUTING.md) for the repo layout and conventions.

## License

[MIT](LICENSE)
