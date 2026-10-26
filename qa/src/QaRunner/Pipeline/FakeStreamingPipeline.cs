namespace QaRunner.Pipeline;

/// <summary>
/// 부하·복구 시험용 가짜 스트리밍 파이프라인. 실제 파이프라인처럼 "처리에 시간이 걸리는" 모습을 흉내 낸다.
/// - 받은 이벤트는 ProcessingDelay(1초)가 지나야 Gold에 반영된다.
/// - 1초에 MaxEventsPerSecond(20건)까지만 처리한다. 그보다 많이 쌓이면 적체가 생긴다.
/// - StopProcessingAsync로 멈추면 받기만 하고 처리하지 않는다 (Spark 중단 흉내).
///
/// 뒤에서 따로 도는 스레드는 없다. 보내거나 읽을 때마다 "지금 시각까지 처리됐어야 할 만큼"을
/// 0.1초 단위로 계산해서 반영한다. 그래서 시각을 직접 넣어 주는 테스트에서도 결과가 항상 같다.
/// </summary>
/// <param name="timeProvider">현재 시각을 알려 주는 곳. 테스트에서는 시각을 직접 움직이는 것을 넣는다</param>
public sealed class FakeStreamingPipeline(TimeProvider? timeProvider = null)
    : IEventSender, IPipelineReader, IProcessingControl
{
    /// <summary>이벤트를 받은 뒤 Gold에 반영되기까지 걸리는 시간</summary>
    public static readonly TimeSpan ProcessingDelay = TimeSpan.FromSeconds(1);

    /// <summary>1초에 처리할 수 있는 최대 건수. 멈췄다가 다시 켜면 이 속도로 따라잡는다</summary>
    public const int MaxEventsPerSecond = 20;

    // 처리 상태를 계산하는 시간 간격과, 그 간격마다 처리할 수 있는 건수
    private static readonly TimeSpan SimulationStep = TimeSpan.FromMilliseconds(100);
    private const int MaxEventsPerStep = MaxEventsPerSecond / 10;

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    // 부하 시험은 보내기와 읽기가 동시에 일어나므로 상태를 바꿀 때는 항상 잠근다
    private readonly object _stateLock = new();

    private readonly List<PipelineEvent> _receivedEvents = [];                            // Bronze 역할
    private readonly Queue<(PipelineEvent Event, DateTimeOffset ReceivedAt)> _waitingEvents = new(); // 처리 대기 줄
    private readonly List<PipelineEvent> _processedEvents = [];                           // Silver·Gold 역할

    private DateTimeOffset? _simulatedUntil;   // 어느 시각까지 처리 상태를 계산해 두었는지
    private bool _isProcessingStopped;

    public Task SendAsync(PipelineEvent pipelineEvent, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            var now = _timeProvider.GetUtcNow();
            AdvanceProcessingTo(now);
            _receivedEvents.Add(pipelineEvent);
            _waitingEvents.Enqueue((pipelineEvent, now));
        }
        return Task.CompletedTask;
    }

    public Task StopProcessingAsync(CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            AdvanceProcessingTo(_timeProvider.GetUtcNow());
            _isProcessingStopped = true;
        }
        return Task.CompletedTask;
    }

    public Task StartProcessingAsync(CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            AdvanceProcessingTo(_timeProvider.GetUtcNow());
            _isProcessingStopped = false;
        }
        return Task.CompletedTask;
    }

    public Task<CaseCounts> ReadCaseCountsAsync(string runId, string caseId, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            AdvanceProcessingTo(_timeProvider.GetUtcNow());

            bool BelongsToCase(PipelineEvent pipelineEvent) => pipelineEvent.RunId == runId && pipelineEvent.CaseId == caseId;
            var caseCounts = new CaseCounts(
                BronzeCount: _receivedEvents.Count(BelongsToCase),
                SilverCount: _processedEvents.Where(BelongsToCase).Select(processed => processed.EventId).Distinct().Count(),
                QuarantineCount: 0);

            return Task.FromResult(caseCounts);
        }
    }

    public Task<IReadOnlyDictionary<DateTimeOffset, int>> ReadGoldCountsAsync(
        IReadOnlyCollection<DateTimeOffset> windowStarts, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            AdvanceProcessingTo(_timeProvider.GetUtcNow());

            var countsByWindow = _processedEvents
                .DistinctBy(processed => processed.EventId)
                .GroupBy(processed => ExpectedResultCalculator.GetWindowStart(processed.EventTime))
                .ToDictionary(window => window.Key, window => window.Count());

            IReadOnlyDictionary<DateTimeOffset, int> requestedCounts = windowStarts.ToDictionary(
                windowStart => windowStart,
                windowStart => countsByWindow.GetValueOrDefault(windowStart));

            return Task.FromResult(requestedCounts);
        }
    }

    // 마지막으로 계산한 시각부터 now까지 0.1초씩 나아가며, 그동안 처리됐어야 할 이벤트를 처리 완료로 옮긴다.
    // 멈춘 동안의 구간은 건너뛰고, 한 구간에 처리할 수 있는 건수를 넘으면 다음 구간으로 미룬다(적체).
    private void AdvanceProcessingTo(DateTimeOffset now)
    {
        _simulatedUntil ??= now;

        while (_simulatedUntil.Value + SimulationStep <= now)
        {
            var stepEnd = _simulatedUntil.Value + SimulationStep;
            _simulatedUntil = stepEnd;

            if (_isProcessingStopped)
                continue;

            var processedInThisStep = 0;
            while (processedInThisStep < MaxEventsPerStep
                   && _waitingEvents.Count > 0
                   && _waitingEvents.Peek().ReceivedAt + ProcessingDelay <= stepEnd)
            {
                _processedEvents.Add(_waitingEvents.Dequeue().Event);
                processedInThisStep++;
            }
        }
    }
}
