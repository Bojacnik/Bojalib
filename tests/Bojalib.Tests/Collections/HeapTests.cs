using Bojalib.Collections;
using CsCheck;

namespace Bojalib.Tests.Collections;

public class BinaryHeapTests
{
    [Test]
    public void RandomOperations_MatchSortedModel()
    {
        Gen.Select(Gen.Int[-50, 50], Gen.Int[0, 3]).Array[0, 500].Sample(ops =>
        {
            var heap = new BinaryHeap<int>();
            var model = new List<int>();
            foreach ((int value, int op) in ops)
            {
                switch (op)
                {
                    case 0:
                        heap.TryPop(out int popped).Should().Be(model.Count > 0);
                        if (model.Count > 0)
                        {
                            int min = model.Min();
                            popped.Should().Be(min);
                            model.Remove(min);
                        }

                        break;
                    case 1:
                        int expected = model.Count > 0 && model.Min() < value ? model.Min() : value;
                        heap.PushPop(value).Should().Be(expected);
                        if (expected != value)
                        {
                            model.Remove(expected);
                            model.Add(value);
                        }

                        break;
                    default:
                        heap.Push(value);
                        model.Add(value);
                        break;
                }

                heap.Count.Should().Be(model.Count);
                if (model.Count > 0)
                {
                    heap.Peek().Should().Be(model.Min());
                }
            }

            heap.Should().BeEquivalentTo(model);
        }, iter: 500);
    }

    [Test]
    public void Constructors_Heapify_AndDrainInOrder()
    {
        Gen.Int.Array[0, 2000].Sample(items =>
        {
            Drain(new BinaryHeap<int>(items)).Should().Equal(items.Order());
            Drain(new BinaryHeap<int>(items.AsSpan())).Should().Equal(items.Order());
            Drain(new BinaryHeap<int>(items, Comparer<int>.Create(static (a, b) => b.CompareTo(a)))).Should().Equal(items.OrderDescending());
        }, iter: 100);
    }

    [Test]
    public void CollectionExpression_StringsAndCustomComparer()
    {
        BinaryHeap<int> heap = [5, 1, 4];
        heap.Pop().Should().Be(1);

        var strings = new BinaryHeap<string>(StringComparer.Ordinal);
        strings.Push("pear");
        strings.Push("apple");
        strings.Peek().Should().Be("apple");
        strings.Comparer.Should().BeSameAs(StringComparer.Ordinal);
        new BinaryHeap<int>(Comparer<int>.Default).Comparer.Should().BeSameAs(Comparer<int>.Default);
    }

    [Test]
    public void EmptyHeap_ThrowsOrReturnsFalse()
    {
        var heap = new BinaryHeap<int>();

        heap.IsEmpty.Should().BeTrue();
        heap.TryPeek(out _).Should().BeFalse();
        heap.Invoking(h => h.Pop()).Should().Throw<InvalidOperationException>();
        heap.Invoking(h => h.Peek()).Should().Throw<InvalidOperationException>();
        heap.PushPop(3).Should().Be(3);
    }

