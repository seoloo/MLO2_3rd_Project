using QaRunner.Pipeline;

namespace QaRunner.Tests;

/// <summary>
/// 부하·복구 시험용 가짜 파이프라인이 처리 지연, 처리 속도 한계, 중단·재가동을 제대로 흉내 내는지 확인한다.
/// 시계를 직접 움직이므로 실제로 기다리지 않는다.
/// </summary>
public class FakeStreamingPipelineTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static PipelineEvent CreateEvent(int number) =>
        new("run-1", "LOAD", $"e{number}", $"s{number}", StartTime, "고위험");

    // 시작 구간(09:00)의 Gold 건수를 읽는다
    private static async Task<int> ReadGoldCountAsync(FakeStreamingPipeline pipeline)
    {
        var goldCounts = await pipeline.ReadGoldCountsAsync([StartTime], CancellationToken.None);
        return goldCounts[StartTime];
    }

    [Fact]
    public async Task Event_reaches_gold_only_after_processing_delay()
    {
        var clock = new ManualTimeProvider(StartTime);
        var pipeline = new FakeStreamingPipeline(clock);
        await pipeline.SendAsync(CreateEvent(1), CancellationToken.None);

        clock.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(0, await ReadGoldCountAsync(pipeline));

        clock.Advance(TimeSpan.FromSeconds(0.6));
        Assert.Equal(1, await ReadGoldCountAsync(pipeline));
    }

    [Fact]
    public async Task Stopped_pipeline_keeps_receiving_but_does_not_process()
    {
        var clock = new ManualTimeProvider(StartTime);
        var pipeline = new FakeStreamingPipeline(clock);
        await pipeline.StopProcessingAsync(CancellationToken.None);
        await pipeline.SendAsync(CreateEvent(1), CancellationToken.None);

        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(0, await ReadGoldCountAsync(pipeline));
        var caseCounts = await pipeline.ReadCaseCountsAsync("run-1", "LOAD", CancellationToken.None);
        Assert.Equal(1, caseCounts.BronzeCount);
    }

    [Fact]
    public async Task Backlog_is_processed_at_max_speed_after_restart()
    {
        var clock = new ManualTimeProvider(StartTime);
        var pipeline = new FakeStreamingPipeline(clock);
        await pipeline.StopProcessingAsync(CancellationToken.None);
        for (var number = 1; number <= 40; number++)
            await pipeline.SendAsync(CreateEvent(number), CancellationToken.None);

        clock.Advance(TimeSpan.FromSeconds(5));
        await pipeline.StartProcessingAsync(CancellationToken.None);

        // 초당 20건까지만 처리하므로 재가동 1초 뒤에는 20건, 2초 뒤에는 40건 모두 반영
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(FakeStreamingPipeline.MaxEventsPerSecond, await ReadGoldCountAsync(pipeline));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(40, await ReadGoldCountAsync(pipeline));
    }
}
