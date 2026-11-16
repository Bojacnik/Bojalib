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
| `Node<T>` (`Data`, `Children` list) | `TreeNode<T>` (`Value`, `AddChild`, `DepthFirst`, …) |
| `Stack<T>` | `LinkedStack<T>` |
| `CustomQueue<T>` (`IsEmpty()`) | `LinkedQueue<T>` (`IsEmpty`, `Count`, …) |
| `StackEmptyException`, `QueueEmptyException` | `InvalidOperationException`, or use `TryPop` / `TryDequeue` |
| `Quicksort.Sort(int[])` | `QuickSort.Sort<T>(Span<T>)` — arrays still work as-is |
| `Bubblesort.Sort(int[])` | `BubbleSort.Sort<T>(Span<T>)` |
| `Locate.DirectoriesRecursively(path)` → `List<string>` incl. root | `Locate.Directories(path)` → lazy, excludes root |
| `Locate.Files(dir)` → `List<string>` | `Locate.Files(dir, recursive)` → lazy |

Requires .NET 10.

### Added
- **Sorting**
  - `QuickSort` is now pattern-defeating quicksort with AVX-512 / AVX2 vectorized partitioning, inlined comparisons,
    SIMD detection of sorted/reversed input and counting sort for 8/16-bit integers — about 2× faster than
    `Span<T>.Sort()` on large random inputs. Also: keys + items overload, generic `TComparer` overload.
  - `MergeSort` (stable, adaptive), `RadixSort` (any integer type), `HeapSort`, `InsertionSort`.
  - `Selection.Select` / `Selection.PartialSort` (n-th element, top-k).
  - `SortedSpan.LowerBound` / `UpperBound` / `EqualRange` (branchless) and `IsSorted` (SIMD).
  - All sorts take `Span<T>` with `IComparable<T>`, `IComparer<T>` or `Comparison<T>`.
- **Collections**
  - `Trie<TValue>`: a radix tree implementing `IReadOnlyDictionary<string, TValue>`; span lookups; `ignoreCase`;
    `ContainsPrefix`, `EnumeratePrefix`, `TryMatchLongestPrefix`, `EnumerateMatches` (wildcards),
    `EnumerateFuzzy` (edit distance); allocation-free enumeration.
  - `BinaryHeap<T>`, `IndexedPriorityQueue<TKey, TPriority>`, `LruCache<TKey, TValue>`, `RingBuffer<T>`,
    `BloomFilter<T>`, `IntervalTree<TPoint, TValue>`, `DisjointSet` / `DisjointSet<T>`.
  - `TreeNode<T>`: `Parent`, `Root`, `Depth`, `Height`, `IsRoot`, `IsLeaf`, `AddChild`, `RemoveChild`, `Find`,
    `GetPath`, and allocation-free `DepthFirst`, `PostOrder`, `Leaves`, `Ancestors` plus pooled `BreadthFirst`.
    Adding a node that already has a parent or would form a cycle throws.
  - `LinkedStack<T>` / `LinkedQueue<T>`: `Count`, `IsEmpty`, `Peek`, `TryPeek`, `TryPop` / `TryDequeue`, `Clear`,
    allocation-free enumerators that detect modification.
  - Collection expressions for `LinkedStack`, `LinkedQueue`, `BinaryHeap` and `RingBuffer`.
- **Hashing:** `XxHash64` (identical output to `System.IO.Hashing.XxHash64`).
- **IO:** `Locate.Files(path, recursive: true)`.
- Trimming and Native AOT compatible.

### Changed
- `Locate` searches are lazy and throw `DirectoryNotFoundException` immediately for a missing directory.

### Fixed
- `Stack.Push` never stored anything.
- `Locate.DirectoriesRecursively` dropped nested directories, followed symbolic links (a link loop made it run
  forever), and threw on unreadable directories. Links are now listed but not followed, unreadable directories are
  skipped and hidden directories are included.
- `Quicksort` mis-sorted types whose `CompareTo` returns values other than -1/0/1.
- Sorting threw `NullReferenceException` on `null` elements; they now sort first.
- `TrieTree.FindWord` returned `default` for missing words and prefixes, indistinguishable from a stored default
  value such as the first enum member; empty-string keys were silently dropped; `with` copies shared state.
- `Node` could not be constructed, compared children by reference, and only enumerated direct children.
- `CustomQueue` kept the last dequeued item alive.
