using QaRunner.Contracts;

namespace QaRunner.Sharing.Api;

/// <summary>서버 저장 경계. 프로젝트 착수 후 파일·DB 저장소로 교체한다.</summary>
public interface ITeamRunStore
{
    void Upsert(SharedQaRun run);
    IReadOnlyList<SharedQaRun> List();
    SharedQaRun? Find(Guid runId);
}
