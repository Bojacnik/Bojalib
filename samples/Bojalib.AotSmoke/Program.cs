// Exercises every public Bojalib type so a Native AOT publish compiles (and trims) all of it, then checks results.
// Exit code 0 = all checks passed.
using Bojalib.Collections;
using Bojalib.Probabilistic;
using Bojalib.Sorting;

int failures = 0;

void Check(bool condition, string what)
{
    if (!condition)
    {
        Console.Error.WriteLine("FAILED: " + what);
        failures++;
    }
}

// Sorting
var random = new Random(1);
int[] ints = [.. Enumerable.Range(0, 100_000).Select(_ => random.Next())];
int[] expected = [.. ints.Order()];
int[] work = [.. ints];
QuickSort.Sort<int>(work);
Check(work.AsSpan().SequenceEqual(expected), "QuickSort");
work = [.. ints];
ParallelSort.Sort(work);
Check(work.AsSpan().SequenceEqual(expected), "ParallelSort");
work = [.. ints];
RadixSort.Sort<int>(work);
Check(work.AsSpan().SequenceEqual(expected), "RadixSort");
work = [.. ints];
MergeSort.Sort<int>(work, static (a, b) => a.CompareTo(b));
Check(work.AsSpan().SequenceEqual(expected), "MergeSort");
work = [.. ints];
HeapSort.Sort<int>(work);
Check(work.AsSpan().SequenceEqual(expected), "HeapSort");
double[] doubles = [3, double.NaN, -0.0, 1];
QuickSort.Sort<double>(doubles);
Check(double.IsNaN(doubles[0]) && doubles[^1] == 3, "QuickSort doubles");
string?[] words = ["pear", null, "apple"];
StringSort.Sort(words);
Check(words[0] is null && words[1] == "apple", "StringSort");
int[] keys = [3, 1, 2];
string[] values = ["c", "a", "b"];
QuickSort.Sort(keys.AsSpan(), values.AsSpan());
Check(values[0] == "a", "QuickSort keys/items");
work = [.. ints];
Selection.Select<int>(work, 500);
Check(work[500] == expected[500], "Selection");
Check(SortedSpan.LowerBound<int>(expected, expected[10]) <= 10 && SortedSpan.IsSorted<int>(expected), "SortedSpan");
InsertionSort.Sort<int>(work.AsSpan(0, 50));
BubbleSort.Sort<int>(work.AsSpan(50, 50));
Check(SortedSpan.IsSorted<int>(work.AsSpan(0, 50)), "InsertionSort");

// Collections
var trie = new Trie<int>(ignoreCase: true) { ["hello"] = 1, ["help"] = 2 };
Check(trie.EnumeratePrefix("HEL").Count() == 2 && trie.EnumerateFuzzy("helo", 1).Any() && trie.EnumerateMatches("hel?").Any(), "Trie");
Check(trie.TryMatchLongestPrefix("helpful", out int length, out _) && length == 4, "Trie longest prefix");
var tree = new BTreeDictionary<int, string>();
for (int i = 0; i < 10_000; i++)
{
    tree[i] = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

Check(tree.GetAt(5000).Key == 5000 && tree.CountLessThan(1234) == 1234, "BTreeDictionary");
BTreeSet<string> set = ["b", "a"];
Check(set[0] == "a", "BTreeSet");
RoaringBitmap bitmap = [1, 2, 70_000];
Check(RoaringBitmap.FromBytes((bitmap | new RoaringBitmap([5])).ToBytes()).Count == 4, "RoaringBitmap");
var pq = new IndexedPriorityQueue<string, int>();
pq.Enqueue("a", 5);
pq.UpdatePriority("a", 1);
Check(pq.Dequeue() == "a", "IndexedPriorityQueue");
Deque<int> deque = [1, 2];
deque.PushFront(0);
CircularBuffer<int> window = [1, 2, 3];
window.PushBack(4);
Check(deque[0] == 0 && window[0] == 2, "Deque/CircularBuffer");
var intervals = new IntervalTree<DateOnly, string>([new(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5), "x")]);
Check(intervals.Query(new DateOnly(2026, 1, 3)).Any(), "IntervalTree");
var sets = new DisjointSet<string>();
sets.Union("a", "b");
Check(sets.AreConnected("b", "a") && new DisjointSet(3).Union(0, 2), "DisjointSet");
var cache = new LruCache<string, int>(2, StringComparer.Ordinal);
cache["x"] = 1;
Check(cache.TryGetValue("x".AsSpan(), out _), "LruCache");
Check(Tree.Count(0, static n => n < 7 ? [(2 * n) + 1, (2 * n) + 2] : null) == 15, "Tree");

// Probabilistic
var bloom = new BloomFilter<string>(1000, 0.01);
bloom.Add("x");
Check(BloomFilter<string>.FromBytes(bloom.ToBytes()).MightContain("x"), "BloomFilter");
var fuse = new BinaryFuseFilter<long>(Enumerable.Range(0, 1000).Select(static i => (long)i));
Check(BinaryFuseFilter<long>.FromBytes(fuse.ToBytes()).MightContain(42), "BinaryFuseFilter");
var hll = new HyperLogLog<int>();
for (int i = 0; i < 10_000; i++)
{
    hll.Add(i);
}

Check(Math.Abs(hll.Estimate() - 10_000) < 500, "HyperLogLog");
var cms = new CountMinSketch<string>(0.01, 0.01);
cms.Add("x", 3);
Check(cms.Estimate("x") >= 3, "CountMinSketch");
var digest = new TDigest();
for (int i = 0; i < 10_000; i++)
{
    digest.Add(i);
}

Check(Math.Abs(digest.Quantile(0.5) - 5000) < 100, "TDigest");
var top = new TopK<string>(10);
top.Add("x", 5);
Check(top.GetTop(1)[0].Item == "x", "TopK");

Console.WriteLine(failures == 0 ? "All AOT smoke checks passed." : $"{failures} checks failed.");
return failures == 0 ? 0 : 1;
