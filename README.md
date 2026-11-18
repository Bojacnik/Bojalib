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
| | `StringSort` | Ordinal string sort (multikey quicksort) |
| | `MergeSort` | Stable, adaptive (TimSort-style) |
| | `HeapSort`, `InsertionSort`, `BubbleSort` | Classic algorithms, same API |
| | `Selection` | n-th element (median, percentiles) and partial sort (top-k) without a full sort |
| | `SortedSpan` | Branchless `LowerBound` / `UpperBound` / `EqualRange`, SIMD `IsSorted` |
| `Bojalib.Collections` | `BTreeDictionary<TKey, TValue>`, `BTreeSet<T>` | Sorted collections with O(log n) "i-th item" and rank queries |
| | `RoaringBitmap` | Compressed set of `uint` IDs with fast union/intersection; standard Roaring format |
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
| | `TopK<T>` | The most frequent items in a stream, with error bounds (Space-Saving) |
| | `TDigest` | Streaming quantiles (p50, p99, p99.9) in a few KB, mergeable |

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
StringSort.Sort(names);                                    // ordinal, faster than Array.Sort(StringComparer.Ordinal)

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

// Compressed ID sets: segments, permissions, search postings
RoaringBitmap activeUsers = [.. activeIds], paidUsers = [.. paidIds];
long activeAndPaid = (activeUsers & paidUsers).Count;
byte[] saved = activeUsers.ToBytes();                 // readable by Java/Go/C/Rust/Python Roaring libraries

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
var topUrls = new TopK<string>(capacity: 1000);                                           // which items are frequent
var latency = new TDigest();                                                              // p99 in ~2.5 KB
latency.Add(elapsedMs);
double p99 = latency.Quantile(0.99);

byte[] saved = known.ToBytes();   // stable hashing: reload in another process or machine
```

## Benchmarks

Full BenchmarkDotNet run (default job, v0.15.8) of `benchmarks/Bojalib.Benchmarks` on a shared 4-core cloud VM
(Intel Xeon 2.1 GHz, x64 with AVX-512, .NET 10). The standard deviation was around 10%, so treat the ratios as
the result rather than the absolute times. Run `dotnet run -c Release --project benchmarks/Bojalib.Benchmarks` for
your own numbers.

**Sorting random `int`s** (time, and ratio to `Span<T>.Sort()`)

| | 100 | 10K | 1M |
|---|---:|---:|---:|
| `Span<T>.Sort()` (.NET) | 0.71 µs | 643 µs | 97.6 ms |
| `QuickSort` | 0.62 µs (0.89) | 322 µs (0.50) | 39.9 ms (0.41) |
| `RadixSort` | 0.59 µs (0.85) | 157 µs (0.25) | 19.8 ms (0.20) |
| `ParallelSort` (4 cores) | 0.62 µs (0.89) | 309 µs (0.48) | 32.8 ms (0.34) |
| `MergeSort` (stable) | 2.0 µs (2.87) | 980 µs (1.53) | 150 ms (1.55) |
| `HeapSort` | 0.94 µs (1.34) | 938 µs (1.47) | 161 ms (1.65) |

None of them allocate, except `ParallelSort` at 1M (13 KB of task bookkeeping). Below 64K elements `ParallelSort`
is just `QuickSort`. On this shared VM it gained only 1.2× over `QuickSort` at 1M; its scaling on real many-core
hardware has not been measured yet.

**`BTreeDictionary<int, int>` vs `SortedDictionary<int, int>`, 100K random keys**

| | `SortedDictionary` | `BTreeDictionary` | Ratio |
|---|---:|---:|---:|
| Insert all | 53.6 ms | 15.3 ms | 3.5× faster |
| Look up all | 26.9 ms | 9.4 ms | 2.9× faster |
| Iterate all | 2.41 ms | 0.24 ms | 9.9× faster |
| Memory allocated building it | 4.8 MB | 1.5 MB | 3.3× less |
| Rank of a key | O(n) | O(log n) | |

**`Trie<int>` vs `Dictionary<string, int>`, 10,000 random keys.** Exact lookups are at parity: 176 µs vs 175 µs for
10K lookups, through an internal hash index. A prefix search takes 0.14 µs vs 21.6 µs for scanning the dictionary.

### Ad-hoc measurements

These come from earlier hand-written timing loops on the same VM, best of several runs, ±15%. They are not part of
the BenchmarkDotNet suite.

- **10M random `int`s:** `Span<T>.Sort()` 885 ms, `QuickSort` 323 ms, `ParallelSort` 161 ms, `RadixSort` 313 ms.
  At this size the radix scatter passes are memory-bound.
- **`QuickSort` by instruction set, 1M `int`s:**

  | Instruction set | Time |
  |---|---:|
  | AVX-512 | 29 ms |
  | AVX2 | 31 ms |
  | 128-bit (the NEON/SSE path) | 34 ms |
  | Scalar | 64 ms |
- **`QuickSort.Sort(keys, items)`, 1M `int` keys:** 42 ms, vs 91 ms for `Array.Sort(keys, items)`.
- **`StringSort` vs `Array.Sort(strings, StringComparer.Ordinal)`, 500K strings:** 1.2× faster on random words, 1.5×
  on URLs and file paths, which have long shared prefixes.
- **`BTreeDictionary` vs `SortedDictionary`, 1M keys:**

  | | `SortedDictionary` | `BTreeDictionary` |
  |---|---:|---:|
  | Insert all | 1,135 ms | 231 ms |
  | Look up all | 738 ms | 183 ms |
- **`RoaringBitmap` vs `HashSet<uint>`, two sets of 2M IDs out of 0–20M:**

  | | `HashSet<uint>` | `RoaringBitmap` |
  |---|---:|---:|
  | Memory | ≈29 MB | 2.4 MB |
  | Intersection | 178 ms | 8.6 ms |
  | Union | 250 ms | 3.2 ms |

  A single `Contains` call is 1.6× slower than a hash set's.

## Building

```sh
dotnet build Bojalib.slnx
dotnet test --project tests/Bojalib.Tests
dotnet run -c Release --project benchmarks/Bojalib.Benchmarks
```

Requires the .NET 10 SDK. See [CONTRIBUTING.md](CONTRIBUTING.md) for the repo layout and conventions.

## License

[MIT](LICENSE)
