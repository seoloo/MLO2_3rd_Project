using QaRunner.Measurement;
using QaRunner.Pipeline;

namespace QaRunner.Tests;

/// <summary>
/// Gold 반영 추적기가 "늘어난 건수만큼 먼저 보낸 것부터 반영됨"으로 시간을 재는지 확인한다.
/// </summary>
public class GoldReflectionTrackerTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static PipelineEvent CreateEvent(int number) =>
        new("run-1", "LOAD", $"e{number}", $"s{number}", StartTime, "고위험");

    // 가짜 파이프라인에 보내고 추적기에도 기록한다 (EventStreamSender가 하는 일과 같음)
    private static async Task SendAndRecordAsync(
        FakeStreamingPipeline pipeline, GoldReflectionTracker tracker, ManualTimeProvider clock, int number)
    {
        await pipeline.SendAsync(CreateEvent(number), CancellationToken.None);
        tracker.RecordSent(CreateEvent(number), clock.GetUtcNow());
    }

    [Fact]
    public async Task Measures_time_from_send_until_gold_reflects_it()
    {
        var clock = new ManualTimeProvider(StartTime);
        var pipeline = new FakeStreamingPipeline(clock);
        var tracker = new GoldReflectionTracker(pipeline, clock);

        await SendAndRecordAsync(pipeline, tracker, clock, 1);
        clock.Advance(TimeSpan.FromSeconds(0.5));
        await tracker.CheckGoldOnceAsync(CancellationToken.None);
        Assert.Equal(0, tracker.ReflectedCount);

        clock.Advance(TimeSpan.FromSeconds(1));
        await tracker.CheckGoldOnceAsync(CancellationToken.None);

        // 처리 지연 1초가 지난 뒤 처음 확인한 시각(보낸 지 1.5초)에 반영된 것으로 기록된다
        Assert.Equal(1, tracker.ReflectedCount);
        Assert.True(tracker.HasCaughtUp);
        Assert.Equal(TimeSpan.FromSeconds(1.5), tracker.GetReflectionLatencies().Single());
    }

    [Fact]
    public async Task Knows_when_events_sent_before_a_point_were_all_reflected()
    {
        var clock = new ManualTimeProvider(StartTime);
        var pipeline = new FakeStreamingPipeline(clock);
        var tracker = new GoldReflectionTracker(pipeline, clock);

        await pipeline.StopProcessingAsync(CancellationToken.None);
        for (var number = 1; number <= 30; number++)
            await SendAndRecordAsync(pipeline, tracker, clock, number);

        clock.Advance(TimeSpan.FromSeconds(3));
        await pipeline.StartProcessingAsync(CancellationToken.None);
        var restartedAt = clock.GetUtcNow();

        // 0.5초마다 확인. 30건을 초당 20건으로 처리하므로 1.5초 뒤에 모두 반영된다
        for (var check = 0; check < 6; check++)
        {
            clock.Advance(TimeSpan.FromSeconds(0.5));
            await tracker.CheckGoldOnceAsync(CancellationToken.None);
        }

        Assert.Equal(TimeSpan.FromSeconds(1.5), tracker.GetTimeWhenFirstEventsReflected(30) - restartedAt);
        Assert.Null(tracker.GetTimeWhenFirstEventsReflected(31));
    }
}
