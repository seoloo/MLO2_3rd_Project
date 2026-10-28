using System.Text.Json;
using QaRunner.Contracts;
using QaRunner.Models;
using QaRunner.Verdicts;

namespace QaRunner.Collaboration;

/// <summary>로컬 보고서와 공유용 스냅샷. 업로드가 실패해도 로컬 결과는 남는다.</summary>
public sealed record TeamQaOutcome(SharedQaRun Run, SavedReports Reports, string SnapshotPath, string? SyncError);

/// <summary>
/// 사례 집합 → 기존 판정 채점기 → 로컬 저장 → 팀 공유를 연결하는 판정 시험용 스켈레톤.
/// 생성기와 공유 클라이언트만 교체하면 되며, 기존 CLI의 네 가지 시험 경로는 유지한다.
/// </summary>
public sealed class TeamQaWorkflow(ITeamRunClient teamClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>시작·종료 상태만 공유한다. 세부 진행률·취소·자동 재전송은 후속 구현 대상이다.</summary>
    public async Task<TeamQaOutcome> RunAsync(
        GeneratedCaseSet caseSet, string actorName, string environmentName, string codeVersion,
        IVerdictService verdictService, string targetName, string reportDirectory)
    {
        caseSet.Validate();
        var startedAt = DateTimeOffset.UtcNow;
        var snapshot = new SharedQaRun(Guid.NewGuid(), actorName, environmentName, codeVersion,
            SharedRunState.Running, startedAt, startedAt, targetName, caseSet, []);
        snapshot.Validate();
        await TryPublishAsync(snapshot);

        try
        {
            // 예상 정답은 QA 내부에서만 보관한다. IVerdictService에는 문자만 전달된다.
            var testCases = caseSet.Cases.Select(testCase => new TestCase(
                testCase.CaseId, testCase.Message, VerdictCodes.ToKorean(testCase.ExpectedVerdict)));
            var results = await new VerdictChecker(verdictService).CheckAllAsync(testCases);
            snapshot = snapshot with
            {
                State = SharedRunState.Completed,
                UpdatedAt = DateTimeOffset.UtcNow,
                Results = results.Select(result => new SharedCheckResult(
                    result.CaseId, result.Expected, result.Actual, result.ErrorMessage)).ToArray(),
            };

            var testRun = new TestRun(snapshot.RunId.ToString("N"), "판정 시험", startedAt, snapshot.UpdatedAt,
                $"생성 사례 {caseSet.DatasetId}", targetName, results)
            {
                HasProvisionalLabels = caseSet.IsProvisional,
            };
            var reports = QaRunService.SaveReports(testRun, reportDirectory);
            var snapshotPath = SaveSnapshot(snapshot, reportDirectory);
            var syncError = await TryPublishAsync(snapshot);
            return new TeamQaOutcome(snapshot, reports, snapshotPath, syncError);
        }
        catch (Exception exception)
        {
            snapshot = snapshot with { State = SharedRunState.Error, UpdatedAt = DateTimeOffset.UtcNow, Error = exception.Message };
            SaveSnapshot(snapshot, reportDirectory);
            await TryPublishAsync(snapshot);
            throw;
        }
    }

    // 추후 재전송과 사례 재현에 쓸 전체 스냅샷. AI 생성 결과도 이 파일에 보관한다.
    private static string SaveSnapshot(SharedQaRun snapshot, string reportDirectory)
    {
        var directory = Path.Combine(reportDirectory, "team-runs");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{snapshot.RunId:N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, JsonOptions));
        return path;
    }

    // 공유 서버 장애로 로컬 QA까지 잃지 않도록 통신 실패를 별도로 알린다.
    private async Task<string?> TryPublishAsync(SharedQaRun snapshot)
    {
        try
        {
            await teamClient.PublishAsync(snapshot);
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return exception.Message;
        }
    }
}
