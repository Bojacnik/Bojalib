using System.Buffers.Binary;
using System.Diagnostics;
using Bojalib.Sorting;

namespace Bojalib.Probabilistic;

/// <summary>
/// Estimates quantiles (median, p95, p99, p99.9, …) of a stream of numbers in a few kilobytes, with the best accuracy
/// at the tails — what you want for latency percentiles. Digests from different servers can be merged.
/// </summary>
/// <remarks>
/// <para>
/// A merging t-digest (Dunning). The data is summarized as at most about 1.5 × <see cref="Compression"/> weighted
/// centroids; the arcsine scale function lets centroids near the median be large and keeps those near 0 and 1 tiny,
/// so extreme quantiles stay accurate. Added values are buffered and merged in batches (sorted with
/// <see cref="QuickSort"/>), so <see cref="Add"/> is amortized O(1) and allocation-free.
/// </para>
/// <para>The minimum and maximum are tracked exactly. Not thread-safe.</para>
/// </remarks>
[DebuggerDisplay("Count = {Count}, Centroids = {CentroidCount}")]
public sealed class TDigest
{
    private const int HeaderBytes = 44;

    private readonly double _normalizer;
    private double[] _means;
    private double[] _weights;
    private int _centroidCount;
    private readonly double[] _bufferValues;
    private readonly double[] _bufferWeights;
    private int _bufferCount;

