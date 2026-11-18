using Bojalib.Collections;
using Bojalib.Probabilistic;
using Bojalib.Sorting;

namespace Bojalib.Tests.Robustness;

/// <summary>
/// Checks the "allocation-free" promises in the docs: after a warm-up call (which may rent pool arrays or build
/// lookup tables once), repeating the operation must allocate nothing on the calling thread.
/// </summary>
[NonParallelizable]
public class AllocationTests
{
    private static long AllocatedBy(Action action)
    {
        action(); // warm-up: JIT, pools, static tables
        action();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++)
        {
            action();
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void ShouldNotAllocate(string what, Action action) =>
        AllocatedBy(action).Should().Be(0, "{0} is documented as allocation-free", what);

    [Test]
    public void Sorting()
    {
        int[] source = [.. Enumerable.Range(0, 5000).Select(static i => (i * 7919) % 5003)];
        int[] work = new int[source.Length];
        double[] doubles = [.. source.Select(static i => i / 3.0)];
        double[] doubleWork = new double[doubles.Length];
        int[] keys = new int[source.Length];
        int[] items = new int[source.Length];

        ShouldNotAllocate("QuickSort", () => { source.CopyTo(work, 0); QuickSort.Sort<int>(work); });
        ShouldNotAllocate("QuickSort (double)", () => { doubles.CopyTo(doubleWork, 0); QuickSort.Sort<double>(doubleWork); });
        ShouldNotAllocate("HeapSort", () => { source.CopyTo(work, 0); HeapSort.Sort<int>(work); });
        ShouldNotAllocate("InsertionSort", () => { source.AsSpan(0, 200).CopyTo(work); InsertionSort.Sort(work.AsSpan(0, 200)); });
        ShouldNotAllocate("MergeSort", () => { source.CopyTo(work, 0); MergeSort.Sort<int>(work); });
        ShouldNotAllocate("RadixSort", () => { source.CopyTo(work, 0); RadixSort.Sort<int>(work); });
        ShouldNotAllocate("Selection.Select", () => { source.CopyTo(work, 0); Selection.Select<int>(work, 2500); });
        ShouldNotAllocate("QuickSort keys/items", () => { source.CopyTo(keys, 0); source.CopyTo(items, 0); QuickSort.Sort(keys.AsSpan(), items.AsSpan()); });
        ShouldNotAllocate("SortedSpan", () => { _ = SortedSpan.LowerBound<int>(work, 42) + SortedSpan.UpperBound<int>(work, 42); _ = SortedSpan.IsSorted<int>(work); });
    }

    [Test]
    public void TrieAndBTreeEnumeration()
    {
        var trie = new Trie<int>();
        var tree = new BTreeDictionary<int, int>();
        var set = new BTreeSet<string>();
        for (int i = 0; i < 2000; i++)
        {
            trie[$"key{i}"] = i;
            tree[i] = i;
            set.Add($"item{i}");
        }

        ShouldNotAllocate("Trie enumeration", () => { foreach (KeyValuePair<string, int> _ in trie) { } });
        ShouldNotAllocate("Trie.EnumeratePrefix", () => { foreach (KeyValuePair<string, int> _ in trie.EnumeratePrefix("key1")) { } });
        ShouldNotAllocate("Trie lookups", () => { _ = trie.TryGetValue("key1234", out _) && trie.ContainsKey("key5".AsSpan()); });
        ShouldNotAllocate("Trie.TryMatchLongestPrefix", () => trie.TryMatchLongestPrefix("key12345 and more", out _, out _));
        ShouldNotAllocate("BTreeDictionary enumeration", () => { foreach (KeyValuePair<int, int> _ in tree) { } });
        ShouldNotAllocate("BTreeDictionary.EnumerateRange", () => { foreach (KeyValuePair<int, int> _ in tree.EnumerateRange(100, 900)) { } });
        ShouldNotAllocate("BTreeDictionary positional queries", () => { _ = tree.GetAt(1000).Key + tree.CountLessThan(1500) + tree.IndexOfKey(7); });
        ShouldNotAllocate("BTreeSet enumeration", () => { foreach (string _ in set) { } });
    }

    [Test]
    public void SteadyStateCollections()
    {
        var cache = new LruCache<int, int>(100);
        var deque = new Deque<int>(64);
        var window = new CircularBuffer<int>(64);
        var queue = new IndexedPriorityQueue<int, int>();
        var intervals = new IntervalTree<int, int>(Enumerable.Range(0, 1000).Select(static i => new Interval<int, int>(i, i + 10, i)));
        var bitmap = new RoaringBitmap(Enumerable.Range(0, 20_000).Select(static i => (uint)(i * 3)));
        for (int i = 0; i < 100; i++)
        {
            cache[i] = i;
            queue.Enqueue(i, i);
        }

        int next = 100;
        ShouldNotAllocate("LruCache (full, evicting)", () =>
        {
            for (int i = 0; i < 200; i++)
            {
                cache[next++] = i;
                _ = cache.TryGetValue(next - 50, out _);
            }
        });
        ShouldNotAllocate("Deque within capacity", () =>
        {
            for (int i = 0; i < 32; i++) { deque.PushBack(i); deque.PushFront(i); }
            for (int i = 0; i < 64; i++) { _ = deque.PopFront(); }
            foreach (int _ in deque) { }
        });
        ShouldNotAllocate("CircularBuffer", () =>
        {
            for (int i = 0; i < 200; i++) { window.PushBack(i); }
            foreach (int _ in window) { }
        });
        ShouldNotAllocate("IndexedPriorityQueue updates", () =>
        {
            for (int i = 0; i < 100; i++) { queue.UpdatePriority(i, (i * 37) % 101); }
        });
        ShouldNotAllocate("IntervalTree.Query", () => { foreach (Interval<int, int> _ in intervals.Query(400, 420)) { } });
        ShouldNotAllocate("RoaringBitmap enumeration and lookups", () =>
        {
            foreach (uint _ in bitmap) { }
            _ = bitmap.Contains(300) && bitmap.Rank(5000) > 0;
        });
    }

    [Test]
    public void TreeTraversalsAfterWarmUp()
    {
        var children = new Dictionary<int, List<int>>();
        for (int i = 0; i < 1000; i++)
        {
            children[i] = i < 499 ? [(2 * i) + 1, (2 * i) + 2] : [];
        }

        ShouldNotAllocate("Tree.DepthFirst (pooled stack)", () => { foreach (int _ in Tree.DepthFirst(0, n => children[n])) { } });
        ShouldNotAllocate("Tree.BreadthFirst (pooled queue)", () => { foreach (int _ in Tree.BreadthFirst(0, n => children[n])) { } });
        ShouldNotAllocate("Tree.PostOrder", () => { foreach (int _ in Tree.PostOrder(0, n => children[n])) { } });
    }

    [Test]
    public void ProbabilisticAdds()
    {
        var bloom = new BloomFilter<int>(10_000, 0.01);
        var fuse = new BinaryFuseFilter<int>(Enumerable.Range(0, 1000));
        var hll = new HyperLogLog<long>();
        var cms = new CountMinSketch<int>(0.001, 0.01);
        var digest = new TDigest();
        var top = new TopK<int>(100);
        for (int i = 0; i < 200; i++)
        {
            top.Add(i % 150);
        }

        int i2 = 0;
        ShouldNotAllocate("BloomFilter", () => { bloom.Add(i2++); _ = bloom.MightContain(i2); });
        ShouldNotAllocate("BinaryFuseFilter.MightContain", () => { _ = fuse.MightContain(i2++); });
        ShouldNotAllocate("HyperLogLog", () => { hll.Add(i2++); });
        ShouldNotAllocate("CountMinSketch", () => { cms.Add(i2++); _ = cms.Estimate(i2); });
        ShouldNotAllocate("TDigest.Add (including periodic merges)", () => { for (int k = 0; k < 2000; k++) { digest.Add(k % 913); } });
        ShouldNotAllocate("TopK.Add for tracked items", () => { top.Add(i2++ % 100); });
    }
}
