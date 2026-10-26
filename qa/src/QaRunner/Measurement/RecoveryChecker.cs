using QaRunner.Models;
using QaRunner.Pipeline;

namespace QaRunner.Measurement;

/// <summary>
/// 복구 시험 채점기. 실행안의 "입력 유지 + Spark 30초 중단 + 재가동 → 60초 안에 따라잡기"를 재현한다.
///
/// 순서:
/// 1. 이벤트를 초당 N건씩 계속 보낸다.
/// 2. 전체 시간의 4분의 1 지점에서 처리를 멈추고, 정해진 시간 뒤에 다시 켠다. (보내기는 계속)
/// 3. 회복 시간 = 재가동한 시각부터, 재가동 시점까지 보낸 이벤트가 모두 Gold에 반영된 시각까지.
/// 4. 보내기가 끝난 뒤 최종 Gold 반영 건수가 보낸 건수와 같은지도 본다 (최종 집계 일치).
/// </summary>
/// <param name="eventSender">이벤트를 넣을 곳</param>
/// <param name="pipelineReader">Gold 반영을 확인할 곳</param>
/// <param name="processingControl">처리를 멈추고 다시 켤 곳</param>
/// <param name="settings">측정 시간, 초당 건수, 중단 시간</param>
public sealed class RecoveryChecker(
    IEventSender eventSender,
    IPipelineReader pipelineReader,
    IProcessingControl processingControl,
    MeasurementSettings settings)
{
    /// <summary>회복 시간 목표 (실행안 초기 목표)</summary>
    public static readonly TimeSpan RecoveryTimeTarget = TimeSpan.FromSeconds(60);

    /// <summary>보내기가 끝난 뒤 남은 이벤트가 Gold에 반영되기를 기다리는 최대 시간</summary>
    public static readonly TimeSpan CatchUpTimeout = TimeSpan.FromSeconds(60);

    public const string CaseId = "RECOVERY";

    // 처리를 멈췄다 다시 켠 기록: 재가동 시각, 그 시점까지 보낸 건수, 제어 실패 이유
    private sealed record StopAndRestartResult(DateTimeOffset? RestartedAt, int SentCountAtRestart, string? ControlError);

    /// <summary>
    /// 이벤트를 보내면서 처리를 멈췄다 다시 켜고, 회복 시간과 최종 집계를 돌려준다.
    /// </summary>
    /// <param name="runId">이번 QA 실행 번호</param>
    public async Task<List<TestResult>> CheckAllAsync(string runId, CancellationToken cancellationToken = default)
    {
        var reflectionTracker = new GoldReflectionTracker(pipelineReader);

        var streamTask = EventStreamSender.SendAtFixedRateAsync(
            eventSender, reflectionTracker, runId, CaseId,
            settings.EventsPerSecond, TimeSpan.FromSeconds(settings.DurationSeconds), cancellationToken);
        var stopAndRestartTask = StopAndRestartProcessingAsync(reflectionTracker, cancellationToken);

        // 보내기와 중단·재가동이 모두 끝나야 따라잡기 대기를 시작한다
        var trackingTask = reflectionTracker.TrackUntilCaughtUpAsync(
            Task.WhenAll(streamTask, stopAndRestartTask), CatchUpTimeout, GoldReflectionTracker.DefaultPollInterval, cancellationToken);

        await Task.WhenAll(streamTask, stopAndRestartTask, trackingTask);

        return BuildResults(await streamTask, await stopAndRestartTask, reflectionTracker);
    }

    // 정해진 시점에 처리를 멈추고, 중단 시간이 지나면 다시 켠다
    private async Task<StopAndRestartResult> StopAndRestartProcessingAsync(
        GoldReflectionTracker reflectionTracker, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(settings.StopAfterSeconds), cancellationToken);
            await processingControl.StopProcessingAsync(cancellationToken);

            await Task.Delay(TimeSpan.FromSeconds(settings.StopSeconds), cancellationToken);
            await processingControl.StartProcessingAsync(cancellationToken);

            return new StopAndRestartResult(DateTimeOffset.UtcNow, reflectionTracker.SentCount, ControlError: null);
        }
        catch (ProcessingControlException exception)
        {
            return new StopAndRestartResult(RestartedAt: null, SentCountAtRestart: 0, exception.Message);
        }
    }

    private List<TestResult> BuildResults(
        StreamSendResult streamSendResult, StopAndRestartResult stopAndRestartResult, GoldReflectionTracker reflectionTracker)
    {
        var input = $"초당 {settings.EventsPerSecond:0.##}건 · {settings.DurationSeconds}초 발행 · " +
                    $"{settings.StopAfterSeconds}초 지점에서 {settings.StopSeconds}초 중단 · 이벤트 {streamSendResult.SentCount}건";

        const string recoveryCheckName = "재가동 후 회복 시간";
        const string finalCountCheckName = "최종 Gold 반영 건수 (집계 일치)";
        var recoveryExpected = $"{RecoveryTimeTarget.TotalSeconds:0}초 이하";

        // 보내기나 중단·재가동 자체가 안 됐다면 측정할 수 없으므로 시스템 실패로 남긴다
        var setupError = streamSendResult.SendError ?? stopAndRestartResult.ControlError;
        if (setupError is not null)
        {
            return
            [
                TestResult.SystemFailure(CaseId, recoveryCheckName, input, recoveryExpected, setupError),
                TestResult.SystemFailure(CaseId, finalCountCheckName, input, reflectionTracker.SentCount.ToString(), setupError),
            ];
        }

        var recoveredAt = reflectionTracker.GetTimeWhenFirstEventsReflected(stopAndRestartResult.SentCountAtRestart);
        TimeSpan? recoveryTime = recoveredAt - stopAndRestartResult.RestartedAt;

        return
        [
            new TestResult(CaseId, recoveryCheckName, input,
                Expected: recoveryExpected,
                Actual: recoveryTime is null ? "제한 시간 안에 회복 못 함" : LatencyStatistics.FormatSeconds(recoveryTime),
                MeetsTarget: recoveryTime <= RecoveryTimeTarget),
            new TestResult(CaseId, finalCountCheckName, input,
                Expected: reflectionTracker.SentCount.ToString(),
                Actual: reflectionTracker.ReflectedCount.ToString()),
        ];
    }
}
