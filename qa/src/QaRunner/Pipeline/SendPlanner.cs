namespace QaRunner.Pipeline;

/// <summary>
/// 보낼 이벤트 하나와 보낼 시각.
/// </summary>
/// <param name="Event">보낼 이벤트</param>
/// <param name="SendAt">이 시각이 되면 보낸다. 파이프라인 입장에서는 이 시각이 도착 시각이다</param>
public sealed record PlannedSend(PipelineEvent Event, DateTimeOffset SendAt);

/// <summary>
/// 시나리오를 보고 "어떤 이벤트를 언제, 몇 번, 어떤 순서로 보낼지" 전송 계획을 세운다.
/// </summary>
public static class SendPlanner
{
    /// <summary>
    /// 시나리오 하나의 전송 계획을 만든다.
    /// - 지연: 기다리지 않고 이벤트 시각을 그만큼 과거로 당겨서 만든다. (5분 지연 시험도 바로 끝난다)
    /// - 역순: 마지막 이벤트가 일어난 뒤에 모든 이벤트를 거꾸로 보낸다.
    /// - 중복: 같은 이벤트를 연달아 여러 번 보낸다.
    /// </summary>
    /// <param name="scenario">계획을 세울 시나리오</param>
    /// <param name="runId">이번 QA 실행 번호. 모든 이벤트에 붙는다</param>
    /// <param name="now">계획 기준 시각 (보통 현재 시각)</param>
    public static List<PlannedSend> BuildPlan(PipelineScenario scenario, string runId, DateTimeOffset now)
    {
        var injection = scenario.Injection;
        var delay = TimeSpan.FromSeconds(injection.DelaySeconds);

        // 지연 시간만큼 이벤트 시각을 과거로 당긴다. 그러면 지금 보내도 "늦게 도착한 이벤트"가 된다
        var firstEventTime = now - delay;

        var events = scenario.Events
            .OrderBy(scenarioEvent => scenarioEvent.OffsetSeconds)
            .Select(scenarioEvent => new PipelineEvent(
                RunId: runId,
                CaseId: scenario.CaseId,
                EventId: scenarioEvent.EventId,
                ScanId: scenarioEvent.ScanId,
                EventTime: firstEventTime.AddSeconds(scenarioEvent.OffsetSeconds),
                Verdict: scenarioEvent.Verdict))
            .ToList();

        if (events.Count == 0)
            return [];

        var lastEventTime = events.Max(pipelineEvent => pipelineEvent.EventTime);
        var sendOrder = injection.SendInReverseOrder ? Enumerable.Reverse(events) : events;
        var sendCountPerEvent = Math.Max(1, injection.DuplicateCount);

        return sendOrder
            .SelectMany(pipelineEvent => Enumerable.Repeat(pipelineEvent, sendCountPerEvent))
            .Select(pipelineEvent =>
            {
                // 역순이면 모두 마지막 이벤트 뒤에 보내고, 아니면 각 이벤트가 일어난 뒤에 보낸다
                var sendBaseTime = injection.SendInReverseOrder ? lastEventTime : pipelineEvent.EventTime;
                return new PlannedSend(pipelineEvent, sendBaseTime + delay);
            })
            .ToList();
    }
}
