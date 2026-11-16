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
- `Quicksort.Sort` / `Bubblesort.Sort` are generic: `Sort<T>(T[]) where T : IComparable<T>`.

### Fixes
- `LinkedStack.Push` now stores items (previously only pushed onto a non-empty stack, so nothing was ever stored).
- `Locate.DirectoriesRecursively` now returns nested directories.
- `Quicksort` works with `CompareTo` implementations that return values other than -1/0/1.
- `Node(TData, params Node[])` constructor is now callable.

### Added
- Marked trimmable and Native AOT compatible.
