namespace QaRunner.Contracts;

/// <summary>데스크톱 앱이 공용 서버에 상태·결과를 보내고 팀 기록을 읽는 경계.</summary>
public interface ITeamRunClient
{
    Task PublishAsync(SharedQaRun run, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SharedQaRun>> ListAsync(CancellationToken cancellationToken = default);
    Task<SharedQaRun?> FindAsync(Guid runId, CancellationToken cancellationToken = default);
}
