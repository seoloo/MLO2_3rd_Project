using QaRunner.Contracts;

namespace QaRunner.Sharing.Api;

/// <summary>데스크톱 앱과 공유할 API 초안. 원격 실행과 Runpod 호출은 이 서버의 현재 역할에 포함하지 않는다.</summary>
public static class TeamRunEndpoints
{
    /// <summary>상태·결과 업로드, 팀 실행 목록, 실행 상세의 최소 경로를 등록한다.</summary>
    public static void MapTeamRunEndpoints(this WebApplication app)
    {
        app.MapGet("/qa/runs", (ITeamRunStore store) => Results.Ok(store.List()));
        app.MapGet("/qa/runs/{runId:guid}", (Guid runId, ITeamRunStore store) =>
            store.Find(runId) is { } run ? Results.Ok(run) : Results.NotFound());

        app.MapPut("/qa/runs/{runId:guid}", (Guid runId, SharedQaRun run, ITeamRunStore store) =>
        {
            if (runId != run.RunId)
                return Results.BadRequest(new { error = "경로와 본문의 실행 ID가 다릅니다." });
            try
            {
                store.Upsert(run);
                return Results.NoContent();
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });
    }
}