    [Test]
    public void ClearCapacityAndEnumerationGuards()
    {
        var heap = new BinaryHeap<string>();
        heap.EnsureCapacity(100);
        heap.Push("a");
        heap.TryPeek(out string? top).Should().BeTrue();
        top.Should().Be("a");

        Action modifyDuringEnumeration = () =>
        {
            foreach (string _ in heap)
            {
                heap.Push("b");
            }
        };
        modifyDuringEnumeration.Should().Throw<InvalidOperationException>();

        heap.Clear();
        heap.Should().BeEmpty();
        FluentActions.Invoking(() => heap.EnsureCapacity(-1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new BinaryHeap<int>((IEnumerable<int>)null!)).Should().Throw<ArgumentNullException>();
    }

    private static List<int> Drain(BinaryHeap<int> heap)
    {
        var result = new List<int>();
        while (heap.TryPop(out int x))
        {
            result.Add(x);
        }

        return result;
    }
}

public class IndexedPriorityQueueTests
{
    [Test]
    public void RandomOperations_MatchDictionaryModel()
    {
        Gen.Select(Gen.Int[0, 15], Gen.Int[0, 100], Gen.Int[0, 4]).Array[0, 400].Sample(ops =>
        {
            var queue = new IndexedPriorityQueue<int, int>();
            var model = new Dictionary<int, int>();
            foreach ((int key, int priority, int op) in ops)
            {
                switch (op)
                {
                    case 0:
                        queue.TryEnqueue(key, priority).Should().Be(model.TryAdd(key, priority));
                        break;
                    case 1:
                        queue.EnqueueOrUpdate(key, priority);
                        model[key] = priority;
                        break;
                    case 2:
                        queue.Remove(key).Should().Be(model.Remove(key));
                        break;
                    default:
                        bool any = queue.TryDequeue(out int k, out int p);
                        any.Should().Be(model.Count > 0);
                        if (any)
                        {
                            p.Should().Be(model.Values.Min());
                            model[k].Should().Be(p);
                            model.Remove(k);
                        }

                        break;
                }

                queue.Count.Should().Be(model.Count);
                foreach ((int mk, int mp) in model)
                {
                    queue.TryGetPriority(mk, out int qp).Should().BeTrue();
                    qp.Should().Be(mp);
                }
            }
        }, iter: 500);
    }

    [Test]
    public void Dijkstra_FindsShortestPaths()
    {
        // 0 -> 1 (4), 0 -> 2 (1), 2 -> 1 (2), 1 -> 3 (1), 2 -> 3 (5)
        (int To, int Weight)[][] edges = [[(1, 4), (2, 1)], [(3, 1)], [(1, 2), (3, 5)], []];
        int[] distance = [0, int.MaxValue, int.MaxValue, int.MaxValue];
        var queue = new IndexedPriorityQueue<int, int>();
        queue.Enqueue(0, 0);

        while (queue.TryDequeue(out int node, out int d))
        {
            foreach ((int to, int weight) in edges[node])
            {
                if (d + weight < distance[to])
                {
                    distance[to] = d + weight;
                    queue.EnqueueOrUpdate(to, distance[to]);
                }
            }
        }

        distance.Should().Equal(0, 3, 1, 4);
    }

    [Test]
    public void Misc_ApiBehaviour()
    {
        var queue = new IndexedPriorityQueue<string, double>(Comparer<double>.Create(static (a, b) => b.CompareTo(a)), StringComparer.OrdinalIgnoreCase);
        queue.Enqueue("a", 1);
        queue.Enqueue("B", 5);

        queue.Contains("b").Should().BeTrue();
        queue.TryPeek(out string? top, out double priority).Should().BeTrue();
        (top, priority).Should().Be(("B", 5d));
        queue.Invoking(q => q.Enqueue("A", 2)).Should().Throw<ArgumentException>();
        queue.Invoking(q => q.UpdatePriority("zzz", 2)).Should().Throw<KeyNotFoundException>();
        queue.UpdatePriority("a", 10);
        queue.Dequeue().Should().Be("a");
        queue.Should().ContainSingle();
        queue.TryGetPriority("zzz", out _).Should().BeFalse();
        queue.Clear();
        queue.IsEmpty.Should().BeTrue();
        queue.Invoking(q => q.Dequeue()).Should().Throw<InvalidOperationException>();
        queue.TryPeek(out _, out _).Should().BeFalse();
        queue.Remove("a").Should().BeFalse();
    }

    [Test]
    public void Enumeration_ThrowsOnModification()
    {
        var queue = new IndexedPriorityQueue<int, int>();
        queue.Enqueue(1, 1);
        queue.Enqueue(2, 2);

        Action act = () =>
        {
            foreach (KeyValuePair<int, int> _ in queue)
            {
                queue.EnqueueOrUpdate(3, 0);
            }
        };

        act.Should().Throw<InvalidOperationException>();
    }
}
