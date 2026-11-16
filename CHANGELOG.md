# Changelog

## 2.0.0

Bojalib is now a single package. `Bojalib.Core.Collections`, `Bojalib.Core.Collections.Generic`,
`Bojalib.Core.IO` and `Bojalib.Core.Sorting` are deprecated — install `Bojalib` instead.
Trimmed / Native AOT publishes only keep the types you actually use.

### Breaking changes
- Targets `net10.0` (was `net9.0`).
- Namespaces: `Bojalib.Core.Collections.Generic` → `Bojalib.Collections`,
  `Bojalib.Core.Sorting.Sorting` → `Bojalib.Sorting`, `Bojalib.Core.IO` → `Bojalib.IO`.
- `Stack<T>` renamed to `LinkedStack<T>` (no longer clashes with `System.Collections.Generic.Stack<T>`).
- `Quicksort.Sort` / `Bubblesort.Sort` are generic: `Sort<T>(T[])`, plus an `IComparer<T>` overload.
- `TrieTree<T>` is a `sealed class` (was a `record`); `TrieNode<T>` is no longer public.
- `QueueEmptyException` / `StackEmptyException` now derive from `InvalidOperationException`.
- `Node<T>` enumeration now walks all descendants (was: only direct children).

### Fixes
- `LinkedStack.Push` now stores items (previously only pushed onto a non-empty stack, so nothing was ever stored).
- `Locate.DirectoriesRecursively` now returns nested directories.
- `Quicksort` works with `CompareTo` implementations that return values other than -1/0/1.
- `Node(TData, params Node[])` constructor is now callable.
- `Node.Equals(x, y)` compares children structurally (was: list reference, so never equal).
- `TrieTree`: prefixes of stored words and missing words are no longer confused with a stored `default`
  value (e.g. the first enum member) — use the new `TryFindWord` / `ContainsWord`. Empty-string keys work.
- `Quicksort` / `Bubblesort` handle `null` elements (sorted first) instead of throwing.
- `Quicksort` stack depth is O(log n) for any input.
- `Locate.DirectoriesRecursively` no longer follows symbolic links (a looping link made it run forever),
  skips unreadable directories instead of throwing, and includes hidden directories.
- `CustomQueue` no longer keeps the last dequeued item alive.

### Added
- Marked trimmable and Native AOT compatible.
- `TrieTree.TryFindWord`, `TrieTree.ContainsWord`.
- `Sort<T>(T[], IComparer<T>)` overloads; `Bubblesort` exits early once sorted.
