namespace Bojalib.Tests;

/// <summary>
/// Scales randomized tests. Default runs stay fast for CI; set <c>BOJALIB_TEST_SCALE</c> (e.g. 50) for the nightly
/// long run, which multiplies iteration counts and input sizes.
/// </summary>
internal static class TestScale
{
    public static readonly int Factor =
        int.TryParse(Environment.GetEnvironmentVariable("BOJALIB_TEST_SCALE"), out int factor) && factor > 0 ? factor : 1;

    public static bool IsLongRun => Factor > 1;

    public static int Iterations(int baseline) => checked(baseline * Factor);
}
