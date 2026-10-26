using QaRunner.Pipeline;

namespace QaRunner.Tests;

/// <summary>
/// 정답 계산기가 실행안의 규칙(중복 제거, 5분 초과 격리, 같은 ID 상충 격리, 60초 구간)대로 계산하는지 확인한다.
/// </summary>
public class ExpectedResultCalculatorTests
{
    // 09:00:10 (UTC). 이 시각이 속한 Gold 구간은 09:00:00
    private static readonly DateTimeOffset EventTime = new(2026, 11, 2, 9, 0, 10, TimeSpan.Zero);

    private static PipelineEvent CreateEvent(string eventId, DateTimeOffset eventTime, string verdict = "고위험") =>
        new("run-1", "case-1", eventId, $"scan-{eventId}", eventTime, verdict);

    [Fact]
    public void Duplicates_stay_in_bronze_but_count_once_in_silver_and_gold()
    {
        var expected = new ExpectedResultCalculator();
        var sameEvent = CreateEvent("e1", EventTime);
        for (var sendCount = 0; sendCount < 3; sendCount++)
            expected.Record(sameEvent, EventTime.AddSeconds(sendCount));

        Assert.Equal(3, expected.BronzeCount);
        Assert.Equal(1, expected.SilverCount);
        Assert.Equal(0, expected.QuarantineCount);
        Assert.Equal(1, expected.GoldCountsByWindow[ExpectedResultCalculator.GetWindowStart(EventTime)]);
    }

    [Fact]
    public void Arrival_after_five_minutes_is_quarantined_and_not_counted_in_gold()
    {
        var expected = new ExpectedResultCalculator();
        expected.Record(CreateEvent("e1", EventTime), arrivedAt: EventTime.AddMinutes(6));

        Assert.Equal(1, expected.BronzeCount);
        Assert.Equal(0, expected.SilverCount);
        Assert.Equal(1, expected.QuarantineCount);
        Assert.Empty(expected.GoldCountsByWindow);
    }

    [Fact]
    public void Thirty_second_delay_is_still_counted_in_original_window()
    {
        var expected = new ExpectedResultCalculator();
        expected.Record(CreateEvent("e1", EventTime), arrivedAt: EventTime.AddSeconds(30));

        Assert.Equal(0, expected.QuarantineCount);
        Assert.Equal(1, expected.GoldCountsByWindow[ExpectedResultCalculator.GetWindowStart(EventTime)]);
    }

    [Fact]
    public void Same_id_with_different_content_keeps_first_and_quarantines_later_one()
    {
        var expected = new ExpectedResultCalculator();
        expected.Record(CreateEvent("e1", EventTime, "고위험"), EventTime);
        expected.Record(CreateEvent("e1", EventTime, "주의"), EventTime.AddSeconds(1));

        Assert.Equal(2, expected.BronzeCount);
        Assert.Equal(1, expected.SilverCount);
        Assert.Equal(1, expected.QuarantineCount);
    }

    [Fact]
    public void Events_are_grouped_into_60_second_windows()
    {
        var expected = new ExpectedResultCalculator();
        expected.Record(CreateEvent("e1", EventTime), EventTime);                                   // 09:00:10 → 09:00 구간
        expected.Record(CreateEvent("e2", EventTime.AddSeconds(49)), EventTime.AddSeconds(49));     // 09:00:59 → 09:00 구간
        expected.Record(CreateEvent("e3", EventTime.AddSeconds(50)), EventTime.AddSeconds(50));     // 09:01:00 → 09:01 구간

        Assert.Equal(2, expected.GoldCountsByWindow[ExpectedResultCalculator.GetWindowStart(EventTime)]);
        Assert.Equal(1, expected.GoldCountsByWindow[ExpectedResultCalculator.GetWindowStart(EventTime.AddSeconds(50))]);
    }

    [Fact]
    public void Window_start_drops_seconds()
    {
        var windowStart = ExpectedResultCalculator.GetWindowStart(EventTime);

        Assert.Equal(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero), windowStart);
    }
}
