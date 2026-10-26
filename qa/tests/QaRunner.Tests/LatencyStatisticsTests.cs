using QaRunner.Measurement;

namespace QaRunner.Tests;

/// <summary>
/// p95 같은 백분위 계산이 맞는지 확인한다.
/// </summary>
public class LatencyStatisticsTests
{
    private static IEnumerable<TimeSpan> Seconds(params double[] seconds) => seconds.Select(TimeSpan.FromSeconds);

    [Fact]
    public void P95_of_1_to_100_seconds_is_95_seconds()
    {
        var latencies = Enumerable.Range(1, 100).Select(second => TimeSpan.FromSeconds(second));

        Assert.Equal(TimeSpan.FromSeconds(95), LatencyStatistics.Percentile(latencies, 95));
    }

    [Fact]
    public void Order_of_values_does_not_matter()
    {
        // nearest-rank: 5건 중 ceil(5 × 0.95) = 5번째 → 가장 느린 값
        Assert.Equal(TimeSpan.FromSeconds(9), LatencyStatistics.Percentile(Seconds(3, 9, 1, 2, 4), 95));
    }

    [Fact]
    public void Returns_null_when_there_are_no_values()
    {
        Assert.Null(LatencyStatistics.Percentile([], 95));
        Assert.Equal("측정값 없음", LatencyStatistics.FormatSeconds(null));
    }

    [Fact]
    public void Formats_seconds_with_two_decimal_places()
    {
        Assert.Equal("1.57초", LatencyStatistics.FormatSeconds(TimeSpan.FromMilliseconds(1570)));
    }
}
