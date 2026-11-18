using Bojalib.Collections;
using CsCheck;

namespace Bojalib.Tests.Collections;

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
        }, iter: TestScale.Iterations(500));
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
    public void UpdatePriority_KeepsOriginalKeyInstance()
    {
        var queue = new IndexedPriorityQueue<string, int>(keyComparer: StringComparer.OrdinalIgnoreCase);
        queue.Enqueue("key", 5);

        queue.UpdatePriority("KEY", 1);
        queue.EnqueueOrUpdate("Key", 0);

        queue.Should().ContainSingle().Which.Key.Should().Be("key");
        queue.Dequeue().Should().Be("key");
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
