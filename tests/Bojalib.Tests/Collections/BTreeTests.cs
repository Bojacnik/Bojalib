using Bojalib.Collections;
using CsCheck;

namespace Bojalib.Tests.Collections;

public class BTreeDictionaryTests
{
    // Runs random operations against a SortedDictionary model, validating the tree structure after every step.
    private static void RunModel<TKey>(Gen<TKey> keys, IComparer<TKey>? comparer, int maxOps, int iterations)
        where TKey : notnull
    {
        Gen.Select(keys, Gen.Int[0, 9]).Array[0, maxOps].Sample(ops =>
        {
            IComparer<TKey> order = comparer ?? Comparer<TKey>.Default;
            var tree = new BTreeDictionary<TKey, int>(comparer);
            var model = new SortedDictionary<TKey, int>(order);

            for (int i = 0; i < ops.Length; i++)
            {
                (TKey key, int op) = ops[i];
                List<TKey> sortedKeys = [.. model.Keys];
                int rank = sortedKeys.Count(k => order.Compare(k, key) < 0);
                switch (op)
                {
                    case 0:
                    case 1:
                        tree.Remove(key, out int removed).Should().Be(model.Remove(key, out int expectedRemoved));
                        removed.Should().Be(expectedRemoved);
                        break;
                    case 2:
                        tree.TryAdd(key, i).Should().Be(model.TryAdd(key, i));
                        break;
                    case 3:
                        if (model.Count > 0)
                        {
                            int index = Math.Abs(i * 7919) % model.Count;
                            TKey victim = sortedKeys[index];
                            tree.GetAt(index).Should().Be(new KeyValuePair<TKey, int>(victim, model[victim]));
                            tree.RemoveAt(index);
                            model.Remove(victim);
                        }

                        break;
                    case 4:
                        tree.CountLessThan(key).Should().Be(rank);
                        tree.IndexOfKey(key).Should().Be(model.ContainsKey(key) ? rank : -1);
                        break;
                    case 5:
                        bool hasFloor = tree.TryGetFloor(key, out KeyValuePair<TKey, int> floor);
                        TKey[] atMost = [.. sortedKeys.Where(k => order.Compare(k, key) <= 0)];
                        hasFloor.Should().Be(atMost.Length > 0);
                        if (hasFloor) floor.Key.Should().Be(atMost[^1]);
                        bool hasCeiling = tree.TryGetCeiling(key, out KeyValuePair<TKey, int> ceiling);
                        TKey[] atLeast = [.. sortedKeys.Where(k => order.Compare(k, key) >= 0)];
                        hasCeiling.Should().Be(atLeast.Length > 0);
                        if (hasCeiling) ceiling.Key.Should().Be(atLeast[0]);
                        break;
                    default:
                        tree[key] = i;
                        model[key] = i;
                        break;
                }

                tree.Validate();
                tree.Count.Should().Be(model.Count);
            }

            tree.Should().Equal(model);
            if (model.Count > 0)
            {
                tree.First.Should().Be(model.First());
                tree.Last.Should().Be(model.Last());
                TKey low = model.Keys.First();
                TKey high = model.Keys.Last();
                tree.EnumerateRange(low, high).Should().Equal(model);
                TKey middle = model.Keys.ElementAt(model.Count / 2);
                tree.EnumerateRange(middle, high).Should().Equal(model.Where(kv => order.Compare(kv.Key, middle) >= 0));
            }
        }, iter: iterations);
    }

    [Test]
    public void RandomOperations_IntKeys_SimdSearchPath() => RunModel(Gen.Int[0, 400], null, 1500, 150);

