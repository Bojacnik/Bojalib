using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Bojalib.Sorting;

namespace Bojalib.Collections;

/// <summary>A closed interval [<paramref name="Start"/>, <paramref name="End"/>] with an associated value.</summary>
/// <typeparam name="TPoint">The endpoint type.</typeparam>
/// <typeparam name="TValue">The value type.</typeparam>
/// <param name="Start">The first point in the interval.</param>
/// <param name="End">The last point in the interval (inclusive).</param>
/// <param name="Value">The associated value.</param>
public readonly record struct Interval<TPoint, TValue>(TPoint Start, TPoint End, TValue Value);

/// <summary>
/// Immutable index of intervals answering "which intervals contain this point / overlap this range?" in
/// O(log n + matches). Endpoints can be any comparable type (numbers, <see cref="DateTime"/>,
/// <see cref="DateOnly"/>, <see cref="TimeSpan"/>, …); for value types the comparisons are specialized and inlined.
/// </summary>
/// <remarks>
/// Built once in O(n log n): the intervals are sorted by start into an array that doubles as an implicit balanced
/// search tree, with each subtree's largest end point stored alongside so queries skip subtrees that can't overlap.
/// No node objects. Query results come back sorted by start and enumerating them allocates nothing.
/// </remarks>
/// <typeparam name="TPoint">The endpoint type.</typeparam>
/// <typeparam name="TValue">The value type.</typeparam>
[DebuggerDisplay("Count = {Count}")]
public sealed class IntervalTree<TPoint, TValue> : IReadOnlyCollection<Interval<TPoint, TValue>>
    where TPoint : IComparable<TPoint>
{
    private readonly Interval<TPoint, TValue>[] _intervals;
    private readonly TPoint[] _maxEnd; // _maxEnd[mid] = largest End in the subtree rooted at mid

    /// <summary>Builds the tree.</summary>
    /// <param name="intervals">The intervals. Duplicates and overlaps are fine.</param>
    /// <exception cref="ArgumentNullException"><paramref name="intervals"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An interval ends before it starts.</exception>
    public IntervalTree(IEnumerable<Interval<TPoint, TValue>> intervals)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        _intervals = [.. intervals];
        foreach (Interval<TPoint, TValue> interval in _intervals)
        {
            if (interval.End.CompareTo(interval.Start) < 0)
            {
                throw new ArgumentException($"Interval [{interval.Start}, {interval.End}] ends before it starts.", nameof(intervals));
            }
        }

        // Stable, so intervals with equal starts keep their input order in query results.
        MergeSort.Sort<Interval<TPoint, TValue>>(_intervals, static (a, b) => a.Start.CompareTo(b.Start));
        _maxEnd = new TPoint[_intervals.Length];
        if (_intervals.Length > 0)
        {
            BuildMaxEnd(0, _intervals.Length);
        }
    }

    /// <summary>Gets the number of intervals.</summary>
    public int Count => _intervals.Length;

    /// <summary>Gets all intervals, sorted by start.</summary>
    public ReadOnlySpan<Interval<TPoint, TValue>> Intervals => _intervals;

    /// <summary>Finds the intervals that contain <paramref name="point"/>.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The matching intervals, sorted by start. Enumeration allocates nothing.</returns>
    public QueryEnumerable Query(TPoint point) => new(this, point, point);

    /// <summary>Finds the intervals that overlap [<paramref name="start"/>, <paramref name="end"/>] (sharing at least one point).</summary>
    /// <param name="start">The start of the query range.</param>
    /// <param name="end">The end of the query range (inclusive).</param>
    /// <returns>The matching intervals, sorted by start. Enumeration allocates nothing.</returns>
    /// <exception cref="ArgumentException"><paramref name="end"/> is before <paramref name="start"/>.</exception>
    public QueryEnumerable Query(TPoint start, TPoint end)
    {
        if (end.CompareTo(start) < 0)
        {
            throw new ArgumentException("The query range ends before it starts.", nameof(end));
        }

        return new(this, start, end);
    }

    /// <summary>Enumerates all intervals, sorted by start.</summary>
    /// <returns>An enumerator.</returns>
    public IEnumerator<Interval<TPoint, TValue>> GetEnumerator() => ((IEnumerable<Interval<TPoint, TValue>>)_intervals).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private TPoint BuildMaxEnd(int low, int high)
    {
        int middle = (int)((uint)(low + high) >> 1);
        TPoint max = _intervals[middle].End;
        if (low < middle)
        {
            TPoint left = BuildMaxEnd(low, middle);
            max = left.CompareTo(max) > 0 ? left : max;
        }

        if (middle + 1 < high)
        {
            TPoint right = BuildMaxEnd(middle + 1, high);
            max = right.CompareTo(max) > 0 ? right : max;
        }

        _maxEnd[middle] = max;
        return max;
    }

    /// <summary>The intervals overlapping a query range. See <see cref="Query(TPoint, TPoint)"/>.</summary>
    public readonly struct QueryEnumerable : IEnumerable<Interval<TPoint, TValue>>
    {
        private readonly IntervalTree<TPoint, TValue> _tree;
        private readonly TPoint _start;
        private readonly TPoint _end;

        internal QueryEnumerable(IntervalTree<TPoint, TValue> tree, TPoint start, TPoint end)
        {
            _tree = tree;
            _start = start;
            _end = end;
        }

        /// <summary>Returns the enumerator.</summary>
        /// <returns>An allocation-free enumerator.</returns>
        public QueryEnumerator GetEnumerator() => new(_tree, _start, _end);

        IEnumerator<Interval<TPoint, TValue>> IEnumerable<Interval<TPoint, TValue>>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// In-order walk of the implicit tree with pruning. Its stack is a fixed inline buffer (the tree is at most
    /// 32 levels deep), so it never allocates.
    /// </summary>
    public struct QueryEnumerator : IEnumerator<Interval<TPoint, TValue>>
    {
        private readonly IntervalTree<TPoint, TValue> _tree;
        private readonly TPoint _start;
        private readonly TPoint _end;
        private QueryFrames _frames;
        private int _top;
        private int _current;

        internal QueryEnumerator(IntervalTree<TPoint, TValue> tree, TPoint start, TPoint end)
        {
            _tree = tree;
            _start = start;
            _end = end;
            _current = -1;
            _top = 0;
            Push(0, tree._intervals.Length, expanded: false);
        }

        /// <inheritdoc />
        public readonly Interval<TPoint, TValue> Current => _tree._intervals[_current];

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            Interval<TPoint, TValue>[] intervals = _tree._intervals;
            while (_top > 0)
            {
                QueryFrame frame = _frames[--_top];
                if (frame.Low >= frame.High)
                {
                    continue;
                }

                int middle = (int)((uint)(frame.Low + frame.High) >> 1);
                if (frame.Expanded)
                {
                    ref Interval<TPoint, TValue> interval = ref intervals[middle];
                    if (interval.Start.CompareTo(_end) <= 0 && interval.End.CompareTo(_start) >= 0)
                    {
                        _current = middle;
                        return true;
                    }

                    continue;
                }

                if (_tree._maxEnd[middle].CompareTo(_start) < 0)
                {
                    continue; // Nothing in this subtree reaches the query.
                }

                // Right subtree starts at or after this interval's start: skip it if that's past the query.
                if (intervals[middle].Start.CompareTo(_end) <= 0)
                {
                    Push(middle + 1, frame.High, expanded: false);
                }

                Push(frame.Low, frame.High, expanded: true);
                Push(frame.Low, middle, expanded: false);
            }

            return false;
        }

        /// <inheritdoc />
        public void Reset()
        {
            _top = 0;
            _current = -1;
            Push(0, _tree._intervals.Length, expanded: false);
        }

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }

        private void Push(int low, int high, bool expanded) => _frames[_top++] = new QueryFrame(low, high, expanded);
    }
}

internal readonly record struct QueryFrame(int Low, int High, bool Expanded);

// Each tree level leaves at most two frames on the stack, and an int-indexed tree has at most 32 levels.
[InlineArray(80)]
internal struct QueryFrames
{
    private QueryFrame _element;
}
