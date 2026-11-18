using Bojalib.Collections;
using CsCheck;

namespace Bojalib.Tests.Collections;

public class LruCacheTests
{
    [Test]
    public void RandomOperations_MatchListModel()
    {
        Gen.Select(Gen.Int[1, 5], Gen.Select(Gen.Int[0, 8], Gen.Int[0, 4]).Array[0, 300]).Sample(t =>
        {
            (int capacity, (int Key, int Op)[] ops) = t;
            var evictions = new List<int>();
            var cache = new LruCache<int, int>(capacity, onEvicted: (k, _) => evictions.Add(k));
            var model = new List<(int Key, int Value)>(); // front = most recently used

            for (int i = 0; i < ops.Length; i++)
            {
                (int key, int op) = ops[i];
                int index = model.FindIndex(e => e.Key == key);
                switch (op)
                {
                    case 0:
                        cache.TryGetValue(key, out int got).Should().Be(index >= 0);
                        if (index >= 0)
                        {
                            got.Should().Be(model[index].Value);
                            (int, int) e = model[index];
                            model.RemoveAt(index);
                            model.Insert(0, e);
                        }

                        break;
                    case 1:
                        cache.Remove(key).Should().Be(index >= 0);
                        if (index >= 0)
                        {
                            model.RemoveAt(index);
                        }

                        break;
                    case 2:
                        cache.TryPeek(key, out _).Should().Be(index >= 0);
                        break;
                    default:
                        evictions.Clear();
                        cache[key] = i;
                        if (index >= 0)
                        {
                            model.RemoveAt(index);
                        }
                        else if (model.Count == capacity)
                        {
                            evictions.Should().Equal(model[^1].Key);
                            model.RemoveAt(model.Count - 1);
                        }

                        model.Insert(0, (key, i));
                        break;
                }

                cache.Should().Equal(model.Select(static e => new KeyValuePair<int, int>(e.Key, e.Value)));
            }
        }, iter: TestScale.Iterations(500));
    }

    [Test]
    public void AlternateLookup_SpanKeysOnStringCache()
    {
        var cache = new LruCache<string, int>(2, StringComparer.Ordinal);
        cache["alpha"] = 1;
        cache["beta"] = 2;

        cache.TryGetValue("xx alpha".AsSpan(3), out int value).Should().BeTrue();
        value.Should().Be(1);
        cache["gamma"] = 3; // evicts beta (alpha was just used)

        cache.ContainsKey("beta").Should().BeFalse();
        cache.TryGetValue("nope".AsSpan(), out _).Should().BeFalse();
        new LruCache<int, int>(1).Invoking(c => c.TryGetValue("x".AsSpan(), out _)).Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void GetOrAdd_CallsFactoryOnlyOnMiss()
    {
        var cache = new LruCache<int, string>(10);
        int calls = 0;

        cache.GetOrAdd(1, k => { calls++; return $"v{k}"; }).Should().Be("v1");
        cache.GetOrAdd(1, k => { calls++; return "other"; }).Should().Be("v1");

        calls.Should().Be(1);
        cache.Count.Should().Be(1);
        cache.Capacity.Should().Be(10);
        cache.Invoking(c => c.GetOrAdd(2, null!)).Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void EvictionCallback_CanUseTheCache()
    {
        LruCache<int, int>? cache = null;
        var seen = new List<int>();
        cache = new LruCache<int, int>(1, onEvicted: (k, _) => seen.Add(cache!.Count));
        cache[1] = 1;
        cache[2] = 2;

        seen.Should().Equal(1);
        cache.Should().Equal([new KeyValuePair<int, int>(2, 2)]);
    }

    [Test]
    public void Misc_IndexerClearAndValidation()
    {
        var cache = new LruCache<int, int>(2) { [1] = 10 };

        cache[1].Should().Be(10);
        cache.Invoking(c => _ = c[9]).Should().Throw<KeyNotFoundException>();
        cache.Clear();
        cache.Count.Should().Be(0);
        cache.Should().BeEmpty();
        FluentActions.Invoking(() => new LruCache<int, int>(0)).Should().Throw<ArgumentOutOfRangeException>();

        cache[1] = 1;
        LruCache<int, int>.Enumerator e = cache.GetEnumerator();
        e.MoveNext().Should().BeTrue();
        e.Reset();
        e.MoveNext().Should().BeTrue();
        e.MoveNext().Should().BeFalse();
        e.MoveNext().Should().BeFalse();
        cache[2] = 2;
        FluentActions.Invoking(() => e.MoveNext()).Should().Throw<InvalidOperationException>();
    }
}
