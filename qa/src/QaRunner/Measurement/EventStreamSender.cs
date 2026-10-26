using QaRunner.Pipeline;

namespace QaRunner.Measurement;

/// <summary>
/// 이벤트를 일정한 속도로 계속 보낸 결과.
/// </summary>
/// <param name="SentCount">보낸 건수</param>
/// <param name="SendError">보내다가 실패했으면 그 이유. 실패하면 거기서 멈춘다. 정상이면 null</param>
public sealed record StreamSendResult(int SentCount, string? SendError);

/// <summary>
/// 부하·복구 시험에서 "초당 N건씩 정해진 시간 동안" 이벤트를 보낸다.
/// </summary>
public static class EventStreamSender
{
    // 보내는 이벤트의 판정 값을 돌아가며 섞는다 (집계에 여러 판정이 고르게 들어가도록)
    private static readonly string[] VerdictsInRotation = ["고위험", "주의", "신호 미발견", "판단 보류"];

    /// <summary>
    /// 정해진 속도로 이벤트를 보내고, 보낼 때마다 반영 추적기에 기록한다.
    /// i번째 이벤트는 시작 시각 + i / 초당 건수 시점에 보낸다. 늦어졌으면 기다리지 않고 바로 보낸다.
    /// </summary>
    /// <param name="eventSender">이벤트를 넣을 곳</param>
    /// <param name="reflectionTracker">보낸 기록을 남길 반영 추적기</param>
    /// <param name="runId">이번 QA 실행 번호</param>
    /// <param name="caseId">이벤트에 붙일 케이스 번호. 예: LOAD-STREAM</param>
    /// <param name="eventsPerSecond">1초에 보낼 건수</param>
    /// <param name="duration">보내는 시간</param>
    public static async Task<StreamSendResult> SendAtFixedRateAsync(
        IEventSender eventSender,
        GoldReflectionTracker reflectionTracker,
        string runId,
        string caseId,
        double eventsPerSecond,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var sendInterval = TimeSpan.FromSeconds(1 / eventsPerSecond);
        var sentCount = 0;

        while (true)
        {
            var plannedSendAt = startedAt + sendInterval * sentCount;
            if (plannedSendAt >= startedAt + duration)
                return new StreamSendResult(sentCount, SendError: null);

            var waitTime = plannedSendAt - DateTimeOffset.UtcNow;
            if (waitTime > TimeSpan.Zero)
                await Task.Delay(waitTime, cancellationToken);

            var sentAt = DateTimeOffset.UtcNow;
            var pipelineEvent = new PipelineEvent(
                RunId: runId,
                CaseId: caseId,
                EventId: $"{caseId}-{runId}-{sentCount + 1:000000}",
                ScanId: $"{caseId}-scan-{sentCount + 1:000000}",
                EventTime: sentAt,
                Verdict: VerdictsInRotation[sentCount % VerdictsInRotation.Length]);

            try
            {
                await eventSender.SendAsync(pipelineEvent, cancellationToken);
            }
            catch (EventSendException exception)
            {
                return new StreamSendResult(sentCount, exception.Message);
            }

            reflectionTracker.RecordSent(pipelineEvent, sentAt);
            sentCount++;
        }
    }
}
