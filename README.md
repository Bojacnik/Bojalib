# Bojalib

[![CI](https://github.com/Bojacnik/Bojalib/actions/workflows/ci.yml/badge.svg)](https://github.com/Bojacnik/Bojalib/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Bojalib.svg)](https://www.nuget.org/packages/Bojalib)

Fast algorithms and specialized data structures that .NET doesn't include: SIMD and parallel sorting,
order-statistic B-trees, radix tries with fuzzy search, decrease-key priority queues, deques, probabilistic filters
and sketches, interval trees and more. Allocation-free where it
matters, no dependencies, trimming and Native AOT compatible.

```sh
dotnet add package Bojalib
```

## What's inside

| Namespace | Type | What it's for |
|---|---|---|
| `Bojalib.Sorting` | `QuickSort` | Fastest single-threaded sort: pdqsort with AVX-512 / AVX2 / NEON partitioning |
| | `ParallelSort` | Multithreaded sample sort for large arrays |
| | `RadixSort` | Integers of any width, O(n) |
| | `MergeSort` | Stable, adaptive (TimSort-style) |
| | `HeapSort`, `InsertionSort`, `BubbleSort` | Classic algorithms, same API |
| | `Selection` | n-th element (median, percentiles) and partial sort (top-k) without a full sort |
| | `SortedSpan` | Branchless `LowerBound` / `UpperBound` / `EqualRange`, SIMD `IsSorted` |
| `Bojalib.Collections` | `BTreeDictionary<TKey, TValue>`, `BTreeSet<T>` | Sorted collections with O(log n) "i-th item" and rank queries |
| | `Trie<TValue>` | Prefix queries, longest-prefix match, wildcard and fuzzy (edit-distance) search |
| | `IndexedPriorityQueue<TKey, TPriority>` | Priority queue with O(log n) `UpdatePriority` / `Remove` (Dijkstra, A*) |
| | `Deque<T>` | Double-ended queue with O(1) indexing (.NET has none) |
| | `CircularBuffer<T>` | Fixed-capacity sliding window that overwrites the oldest item |
| | `IntervalTree<TPoint, TValue>` | Which intervals contain a point / overlap a range |
| | `DisjointSet`, `DisjointSet<T>` | Union-find |
| | `LruCache<TKey, TValue>` | Single-threaded, allocation-free LRU cache for hot paths |
| | `Tree` | Depth-first, breadth-first, post-order and leaf traversals of *your own* node types |
| `Bojalib.Probabilistic` | `BloomFilter<T>` | Set membership for a growing set (SIMD split-block) |
| | `BinaryFuseFilter<T>` | Set membership for a fixed set: ~30% smaller than Bloom |
| | `HyperLogLog<T>` | Count distinct items in a few KB |
| | `CountMinSketch<T>` | Approximate frequency of each item in a stream |

## Sorting

Every algorithm sorts a `Span<T>` in place, so arrays, slices and stack memory all work, and takes an optional
`IComparer<T>` or `Comparison<T>`:

```csharp
int[] numbers = [5, 3, 9, 1];
QuickSort.Sort(numbers);                                   // pdqsort, SIMD partitioning
QuickSort.Sort(numbers.AsSpan(1, 2));                      // just a slice
QuickSort.Sort(people, (a, b) => a.Age.CompareTo(b.Age));  // Comparison<T>
QuickSort.Sort(keys.AsSpan(), values.AsSpan());            // sort values by keys (SIMD for int/uint/float keys)
ParallelSort.Sort(hugeArray);                              // all cores
MergeSort.Sort(people, byLastName);                        // stable
RadixSort.Sort<long>(timestamps);                          // O(n) for integers

Selection.Select(scores, scores.Length / 2);               // median in O(n)
Selection.PartialSort(scores, 10);                         // top 10, sorted
int index = SortedSpan.LowerBound(sorted, 42);
```

How `QuickSort` gets its speed:
- **pdqsort.** Pattern-defeating quicksort handles already-sorted runs and many duplicates in O(n), and falls back
  to heapsort so the worst case is O(n log n).
- **Vectorized partitioning for 32/64-bit integers and floats.** AVX-512 uses `vpcompress`; AVX2 uses `vpermd`;
  ARM64 (NEON) and older x86 use a portable 128-bit byte shuffle.
- **Comparisons are inlined.** Built-in numbers compare with a single instruction; comparer structs are inlined too.
- **Fast paths:** SIMD detection of sorted/reversed input; counting sort for 8- and 16-bit integers.

`ParallelSort` is a sample sort: sampled splitters, parallel classification and lock-free scatter, then parallel
pdqsort per bucket. `null`s and NaNs sort first, matching `Array.Sort`.

## Collections

```csharp
// Sorted collections that know positions: leaderboards, percentiles, ordered indexes
var scores = new BTreeSet<int>();
int rank = scores.CountLessThan(myScore);        // O(log n)
int median = scores[scores.Count / 2];           // O(log n)
foreach (int s in scores.EnumerateRange(100, 200)) { … }

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

// Calendars, genomic ranges, IP ranges, …
var bookings = new IntervalTree<DateTime, string>(intervals);
foreach (var booking in bookings.Query(start, end)) { … }

// Walk any tree you already have: no wrapper types, no recursion
foreach (Control c in Tree.DepthFirst(form, c => c.Controls.Cast<Control>())) { … }
Tree.TryFindPath(syntaxRoot, n => n.ChildNodes(), n => n is MethodDeclarationSyntax, out var path);

// Single-threaded hot-path cache (for shared caches use a concurrent library such as BitFaster.Caching)
var cache = new LruCache<string, Token>(capacity: 1000, StringComparer.Ordinal);
```

## Probabilistic

```csharp
var seen = new BloomFilter<string>(expectedItems: 1_000_000, falsePositiveRate: 0.01);   // growing set
var known = new BinaryFuseFilter<string>(allUrls);                                       // fixed set, ~9 bits/item
var visitors = new HyperLogLog<string>();                                                // distinct count, 16 KB
var hits = new CountMinSketch<string>(epsilon: 0.001, delta: 0.01, conservativeUpdate: true);

byte[] saved = known.ToBytes();   // stable hashing: reload in another process or machine
```

## Benchmarks

Indicative results on a shared 4-core cloud VM (.NET 10, x64 with AVX-512), best of several runs — expect ±15%
noise; run `dotnet run -c Release --project benchmarks/Bojalib.Benchmarks` for your own numbers.

**Sorting random `int`s**

| | 1M | 10M |
|---|---:|---:|
| `Span<T>.Sort()` (.NET) | 74 ms | 885 ms |
| `QuickSort` | 30 ms | 323 ms |
| `ParallelSort` (4 cores) | 18 ms | 161 ms |
| `RadixSort` | 12 ms | 313 ms |

`RadixSort` wins while the data fits in cache; at 10M its scatter passes become memory-bound and it is no faster
than `QuickSort`.

`QuickSort` by instruction set (1M ints): AVX-512 29 ms, AVX2 31 ms, 128-bit (NEON/SSE path) 34 ms, scalar 64 ms.
`QuickSort.Sort(keys, items)` with 1M `int` keys: 42 ms vs 91 ms for `Array.Sort(keys, items)`.

**`BTreeDictionary<int, int>` vs `SortedDictionary<int, int>`, 1M random keys**

| | `SortedDictionary` | `BTreeDictionary` |
|---|---:|---:|
| Insert all | 1,135 ms | 231 ms |
| Look up all | 738 ms | 183 ms |
| Iterate all | 48.8 ms | 3.4 ms |
| Rank of a key | O(n) | O(log n) |

**`Trie<int>`, 10,000 random keys, vs `Dictionary<string, int>`**: exact lookups at parity (305 µs vs 290 µs for
10K lookups, via an internal hash index); prefix search 0.6 µs vs 191 µs for scanning the dictionary.

## Building

```sh
dotnet build Bojalib.slnx
dotnet test --project tests/Bojalib.Tests
dotnet run -c Release --project benchmarks/Bojalib.Benchmarks
```

Requires the .NET 10 SDK. See [CONTRIBUTING.md](CONTRIBUTING.md) for the repo layout and conventions.

## License

[MIT](LICENSE)
