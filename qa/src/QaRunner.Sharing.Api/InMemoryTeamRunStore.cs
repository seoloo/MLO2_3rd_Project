using System.Collections.Concurrent;
using QaRunner.Contracts;

namespace QaRunner.Sharing.Api;

/// <summary>서버가 켜진 동안만 유지하는 예제 저장소. 서버를 다시 켜면 기록이 사라진다.</summary>
public sealed class InMemoryTeamRunStore : ITeamRunStore
{
    private readonly ConcurrentDictionary<Guid, SharedQaRun> _runs = new();

    /// <summary>UUID를 키로 덮어써 재전송 중복을 막고 옛 스냅샷을 무시한다.</summary>
    public void Upsert(SharedQaRun run)
    {
        run.Validate();
        _runs.AddOrUpdate(run.RunId, run, (_, previous) => run.UpdatedAt >= previous.UpdatedAt ? run : previous);
    }

    public IReadOnlyList<SharedQaRun> List() => _runs.Values.OrderByDescending(run => run.StartedAt).ToArray();
    public SharedQaRun? Find(Guid runId) => _runs.GetValueOrDefault(runId);
}
