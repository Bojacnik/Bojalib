# Bojalib — notes for Claude

High-performance .NET 10 library: SIMD/parallel sorting, B-trees, tries, Roaring bitmaps, probabilistic structures.
See `README.md` for the API, `CONTRIBUTING.md` for layout and conventions, `CHANGELOG.md` for 2.0.0 contents.

## Status

- 2.0.0 is in progress on branch `ccr-7d27f5b9-tgjrfe`, draft PR #1. Not released, not tagged.
- No public API is frozen yet: breaking changes are still free until 2.0.0 ships.

## Next session: heavy testing (agreed with the owner)

The owner asked that the next session focus on **heavy testing** before anything else. Plan, roughly in priority order:

1. **Mutation testing** with Stryker.NET on `src/Bojalib`: find code whose behaviour the tests don't actually pin down.
   Fix surviving mutants with targeted tests; aim for a high mutation score on sorting, B-tree, Trie and Roaring.
2. **Long-running property tests**: re-run the CsCheck model tests with far more iterations and larger inputs
   (e.g. `CsCheck_Iter`/`CsCheck_Time` env vars or a separate `[Category("Long")]` suite), including the
   `Validate()` invariant checks for `BTreeDictionary`.
3. **Fuzzing of every deserializer** (`RoaringBitmap.FromBytes`, `BloomFilter`/`BinaryFuseFilter`/`HyperLogLog`/
   `CountMinSketch`/`TDigest` `FromBytes`): random and mutated inputs must either round-trip or throw
   `ArgumentException`, never crash, hang or allocate unboundedly (e.g. SharpFuzz, or a CsCheck byte-mutation loop).
4. **Differential testing against reference implementations**:
   - Roaring set operations and serialization vs CRoaring (pyroaring) on large random sets, not just fixtures.
   - `TDigest` quantiles vs a reference t-digest; `HyperLogLog` vs a reference HLL.
   - Every sort vs `Array.Sort` on adversarial inputs (pdqsort/quicksort killers, all-equal, NaN/-0.0 mixes).
5. **Allocation tests**: assert zero allocations where the docs promise them (struct enumerators, Trie/B-tree
   enumeration, `QuickSort`, `LruCache` steady state) using `GC.GetAllocatedBytesForCurrentThread()`.
6. **Concurrency/stress**: `ParallelSort` with many thread counts and huge inputs; `Parallel` misuse of
   non-thread-safe types should be documented, not silently corrupting.
7. **Trimming / Native AOT**: publish a small sample app with `PublishAot=true` and require zero warnings.
8. **Real hardware**: run the test suite and benchmarks on ARM64 (Apple Silicon or Graviton) and on a 16+ core
   machine — the NEON path and `ParallelSort` scaling are currently only verified indirectly.
9. **Full BenchmarkDotNet runs** (not `--job short`) to replace the indicative numbers in the README.

## How to verify (same as CI)

```sh
dotnet format Bojalib.slnx --verify-no-changes
dotnet build Bojalib.slnx -c Release
dotnet test --project tests/Bojalib.Tests -c Release --coverlet --coverlet-threshold 90 --coverlet-threshold-type line,branch --coverlet-threshold-stat Total
DOTNET_EnableAVX512F=0 dotnet test --project tests/Bojalib.Tests -c Release
DOTNET_EnableAVX2=0 dotnet test --project tests/Bojalib.Tests -c Release
DOTNET_EnableHWIntrinsic=0 dotnet test --project tests/Bojalib.Tests -c Release
```

## Conventions learned so far

- Measure before claiming performance; put real numbers (and honest regressions) in the docs.
- Every data structure gets a model-based CsCheck test against a simple reference implementation.
- Keep branch coverage comfortably above 90% *without* AVX-512, since CI runners may lack it.
