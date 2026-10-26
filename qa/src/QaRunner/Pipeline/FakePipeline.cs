namespace QaRunner.Pipeline;

/// <summary>
/// C의 파이프라인이 준비되기 전에 쓰는 메모리 안의 가짜 파이프라인.
/// 이벤트를 받는 쪽(IEventSender)과 결과를 읽는 쪽(IPipelineReader)을 함께 흉내 낸다.
///
/// 판정 시험의 FakeVerdictService처럼 일부러 엉성하게 만들었다.
/// 중복 제거는 하지만 "5분 초과 도착"과 "같은 ID 상충"을 격리하지 않으므로,
/// 채점기가 이 차이를 실패로 잡아내는지 확인할 수 있다.
/// </summary>
public sealed class FakePipeline : IEventSender, IPipelineReader
{
    // Bronze 역할: 받은 이벤트를 받은 순서대로 모두 보관
    private readonly List<PipelineEvent> _receivedEvents = [];

    public Task SendAsync(PipelineEvent pipelineEvent, CancellationToken cancellationToken)
    {
        _receivedEvents.Add(pipelineEvent);
        return Task.CompletedTask;
    }

    public Task<CaseCounts> ReadCaseCountsAsync(string runId, string caseId, CancellationToken cancellationToken)
    {
        var caseEvents = _receivedEvents
            .Where(receivedEvent => receivedEvent.RunId == runId && receivedEvent.CaseId == caseId)
            .ToList();

        var caseCounts = new CaseCounts(
            BronzeCount: caseEvents.Count,
            SilverCount: caseEvents.Select(caseEvent => caseEvent.EventId).Distinct().Count(),
            QuarantineCount: 0);

        return Task.FromResult(caseCounts);
    }

    public Task<IReadOnlyDictionary<DateTimeOffset, int>> ReadGoldCountsAsync(
        IReadOnlyCollection<DateTimeOffset> windowStarts, CancellationToken cancellationToken)
    {
        // Silver 역할: event_id별로 처음 받은 것 하나만 남긴다
        var silverEvents = _receivedEvents
            .GroupBy(receivedEvent => receivedEvent.EventId)
            .Select(sameIdEvents => sameIdEvents.First());

        var countsByWindow = silverEvents
            .GroupBy(silverEvent => ExpectedResultCalculator.GetWindowStart(silverEvent.EventTime))
            .ToDictionary(window => window.Key, window => window.Count());

        IReadOnlyDictionary<DateTimeOffset, int> requestedCounts = windowStarts.ToDictionary(
            windowStart => windowStart,
            windowStart => countsByWindow.GetValueOrDefault(windowStart));

        return Task.FromResult(requestedCounts);
    }
}