    [Test]
    public void RandomOperations_StringKeys_ComparerPath() =>
        RunModel(Gen.Int[0, 300].Select(static i => i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)), StringComparer.Ordinal, 1200, 100);

    [Test]
    public void RandomOperations_DescendingComparer() =>
        RunModel(Gen.Long[-200, 200], Comparer<long>.Create(static (a, b) => b.CompareTo(a)), 1000, 100);

    [Test]
    public void RandomOperations_DoubleKeysWithNaN() =>
        RunModel(Gen.OneOf(Gen.Int[0, 300].Select(static i => i / 4.0), Gen.Const(double.NaN)), null, 1000, 100);

    [Test]
    public void LargeSequentialAndRandomWorkload_StaysValid()
    {
        var tree = new BTreeDictionary<int, int>();
        for (int i = 0; i < 200_000; i++)
        {
            tree.Add(i, i);
        }

        tree.Validate();
        tree.GetAt(123_456).Key.Should().Be(123_456);
        tree.CountLessThan(150_000).Should().Be(150_000);

        var random = new Random(1);
        int[] order = [.. Enumerable.Range(0, 200_000)];
        random.Shuffle(order);
        foreach (int key in order.Take(190_000))
        {
            tree.Remove(key).Should().BeTrue();
        }

        tree.Validate();
        tree.Count.Should().Be(10_000);
        tree.Keys.Should().Equal(order.Skip(190_000).Order());
    }

    [Test]
    public void ApiBehaviour()
    {
        var tree = new BTreeDictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["b"] = 2 };
        tree.Add("a", 1);

        tree["B"].Should().Be(2);
        tree.ContainsKey("A").Should().BeTrue();
        tree.Invoking(t => t.Add("A", 9)).Should().Throw<ArgumentException>();
        tree.Invoking(t => _ = t["zzz"]).Should().Throw<KeyNotFoundException>();
        tree.Invoking(t => t.GetAt(2)).Should().Throw<ArgumentOutOfRangeException>();
        tree.Invoking(t => t.TryGetValue(null!, out _)).Should().Throw<ArgumentNullException>();
        tree.Keys.Should().Equal("a", "b");
        tree.Values.Should().Equal(1, 2);
        tree.Comparer.Should().BeSameAs(StringComparer.OrdinalIgnoreCase);
        tree.EnumerateRange("c", "z").Should().BeEmpty();
        tree.EnumerateRange("b", "a").Should().BeEmpty();

        IReadOnlyDictionary<string, int> readOnly = tree;
        readOnly.TryGetValue("a", out int a).Should().BeTrue();
        a.Should().Be(1);

        tree.Clear();
        tree.Count.Should().Be(0);
        tree.Invoking(t => _ = t.First).Should().Throw<InvalidOperationException>();
        tree.Invoking(t => _ = t.Last).Should().Throw<InvalidOperationException>();
        tree.TryGetFloor("a", out _).Should().BeFalse();
        tree.TryGetCeiling("a", out _).Should().BeFalse();
        new BTreeDictionary<int, int>(Comparer<int>.Default).Comparer.Should().BeSameAs(Comparer<int>.Default);
    }

    [Test]
    public void Enumerators_DetectModificationAndReset()
    {
        var tree = new BTreeDictionary<int, int> { [1] = 1, [2] = 2 };

        BTreeDictionary<int, int>.Enumerator e = tree.GetEnumerator();
        e.MoveNext();
        e.Reset();
        e.MoveNext().Should().BeTrue();
        e.Current.Key.Should().Be(1);
        tree[3] = 3;
        FluentActions.Invoking(() => e.MoveNext()).Should().Throw<InvalidOperationException>();
    }
}

public class BTreeSetTests
{
    [Test]
    public void RandomOperations_MatchSortedSetAndList()
    {
        Gen.Select(Gen.Int[0, 500], Gen.Int[0, 4]).Array[0, 1500].Sample(ops =>
        {
            var set = new BTreeSet<int>();
            var model = new SortedSet<int>();
            for (int i = 0; i < ops.Length; i++)
            {
                (int value, int op) = ops[i];
                switch (op)
                {
                    case 0:
                        set.Remove(value).Should().Be(model.Remove(value));
                        break;
                    case 1:
                        if (model.Count > 0)
                        {
                            int index = value % model.Count;
                            int expected = model.ElementAt(index);
                            set[index].Should().Be(expected);
                            set.RemoveAt(index);
                            model.Remove(expected);
                        }

                        break;
                    case 2:
                        set.IndexOf(value).Should().Be(model.Contains(value) ? model.Count(x => x < value) : -1);
                        set.CountLessThan(value).Should().Be(model.Count(x => x < value));
                        break;
                    default:
                        set.Add(value).Should().Be(model.Add(value));
                        break;
                }

                set.Validate();
            }

            set.Should().Equal(model);
            set.Count.Should().Be(model.Count);
            if (model.Count > 0)
            {
                set.Min.Should().Be(model.Min);
                set.Max.Should().Be(model.Max);
                set.EnumerateRange(100, 300).Should().Equal(model.GetViewBetween(100, 300));
                set.TryGetFloor(250, out int floor).Should().Be(model.Any(x => x <= 250));
                set.TryGetCeiling(250, out int ceiling).Should().Be(model.Any(x => x >= 250));
                if (model.Any(x => x <= 250)) floor.Should().Be(model.Where(x => x <= 250).Max());
                if (model.Any(x => x >= 250)) ceiling.Should().Be(model.Where(x => x >= 250).Min());
            }
        }, iter: 200);
    }

    [Test]
    public void Leaderboard_RankAndPercentile()
    {
        BTreeSet<(int Score, string Name)> board = [(50, "ann"), (90, "bob"), (70, "cat"), (90, "abe")];

        board.CountLessThan((70, "")).Should().Be(1);   // one score below 70
        board[1].Should().Be((70, "cat"));               // second-lowest
        board.IndexOf((90, "bob")).Should().Be(3);
        board.Max.Should().Be((90, "bob"));
        board.Contains((90, "abe")).Should().BeTrue();
        board.Comparer.Should().NotBeNull();
    }

    [Test]
    public void EnumeratorsAndClear()
    {
        BTreeSet<string> set = ["b", "a", "b"];
        set.Should().Equal("a", "b");

        BTreeSet<string>.Enumerator e = set.GetEnumerator();
        e.MoveNext();
        e.Reset();
        e.MoveNext().Should().BeTrue();
        e.Current.Should().Be("a");

        set.Clear();
        set.Should().BeEmpty();
        set.Invoking(s => _ = s.Min).Should().Throw<InvalidOperationException>();
        set.TryGetFloor("x", out _).Should().BeFalse();
    }
}
