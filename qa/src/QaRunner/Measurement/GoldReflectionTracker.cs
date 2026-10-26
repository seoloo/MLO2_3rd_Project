using QaRunner.Pipeline;

namespace QaRunner.Measurement;

/// <summary>
/// 보낸 이벤트가 Gold에 언제 반영됐는지 기록한다. 부하·복구 시험의 시간 측정이 모두 여기서 나온다.
///
/// 측정 방법: 일정 간격으로 Gold 집계 건수를 읽고, 건수가 늘어난 만큼 "먼저 보낸 이벤트부터" 반영된 것으로 본다.
/// 이벤트 하나하나를 따로 찾지 않아도 되므로 실제 MongoDB에서도 같은 방식으로 잴 수 있다.
/// 처리 순서가 보낸 순서와 거의 같다는 가정이 들어 있어서, 개별 시간은 근삿값이고 p95 같은 통계로 본다.
/// 다른 데이터가 Gold에 섞이면 안 되므로 시험 환경에서만 쓴다.
/// </summary>
/// <param name="pipelineReader">Gold 집계를 읽을 곳</param>
/// <param name="timeProvider">현재 시각을 알려 주는 곳 (테스트에서 시각을 직접 움직이기 위함)</param>
public sealed class GoldReflectionTracker(IPipelineReader pipelineReader, TimeProvider? timeProvider = null)
{
    /// <summary>Gold를 다시 읽기 전에 쉬는 시간. 측정 정밀도가 이 간격에 따라 정해진다</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly object _stateLock = new();

    // 보낸 순서대로: 보낸 시각과 Gold에 반영된 것을 확인한 시각(아직이면 null)
    private readonly List<SentEventRecord> _sentEvents = [];

    // 보낸 이벤트들이 걸친 Gold 구간 (이 구간들의 건수를 합쳐 반영 건수를 센다)
    private readonly HashSet<DateTimeOffset> _goldWindows = [];

    private int _reflectedCount;

    private sealed class SentEventRecord(DateTimeOffset sentAt)
    {
        public DateTimeOffset SentAt { get; } = sentAt;
        public DateTimeOffset? ReflectedAt { get; set; }
    }

    /// <summary>지금까지 보낸 건수</summary>
    public int SentCount { get { lock (_stateLock) return _sentEvents.Count; } }

    /// <summary>Gold에 반영된 것을 확인한 건수</summary>
    public int ReflectedCount { get { lock (_stateLock) return _reflectedCount; } }

    /// <summary>보낸 것이 모두 Gold에 반영됐는지</summary>
    public bool HasCaughtUp { get { lock (_stateLock) return _reflectedCount >= _sentEvents.Count; } }

    /// <summary>
    /// 이벤트를 보냈다고 기록한다. 보낸 직후에 불러야 한다.
    /// </summary>
    public void RecordSent(PipelineEvent pipelineEvent, DateTimeOffset sentAt)
    {
        lock (_stateLock)
        {
            _sentEvents.Add(new SentEventRecord(sentAt));
            _goldWindows.Add(ExpectedResultCalculator.GetWindowStart(pipelineEvent.EventTime));
        }
    }

    /// <summary>
    /// Gold 집계를 한 번 읽고, 늘어난 건수만큼 먼저 보낸 이벤트부터 "지금 반영됨"으로 기록한다.
    /// </summary>
    public async Task CheckGoldOnceAsync(CancellationToken cancellationToken)
    {
        List<DateTimeOffset> windowsToRead;
        lock (_stateLock)
            windowsToRead = _goldWindows.ToList();

        if (windowsToRead.Count == 0)
            return;

        var goldCounts = await pipelineReader.ReadGoldCountsAsync(windowsToRead, cancellationToken);
        var totalGoldCount = goldCounts.Values.Sum();
        var checkedAt = _timeProvider.GetUtcNow();

        lock (_stateLock)
        {
            var newReflectedCount = Math.Min(totalGoldCount, _sentEvents.Count);
            for (var index = _reflectedCount; index < newReflectedCount; index++)
                _sentEvents[index].ReflectedAt = checkedAt;

            _reflectedCount = Math.Max(_reflectedCount, newReflectedCount);
        }
    }

    /// <summary>
    /// 보내기 작업이 끝날 때까지 Gold를 계속 확인하고, 끝난 뒤에는 모두 반영되거나 제한 시간이 될 때까지 더 확인한다.
    /// </summary>
    /// <param name="sendingTask">보내기(와 장애 주입) 작업. 이 작업이 끝나야 따라잡기 대기를 시작한다</param>
    /// <param name="catchUpTimeout">보내기가 끝난 뒤 따라잡기를 기다리는 최대 시간</param>
    /// <param name="pollInterval">Gold를 다시 읽는 간격</param>
    public async Task TrackUntilCaughtUpAsync(
        Task sendingTask, TimeSpan catchUpTimeout, TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        DateTimeOffset? catchUpDeadline = null;

        while (true)
        {
            await CheckGoldOnceAsync(cancellationToken);

            if (sendingTask.IsCompleted)
            {
                catchUpDeadline ??= _timeProvider.GetUtcNow() + catchUpTimeout;
                if (HasCaughtUp || _timeProvider.GetUtcNow() >= catchUpDeadline)
                    return;
            }

            await Task.Delay(pollInterval, cancellationToken);
        }
    }

    /// <summary>
    /// 반영된 이벤트마다 "보낸 뒤 Gold에 반영되기까지 걸린 시간" 목록
    /// </summary>
    public IReadOnlyList<TimeSpan> GetReflectionLatencies()
    {
        lock (_stateLock)
        {
            return _sentEvents
                .Where(sentEvent => sentEvent.ReflectedAt is not null)
                .Select(sentEvent => sentEvent.ReflectedAt!.Value - sentEvent.SentAt)
                .ToList();
        }
    }

    /// <summary>
    /// 처음 보낸 eventCount건이 모두 Gold에 반영된 것을 확인한 시각. 아직이면 null.
    /// 복구 시험에서 "재가동 시점까지 보낸 이벤트를 모두 따라잡은 시각"을 구하는 데 쓴다.
    /// </summary>
    public DateTimeOffset? GetTimeWhenFirstEventsReflected(int eventCount)
    {
        lock (_stateLock)
        {
            if (eventCount <= 0 || eventCount > _reflectedCount)
                return null;

            return _sentEvents[eventCount - 1].ReflectedAt;
        }
    }
}
