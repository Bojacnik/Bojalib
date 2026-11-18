using Bojalib.Probabilistic;

namespace Bojalib.Tests.Robustness;

/// <summary>
/// Estimators judged over many independent runs (different item sets) rather than one: the average error must be
/// near zero (no bias) and the spread close to what the theory predicts.
/// </summary>
public class StatisticalTests
{
    private static int Runs => TestScale.IsLongRun ? 200 : 30;

    [Test]
    public void HyperLogLog_IsUnbiasedWithTheExpectedSpread()
    {
        const int distinct = 50_000;
        var errors = new List<double>();
        double standardError = 0;
        for (int run = 0; run < Runs; run++)
        {
            var sketch = new HyperLogLog<long>(precision: 12);
            standardError = sketch.StandardError;
            long offset = (long)run * 10_000_000;
            for (long i = 0; i < distinct; i++)
            {
                sketch.Add(offset + i);
            }

            errors.Add((sketch.Estimate() - (double)distinct) / distinct);
        }

        double mean = errors.Average();
        double spread = Math.Sqrt(errors.Sum(e => (e - mean) * (e - mean)) / (errors.Count - 1));

        // The mean of n runs has standard error σ/√n; allow 4 of those.
        Math.Abs(mean).Should().BeLessThan(4 * standardError / Math.Sqrt(Runs), "bias");
        spread.Should().BeInRange(0.5 * standardError, 1.5 * standardError, "spread vs theoretical {0:P2}", standardError);
    }

    [Test]
    public void BloomAndFuseFilters_FalsePositiveRatesAcrossRuns()
    {
        var bloomRates = new List<double>();
        var fuseRates = new List<double>();
        for (int run = 0; run < Runs; run++)
        {
            int[] members = [.. Enumerable.Range(run * 1_000_000, 20_000)];
            var bloom = new BloomFilter<int>(members.Length, 0.01);
            foreach (int m in members)
            {
                bloom.Add(m);
            }

            var fuse = new BinaryFuseFilter<int>(members);
            int[] others = [.. Enumerable.Range((run * 1_000_000) + 500_000, 50_000)];
            bloomRates.Add(others.Count(bloom.MightContain) / (double)others.Length);
            fuseRates.Add(others.Count(fuse.MightContain) / (double)others.Length);
        }

        bloomRates.Average().Should().BeLessThan(0.015, "configured for 1%");
        bloomRates.Max().Should().BeLessThan(0.025);
        fuseRates.Average().Should().BeInRange(0.0025, 0.0055, "8-bit fingerprints give ~1/256 = 0.39%");
    }

    [Test]
    public void TDigest_TailAccuracyAcrossRuns()
    {
        double worstP99 = 0;
        double worstMedian = 0;
        for (int run = 0; run < Runs; run++)
        {
            var random = new Random(run);
            double[] data = [.. Enumerable.Range(0, 100_000).Select(_ => -Math.Log(1 - random.NextDouble()) * (1 + run))];
            var digest = new TDigest();
            foreach (double value in data)
            {
                digest.Add(value);
            }

            Array.Sort(data);
            double RankOf(double x) => Array.BinarySearch(data, x) is var i && i < 0 ? ~i / (double)data.Length : i / (double)data.Length;
            worstP99 = Math.Max(worstP99, Math.Abs(RankOf(digest.Quantile(0.99)) - 0.99));
            worstMedian = Math.Max(worstMedian, Math.Abs(RankOf(digest.Quantile(0.5)) - 0.5));
        }

        worstP99.Should().BeLessThan(0.001, "p99 rank error, worst of {0} runs", Runs);
        worstMedian.Should().BeLessThan(0.005, "median rank error, worst of {0} runs", Runs);
    }

    [Test]
    public void CountMinSketch_ErrorBoundHoldsAtTheConfiguredProbability()
    {
        int violations = 0;
        int checks = 0;
        for (int run = 0; run < Runs; run++)
        {
            var random = new Random(run);
            var sketch = new CountMinSketch<int>(epsilon: 0.005, delta: 0.05);
            var truth = new Dictionary<int, int>();
            for (int i = 0; i < 50_000; i++)
            {
                int item = random.Next(5_000);
                sketch.Add(item);
                truth[item] = truth.GetValueOrDefault(item) + 1;
            }

            long bound = (long)Math.Ceiling(0.005 * sketch.TotalCount);
            foreach ((int item, int count) in truth)
            {
                long estimate = sketch.Estimate(item);
                estimate.Should().BeGreaterThanOrEqualTo(count);
                checks++;
                if (estimate - count > bound)
                {
                    violations++;
                }
            }
        }

        (violations / (double)checks).Should().BeLessThan(0.05, "delta = 5%");
    }
}