    /// <summary>Creates an empty digest.</summary>
    /// <param name="compression">
    /// Accuracy versus size, 10–10,000. The default, 100, keeps at most about 150 centroids (≈2.5 KB) and estimates
    /// p99 to within roughly 0.1% of rank.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="compression"/> is outside 10–10,000.</exception>
    public TDigest(double compression = 100)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(compression, 10);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(compression, 10_000);
        Compression = compression;
        _normalizer = compression / (2 * Math.PI);
        int centroids = (int)Math.Ceiling(compression * Math.PI / 2) + 10;
        _means = new double[centroids];
        _weights = new double[centroids];
        int buffer = (int)(5 * compression) + centroids;
        _bufferValues = new double[buffer];
        _bufferWeights = new double[buffer];
    }

    /// <summary>Gets the compression parameter.</summary>
    public double Compression { get; }

    /// <summary>Gets the total number of values added (the sum of their weights).</summary>
    public long Count { get; private set; }

    /// <summary>Gets the smallest value added, or NaN if empty.</summary>
    public double Min { get; private set; } = double.NaN;

    /// <summary>Gets the largest value added, or NaN if empty.</summary>
    public double Max { get; private set; } = double.NaN;

    /// <summary>Gets the current number of centroids (after merging buffered values).</summary>
    public int CentroidCount
    {
        get
        {
            Flush();
            return _centroidCount;
        }
    }

    /// <summary>Adds <paramref name="value"/>, <paramref name="weight"/> times.</summary>
    /// <param name="value">A finite number.</param>
    /// <param name="weight">How many occurrences; at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is NaN or infinite, or <paramref name="weight"/> is less than 1.</exception>
    public void Add(double value, long weight = 1)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be a finite number.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(weight, 1);
        Min = Count == 0 ? value : Math.Min(Min, value);
        Max = Count == 0 ? value : Math.Max(Max, value);
        Count += weight;
        Buffer(value, weight);
    }

    /// <summary>Estimates the value below which a fraction <paramref name="q"/> of the data falls.</summary>
    /// <param name="q">The quantile, 0–1 (0.5 = median, 0.99 = p99).</param>
    /// <returns>The estimated value; exactly <see cref="Min"/> at 0 and <see cref="Max"/> at 1.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="q"/> is outside 0–1.</exception>
    /// <exception cref="InvalidOperationException">The digest is empty.</exception>
    public double Quantile(double q)
    {
        if (q is < 0 or > 1 || double.IsNaN(q))
        {
            throw new ArgumentOutOfRangeException(nameof(q), "Must be between 0 and 1.");
        }

        ThrowIfEmpty();
        Flush();

        // Piecewise-linear through (0, Min), each centroid's centre (cumulative weight, mean) and (Count, Max).
        double target = q * Count;
        double previousRank = 0;
        double previousValue = Min;
        double cumulative = 0;
        for (int i = 0; i < _centroidCount; i++)
        {
            double rank = cumulative + (_weights[i] / 2);
            if (target <= rank)
            {
                return Interpolate(target, previousRank, previousValue, rank, _means[i]);
            }

            previousRank = rank;
            previousValue = _means[i];
            cumulative += _weights[i];
        }

        return Interpolate(target, previousRank, previousValue, Count, Max);
    }

    /// <summary>Estimates the fraction of values that are &lt;= <paramref name="value"/> (its percentile rank).</summary>
    /// <param name="value">The value.</param>
    /// <returns>A fraction between 0 and 1.</returns>
    /// <exception cref="InvalidOperationException">The digest is empty.</exception>
    public double Cdf(double value)
    {
        ThrowIfEmpty();
        Flush();
        if (value < Min)
        {
            return 0;
        }

        if (value >= Max)
        {
            return 1;
        }

        double previousRank = 0;
        double previousValue = Min;
        double cumulative = 0;
        for (int i = 0; i < _centroidCount; i++)
        {
            double rank = cumulative + (_weights[i] / 2);
            if (value < _means[i])
            {
                return Interpolate(value, previousValue, previousRank, _means[i], rank) / Count;
            }

            previousRank = rank;
            previousValue = _means[i];
            cumulative += _weights[i];
        }

        return Interpolate(value, previousValue, previousRank, Max, Count) / Count;
    }

    /// <summary>Adds everything summarized by <paramref name="other"/> to this digest.</summary>
    /// <param name="other">Another digest (any compression).</param>
    public void Merge(TDigest other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Count == 0)
        {
            return;
        }

        other.Flush();
        Min = Count == 0 ? other.Min : Math.Min(Min, other.Min);
        Max = Count == 0 ? other.Max : Math.Max(Max, other.Max);
        Count += other.Count;
        for (int i = 0; i < other._centroidCount; i++)
        {
            Buffer(other._means[i], other._weights[i]);
        }
    }

    /// <summary>Removes all data.</summary>
    public void Clear()
    {
        _centroidCount = 0;
        _bufferCount = 0;
        Count = 0;
        Min = Max = double.NaN;
    }

    /// <summary>Serializes the digest (little-endian).</summary>
    /// <returns>The digest's bytes.</returns>
    public byte[] ToBytes()
    {
        Flush();
        byte[] bytes = new byte[HeaderBytes + (_centroidCount * 16)];
        Span<byte> span = bytes;
        BinaryPrimitives.WriteDoubleLittleEndian(span, Compression);
        BinaryPrimitives.WriteInt64LittleEndian(span[8..], Count);
        BinaryPrimitives.WriteDoubleLittleEndian(span[16..], Min);
        BinaryPrimitives.WriteDoubleLittleEndian(span[24..], Max);
        BinaryPrimitives.WriteInt32LittleEndian(span[32..], _centroidCount);
        for (int i = 0; i < _centroidCount; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(span[(HeaderBytes + (i * 16))..], _means[i]);
            BinaryPrimitives.WriteDoubleLittleEndian(span[(HeaderBytes + (i * 16) + 8)..], _weights[i]);
        }

        return bytes;
    }

    /// <summary>Recreates a digest from <see cref="ToBytes"/> output.</summary>
    /// <param name="bytes">The serialized digest.</param>
    /// <returns>The digest.</returns>
    /// <exception cref="ArgumentException"><paramref name="bytes"/> is not a serialized digest.</exception>
    public static TDigest FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderBytes)
        {
            throw new ArgumentException("Not a serialized t-digest.", nameof(bytes));
        }

        double compression = BinaryPrimitives.ReadDoubleLittleEndian(bytes);
        int centroids = BinaryPrimitives.ReadInt32LittleEndian(bytes[32..]);
        if (compression is < 10 or > 10_000 || centroids < 0 || bytes.Length != HeaderBytes + ((long)centroids * 16))
        {
            throw new ArgumentException("Not a serialized t-digest.", nameof(bytes));
        }

        var digest = new TDigest(compression)
        {
            Count = BinaryPrimitives.ReadInt64LittleEndian(bytes[8..]),
            Min = BinaryPrimitives.ReadDoubleLittleEndian(bytes[16..]),
            Max = BinaryPrimitives.ReadDoubleLittleEndian(bytes[24..]),
        };

        if (centroids > digest._means.Length)
        {
            digest._means = new double[centroids];
            digest._weights = new double[centroids];
        }

        for (int i = 0; i < centroids; i++)
        {
            digest._means[i] = BinaryPrimitives.ReadDoubleLittleEndian(bytes[(HeaderBytes + (i * 16))..]);
            digest._weights[i] = BinaryPrimitives.ReadDoubleLittleEndian(bytes[(HeaderBytes + (i * 16) + 8)..]);
        }

        digest._centroidCount = centroids;
        return digest;
    }

    private static double Interpolate(double x, double x0, double y0, double x1, double y1) =>
        x1 <= x0 ? (y0 + y1) / 2 : y0 + ((y1 - y0) * (x - x0) / (x1 - x0));

    private void ThrowIfEmpty()
    {
        if (Count == 0)
        {
            throw new InvalidOperationException("The digest is empty.");
        }
    }

    private void Buffer(double value, double weight)
    {
        if (_bufferCount + _centroidCount >= _bufferValues.Length)
        {
            Flush();
        }

        _bufferValues[_bufferCount] = value;
        _bufferWeights[_bufferCount] = weight;
        _bufferCount++;
    }

    // Merges the buffered points with the existing centroids in one sorted pass.
    private void Flush()
    {
        if (_bufferCount == 0)
        {
            return;
        }

        // Existing centroids join the buffer; then everything is sorted by mean, carrying weights along.
        Array.Copy(_means, 0, _bufferValues, _bufferCount, _centroidCount);
        Array.Copy(_weights, 0, _bufferWeights, _bufferCount, _centroidCount);
        int n = _bufferCount + _centroidCount;
        QuickSort.Sort(_bufferValues.AsSpan(0, n), _bufferWeights.AsSpan(0, n));

        double total = 0;
        for (int i = 0; i < n; i++)
        {
            total += _bufferWeights[i];
        }

        // Greedy merge: keep absorbing neighbours into the current centroid while the scale function allows it.
        int count = 0;
        double weightSoFar = 0;
        double mean = _bufferValues[0];
        double weight = _bufferWeights[0];
        double limit = total * InverseScale(Scale(0) + 1);
        for (int i = 1; i < n; i++)
        {
            double proposed = weight + _bufferWeights[i];
            if (weightSoFar + proposed <= limit)
            {
                mean += (_bufferValues[i] - mean) * _bufferWeights[i] / proposed;
                weight = proposed;
            }
            else
            {
                Emit(ref count, mean, weight);
                weightSoFar += weight;
                limit = total * InverseScale(Scale(weightSoFar / total) + 1);
                mean = _bufferValues[i];
                weight = _bufferWeights[i];
            }
        }

        Emit(ref count, mean, weight);
        _centroidCount = count;
        _bufferCount = 0;
    }

    private void Emit(ref int count, double mean, double weight)
    {
        if (count == _means.Length)
        {
            Array.Resize(ref _means, count * 2);
            Array.Resize(ref _weights, count * 2);
        }

        _means[count] = mean;
        _weights[count] = weight;
        count++;
    }

    // The k1 scale function: k(q) = δ/(2π)·asin(2q − 1). A centroid may span at most one unit of k.
    private double Scale(double q) => _normalizer * Math.Asin((2 * Math.Clamp(q, 0, 1)) - 1);

    private double InverseScale(double k) =>
        k >= _normalizer * Math.PI / 2 ? 1 : (Math.Sin(k / _normalizer) + 1) / 2;
}
