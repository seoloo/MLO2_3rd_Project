namespace QaRunner.Pipeline;

/// <summary>
/// 정답 계산기. 파이프라인 코드를 쓰지 않고, QA 러너가 "보낸 기록"만 보고
/// Bronze·Silver·Gold에 있어야 할 값을 직접 계산한다 (실행안의 독립 정답).
///
/// 계산 규칙 (실행안 기준, 일부는 C와 확정 전 임시안):
/// - Bronze: 받은 이벤트를 모두 보관한다. 중복으로 보낸 것도 각각 남는다.
/// - Silver: 같은 event_id는 한 번만 남긴다.
/// - 격리: 이벤트가 일어난 뒤 5분을 넘겨 도착했거나, 같은 event_id인데 내용이 다르면 격리한다.
///         (임시안: 먼저 도착한 내용을 기준으로 삼고 뒤에 온 다른 내용을 격리)
/// - Gold: Silver에 남은 이벤트를 이벤트 시각 기준 60초 구간별로 센다.
/// </summary>
public sealed class ExpectedResultCalculator
{
    /// <summary>이벤트가 일어난 뒤 이 시간보다 늦게 도착하면 격리한다</summary>
    public static readonly TimeSpan LateArrivalLimit = TimeSpan.FromMinutes(5);

    /// <summary>Gold 집계 구간 길이(초)</summary>
    public const int GoldWindowSeconds = 60;

    // 보낸 기록: 어떤 이벤트가 언제 도착했는지
    private readonly List<(PipelineEvent Event, DateTimeOffset ArrivedAt)> _sentEvents = [];

    /// <summary>
    /// 보낸 이벤트 하나를 기록한다. 전송 계획의 순서대로 기록해야 한다.
    /// </summary>
    /// <param name="pipelineEvent">보낸 이벤트</param>
    /// <param name="arrivedAt">파이프라인에 도착하는 시각 (= 보낸 시각)</param>
    public void Record(PipelineEvent pipelineEvent, DateTimeOffset arrivedAt) =>
        _sentEvents.Add((pipelineEvent, arrivedAt));

    /// <summary>Bronze에 있어야 할 건수: 보낸 횟수 전부</summary>
    public int BronzeCount => _sentEvents.Count;

    /// <summary>Silver에 있어야 할 고유 이벤트 수</summary>
    public int SilverCount => ClassifyEvents().AcceptedEvents.Count;

    /// <summary>격리되어야 할 건수</summary>
    public int QuarantineCount => ClassifyEvents().QuarantinedCount;

    /// <summary>
    /// Gold에 있어야 할 값: 60초 구간 시작 시각 → 그 구간의 고유 이벤트 수
    /// </summary>
    public IReadOnlyDictionary<DateTimeOffset, int> GoldCountsByWindow =>
        ClassifyEvents().AcceptedEvents
            .GroupBy(acceptedEvent => GetWindowStart(acceptedEvent.EventTime))
            .ToDictionary(window => window.Key, window => window.Count());

    /// <summary>
    /// 시각이 속한 60초 구간의 시작 시각을 구한다. 예: 14:30:42 → 14:30:00 (UTC 기준)
    /// </summary>
    public static DateTimeOffset GetWindowStart(DateTimeOffset time)
    {
        var unixSeconds = time.ToUnixTimeSeconds();
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds - unixSeconds % GoldWindowSeconds);
    }

    // 보낸 이벤트를 도착 순서대로 보면서 Silver에 남을 것과 격리될 것을 나눈다
    private (List<PipelineEvent> AcceptedEvents, int QuarantinedCount) ClassifyEvents()
    {
        var acceptedEventsById = new Dictionary<string, PipelineEvent>();
        var quarantinedCount = 0;

        // OrderBy는 같은 시각끼리 기록 순서를 유지한다 (중복·역순 전송 순서가 그대로 지켜짐)
        foreach (var (sentEvent, arrivedAt) in _sentEvents.OrderBy(sent => sent.ArrivedAt))
        {
            var isLateArrival = arrivedAt - sentEvent.EventTime > LateArrivalLimit;
            if (isLateArrival)
            {
                quarantinedCount++;
                continue;
            }

            if (acceptedEventsById.TryGetValue(sentEvent.EventId, out var firstArrivedEvent))
            {
                // 같은 내용이면 단순 중복이라 버리고, 내용이 다르면 상충이라 격리한다
                if (firstArrivedEvent != sentEvent)
                    quarantinedCount++;
                continue;
            }

            acceptedEventsById[sentEvent.EventId] = sentEvent;
        }

        return (acceptedEventsById.Values.ToList(), quarantinedCount);
    }
}
