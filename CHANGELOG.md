# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [2.0.0]

A redesign. Bojalib is now a single package with a consistent, .NET-style API; nearly every public type changed.

### Migrating from 1.x / the `Bojalib.Core.*` packages

`Bojalib.Core.Collections`, `Bojalib.Core.Collections.Generic`, `Bojalib.Core.IO` and `Bojalib.Core.Sorting`
are deprecated — install `Bojalib` instead. Trimmed / Native AOT publishes only keep the types you use.

| 1.x | 2.0 |
|---|---|
| `Bojalib.Core.Collections.Generic` namespace | `Bojalib.Collections` |
| `Bojalib.Core.Sorting.Sorting` namespace | `Bojalib.Sorting` |
| `Bojalib.Core.IO` namespace | `Bojalib.IO` |
| `TrieTree<T>` (`InsertWord`, `FindWord`) | `Trie<TValue>` (indexer, `Add`, `TryGetValue`, …) |
| `Node<T>` | Removed. Use your own node type with the `Tree` traversal helpers |
| `Stack<T>`, `CustomQueue<T>` | Removed. Use `System.Collections.Generic.Stack<T>` / `Queue<T>`, or `Deque<T>` |
| `StackEmptyException`, `QueueEmptyException` | Removed with the types above |
| `Quicksort.Sort(int[])` | `QuickSort.Sort<T>(Span<T>)` — arrays still work as-is |
| `Bubblesort.Sort(int[])` | `BubbleSort.Sort<T>(Span<T>)` |
| `Locate` | Removed: `Directory.EnumerateDirectories` / `FileSystemEnumerable` cover it |

Requires .NET 10.

### Added
- **Sorting**
  - `QuickSort` is now pattern-defeating quicksort with AVX-512 / AVX2 vectorized partitioning, inlined comparisons,
    SIMD detection of sorted/reversed input and counting sort for 8/16-bit integers — about 2× faster than
    `Span<T>.Sort()` on large random inputs. Also: keys + items overload, generic `TComparer` overload.
  - `ParallelSort`: multithreaded sample sort over `T[]` / `Memory<T>`.
  - Portable 128-bit SIMD partitioning for ARM64 (NEON) and pre-AVX2 x86.
  - `QuickSort.Sort(keys, items)`: SIMD path (stable) for `int`/`uint`/`float` keys.
  - `MergeSort` (stable, adaptive), `RadixSort` (any integer type), `StringSort` (ordinal multikey quicksort),
    `HeapSort`, `InsertionSort`.
  - `Selection.Select` / `Selection.PartialSort` (n-th element, top-k).
  - `SortedSpan.LowerBound` / `UpperBound` / `EqualRange` (branchless) and `IsSorted` (SIMD).
  - All sorts take `Span<T>` with `IComparable<T>`, `IComparer<T>` or `Comparison<T>`.
- **Collections**
  - `RoaringBitmap`: compressed `uint` sets with SIMD set operations and the standard portable Roaring format
    (byte-identical to CRoaring; run containers are read).
  - `BTreeDictionary<TKey, TValue>` / `BTreeSet<T>`: order-statistic B+trees with `GetAt`, `IndexOfKey`,
    `CountLessThan`, `RemoveAt`, floor/ceiling and range enumeration.
  - `Trie<TValue>`: a radix tree implementing `IReadOnlyDictionary<string, TValue>`, with a hash index for
    `Dictionary`-speed exact lookups; span lookups; `ignoreCase`; `ContainsPrefix`, `EnumeratePrefix`,
    `TryMatchLongestPrefix`, `EnumerateMatches` (wildcards), `EnumerateFuzzy` (edit distance); allocation-free
    enumeration.
  - `IndexedPriorityQueue<TKey, TPriority>`, `Deque<T>`, `CircularBuffer<T>`, `IntervalTree<TPoint, TValue>`,
    `DisjointSet` / `DisjointSet<T>`, `LruCache<TKey, TValue>` (single-threaded).
  - `Tree`: `DepthFirst`, `PostOrder`, `BreadthFirst`, `Leaves`, `TryFind`, `TryFindPath`, `Count`, `Height` over any
    node type, given a children function. Pooled stack/queue, no recursion.
  - Collection expressions for `Deque`, `CircularBuffer` and `BTreeSet`.
- **Probabilistic** (`Bojalib.Probabilistic`): `BloomFilter<T>` (SIMD split-block), `BinaryFuseFilter<T>`,
  `HyperLogLog<T>`, `CountMinSketch<T>`, `TDigest` (streaming quantiles), `TopK<T>` (Space-Saving heavy hitters) —
  serializable where it makes sense, with stable hashing for strings and primitives.
- Trimming and Native AOT compatible.

### Fixed
- `Stack.Push` never stored anything (the type is removed in 2.0).
- `Locate.DirectoriesRecursively` dropped nested directories and looped forever on symbolic link cycles (the type
  is removed in 2.0).
- `Quicksort` mis-sorted types whose `CompareTo` returns values other than -1/0/1.
- Sorting threw `NullReferenceException` on `null` elements; they now sort first.
- `TrieTree.FindWord` returned `default` for missing words and prefixes, indistinguishable from a stored default
  value such as the first enum member; empty-string keys were silently dropped; `with` copies shared state.
- `Node` could not be constructed and only enumerated direct children (replaced by `Tree` in 2.0).
