using System.Collections.Concurrent;
using QaRunner.Contracts;

namespace QaRunner.Collaboration;

/// <summary>한 프로세스 안에서만 결과를 공유하는 예제. 다른 PC와 통신하거나 영구 저장하지 않는다.</summary>
public sealed class DemoTeamRunClient : ITeamRunClient
{
    private readonly ConcurrentDictionary<Guid, SharedQaRun> _runs = new();

    public Task PublishAsync(SharedQaRun run, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        run.Validate();
        // 같은 실행을 다시 보내도 한 건으로 남고, 늦게 도착한 옛 상태로 되돌리지 않는다.
        _runs.AddOrUpdate(run.RunId, run, (_, previous) => run.UpdatedAt >= previous.UpdatedAt ? run : previous);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SharedQaRun>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<SharedQaRun>>(_runs.Values.OrderByDescending(run => run.StartedAt).ToArray());
    }

    public Task<SharedQaRun?> FindAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_runs.GetValueOrDefault(runId));
    }
}
