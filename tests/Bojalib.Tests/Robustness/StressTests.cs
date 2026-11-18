using Bojalib.Collections;
using Bojalib.Sorting;

namespace Bojalib.Tests.Robustness;

/// <summary>Large inputs and long operation sequences. Sizes grow with <see cref="TestScale"/> for nightly runs.</summary>
public class StressTests
{
    private static int Size(int baseline) => TestScale.IsLongRun ? baseline * 10 : baseline;

    [Test]
    public void ParallelSort_HugeInputs_EveryThreadCount()
    {
        var random = new Random(21);
        int n = Size(2_000_000);
        int[] source = [.. Enumerable.Range(0, n).Select(_ => random.Next())];
        int[] expected = [.. source];
        Array.Sort(expected);

        foreach (int threads in (int[])[1, 2, 3, 5, Environment.ProcessorCount, Environment.ProcessorCount * 2, -1])
        {
            int[] actual = [.. source];
            ParallelSort.Sort(actual, threads);
            actual.AsSpan().SequenceEqual(expected).Should().BeTrue("threads = {0}", threads);
        }
    }

    [Test]
    public void ParallelSort_ManyConcurrentCalls_ShareThePoolSafely()
    {
        // Several sorts at once compete for ArrayPool buffers and thread-pool threads.
        int n = Size(300_000);
        Task[] sorts = [.. Enumerable.Range(0, 8).Select(seed => Task.Run(() =>
        {
            var random = new Random(seed);
            long[] data = [.. Enumerable.Range(0, n).Select(_ => random.NextInt64(-1000, 1000))];
            long[] expected = [.. data];
            Array.Sort(expected);
            ParallelSort.Sort(data);
            data.AsSpan().SequenceEqual(expected).Should().BeTrue("concurrent sort {0}", seed);

            string[] words = [.. Enumerable.Range(0, n / 4).Select(_ => random.Next(10_000).ToString("x", System.Globalization.CultureInfo.InvariantCulture))];
            string[] expectedWords = [.. words];
            Array.Sort(expectedWords, StringComparer.Ordinal);
            ParallelSort.Sort(words, StringComparer.Ordinal);
            words.Should().Equal(expectedWords);
        }))];

        Task.WaitAll(sorts);
    }

    [Test]
    public void BTree_MillionOperations_StaysValid()
    {
        var random = new Random(22);
        var tree = new BTreeDictionary<int, int>();
        var model = new Dictionary<int, int>();
        int operations = Size(400_000);
        int keySpace = operations / 4;
        for (int i = 0; i < operations; i++)
        {
            int key = random.Next(keySpace);
            if (random.Next(3) == 0)
            {
                tree.Remove(key).Should().Be(model.Remove(key));
            }
            else
            {
                tree[key] = i;
                model[key] = i;
            }

            if (i % 50_000 == 0)
            {
                tree.Validate();
            }
        }

        tree.Validate();
        tree.Count.Should().Be(model.Count);
        tree.Keys.Should().Equal(model.Keys.Order());
        for (int i = 0; i < 1000; i++)
        {
            int index = random.Next(tree.Count);
            tree.CountLessThan(tree.GetAt(index).Key).Should().Be(index);
        }
    }

    [Test]
    public void Trie_LargeRandomWorkload_MatchesDictionary()
    {
        var random = new Random(23);
        var trie = new Trie<int>();
        var model = new Dictionary<string, int>(StringComparer.Ordinal);
        int operations = Size(300_000);
        for (int i = 0; i < operations; i++)
        {
            string key = new([.. Enumerable.Range(0, random.Next(1, 9)).Select(_ => (char)('a' + random.Next(6)))]);
            if (random.Next(3) == 0)
            {
                trie.Remove(key).Should().Be(model.Remove(key));
            }
            else
            {
                trie[key] = i;
                model[key] = i;
            }
        }

        trie.Count.Should().Be(model.Count);

        // Compare materialized lists: asserting a dictionary-like subject against a lazy, sorted expectation makes the
        // assertion library re-sort it per element (minutes for ~70K keys).
        List<KeyValuePair<string, int>> expected = [.. model.OrderBy(static kv => kv.Key, StringComparer.Ordinal)];
        List<KeyValuePair<string, int>> actual = [.. trie];
        actual.SequenceEqual(expected).Should().BeTrue("trie contents in ordinal order");
        foreach (string prefix in (string[])["a", "ab", "fed", "cccc"])
        {
            List<string> prefixed = [.. trie.EnumeratePrefix(prefix).Select(static kv => kv.Key)];
            List<string> expectedPrefixed = [.. model.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
            prefixed.Should().Equal(expectedPrefixed);
        }
    }

    [Test]
    public void RoaringBitmap_LargeRandomWorkload_MatchesHashSet()
    {
        var random = new Random(24);
        var bitmap = new RoaringBitmap();
        var model = new HashSet<uint>();
        int operations = Size(1_000_000);
        for (int i = 0; i < operations; i++)
        {
            // A few hot chunks get dense, the rest stay sparse.
            uint value = random.Next(4) == 0 ? (uint)random.Next(200_000) : (uint)random.NextInt64(uint.MaxValue);
            if (random.Next(4) == 0)
            {
                bitmap.Remove(value).Should().Be(model.Remove(value));
            }
            else
            {
                bitmap.Add(value).Should().Be(model.Add(value));
            }
        }

        bitmap.Count.Should().Be(model.Count);
        bitmap.ToArray().Should().Equal(model.Order());
        RoaringBitmap.FromBytes(bitmap.ToBytes()).Equals(bitmap).Should().BeTrue();
    }

    [Test]
    public void Sorts_HugeArrays()
    {
        var random = new Random(25);
        int n = Size(3_000_000);
        long[] source = [.. Enumerable.Range(0, n).Select(_ => random.NextInt64())];
        long[] expected = [.. source];
        Array.Sort(expected);

        foreach (Action<long[]> sort in (ReadOnlySpan<Action<long[]>>)[static a => QuickSort.Sort<long>(a), static a => RadixSort.Sort<long>(a), static a => MergeSort.Sort<long>(a)])
        {
            long[] actual = [.. source];
            sort(actual);
            actual.AsSpan().SequenceEqual(expected).Should().BeTrue();
        }
    }
}
