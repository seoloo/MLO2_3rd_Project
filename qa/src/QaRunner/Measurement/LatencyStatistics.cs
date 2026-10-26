namespace QaRunner.Measurement;

/// <summary>
/// 걸린 시간 목록에서 p95 같은 통계를 구한다.
/// </summary>
public static class LatencyStatistics
{
    /// <summary>
    /// 백분위 값을 구한다. p95는 "100건 중 95번째로 느린 건의 시간", 즉 95%가 이 시간 안에 끝났다는 뜻이다.
    /// 계산 방식은 nearest-rank: 오름차순으로 줄 세운 뒤 ceil(n × p / 100)번째 값을 쓴다.
    /// 값이 하나도 없으면 null을 돌려준다.
    /// </summary>
    /// <param name="latencies">걸린 시간 목록</param>
    /// <param name="percentile">구할 백분위. 예: 95</param>
    public static TimeSpan? Percentile(IEnumerable<TimeSpan> latencies, double percentile)
    {
        var sortedLatencies = latencies.Order().ToList();
        if (sortedLatencies.Count == 0)
            return null;

        var rank = (int)Math.Ceiling(sortedLatencies.Count * percentile / 100.0);
        var index = Math.Clamp(rank - 1, 0, sortedLatencies.Count - 1);
        return sortedLatencies[index];
    }

    /// <summary>
    /// 보고서에 쓸 시간 표기. 예: 1.23초. 값이 없으면 "측정값 없음"
    /// </summary>
    public static string FormatSeconds(TimeSpan? latency) =>
        latency is null ? "측정값 없음" : $"{latency.Value.TotalSeconds:0.00}초";
}
