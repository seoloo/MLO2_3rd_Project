using System.Text.Json;
using QaRunner.Collaboration;
using QaRunner.Contracts;
using QaRunner.Verdicts;

namespace QaRunner.Tests;

/// <summary>AI 라벨의 검수 상태, 기존 채점기와의 연결, 공유 실패 시 결과 보존을 검증한다.</summary>
public sealed class CollaborationTests : IDisposable
{
    private readonly string _reportDirectory = Path.Combine(Path.GetTempPath(), "qa-collaboration-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_reportDirectory)) Directory.Delete(_reportDirectory, recursive: true);
    }

    [Fact]
    public async Task Demo_cases_are_valid_but_never_automatically_reviewed()
    {
        var caseSet = await CreateCasesAsync();
        caseSet.Validate();
        Assert.True(caseSet.IsProvisional);
        Assert.Equal(4, caseSet.Cases.Count);
        Assert.All(caseSet.Cases, testCase => Assert.Equal(LabelReviewState.AiSuggested, testCase.ReviewState));
        var reviewed = caseSet with
        {
            Cases = caseSet.Cases.Select(testCase => testCase with { ReviewState = LabelReviewState.HumanReviewed }).ToArray(),
        };
        Assert.False(reviewed.IsProvisional);
    }

    [Fact]
    public async Task Rejects_unknown_labels_and_duplicate_case_ids()
    {
        var caseSet = await CreateCasesAsync();
        var unknownLabel = caseSet with { Cases = [caseSet.Cases[0] with { ExpectedVerdict = "SAFE" }] };
        var duplicateId = caseSet with { Cases = [caseSet.Cases[0], caseSet.Cases[0]] };
        var noSteps = caseSet with { Cases = [caseSet.Cases[0] with { Steps = [] }] };
        var noActor = caseSet with { Cases = [caseSet.Cases[0] with { Actor = " " }] };
        Assert.Throws<ArgumentException>(unknownLabel.Validate);
        Assert.Throws<ArgumentException>(duplicateId.Validate);
        Assert.Throws<ArgumentException>(noSteps.Validate);
        Assert.Throws<ArgumentException>(noActor.Validate);
    }

    [Fact]
    public async Task Workflow_compares_service_answers_and_preserves_case_provenance()
    {
        var caseSet = await CreateCasesAsync();
        var client = new RecordingTeamClient();
        var service = new RecordingVerdictService();
        var outcome = await new TeamQaWorkflow(client).RunAsync(
            caseSet, "팀원 A", "local-demo", "test-commit", service, "FakeRecordingService", _reportDirectory);

        // 생성된 세션이 모두 같은 순서로 판정 대상에 전달되고, 행동 수도 그대로다
        Assert.Equal(caseSet.Cases.Select(testCase => testCase.CaseId), service.CaseIds);
        Assert.Equal(caseSet.Cases.Select(testCase => testCase.Steps.Count), service.StepCounts);
        Assert.Equal([SharedRunState.Running, SharedRunState.Completed], client.Published.Select(run => run.State));

        // 항상 "정상"이라고 답하므로 사람 2개만 통과한다. 팀 화면에는 사례별 결과만 올라간다
        Assert.Equal(2, outcome.Run.PassedCount);
        Assert.Equal(caseSet.Cases.Count, outcome.Run.Results.Count);
        Assert.Contains("사람 오탐률", File.ReadAllText(outcome.Reports.HtmlReportPath));
        Assert.True(outcome.Run.IsProvisional);
        Assert.Null(outcome.SyncError);
        Assert.NotEqual(Guid.Empty, outcome.Run.RunId);
        Assert.Contains("공식 정확도가 아닙니다", File.ReadAllText(outcome.Reports.HtmlReportPath));
        Assert.Contains("검수 전 탐색 시험", File.ReadAllText(outcome.Reports.DashboardPath));

        var saved = JsonSerializer.Deserialize<SharedQaRun>(File.ReadAllText(outcome.SnapshotPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(saved);
        Assert.Equal(caseSet.DatasetId, saved.CaseSet.DatasetId);
        Assert.Equal(caseSet.PromptVersion, saved.CaseSet.PromptVersion);
        Assert.Equal(SharedRunState.Completed, saved.State);
        Assert.True(saved.IsProvisional);
    }

    [Fact]
    public async Task Upload_failure_keeps_local_reports_and_retry_snapshot()
    {
        var outcome = await new TeamQaWorkflow(new UnavailableTeamClient()).RunAsync(
            await CreateCasesAsync(), "팀원 A", "local", "prototype", new FakeVerdictService(),
            nameof(FakeVerdictService), _reportDirectory);

        Assert.Equal(SharedRunState.Completed, outcome.Run.State);
        Assert.Contains("공유 서버 연결 실패", outcome.SyncError);
        Assert.True(File.Exists(outcome.Reports.JsonReportPath));
        Assert.True(File.Exists(outcome.SnapshotPath));
    }

    [Fact]
    public async Task Unexpected_runner_error_is_recorded_as_error_instead_of_completed()
    {
        var client = new RecordingTeamClient();
        var caseSet = await CreateCasesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new TeamQaWorkflow(client).RunAsync(
            caseSet, "팀원 A", "local", "prototype", new BrokenVerdictService(),
            "BrokenService", _reportDirectory));
        Assert.Equal(SharedRunState.Error, client.Published[^1].State);
        Assert.Equal("시험 대상 버그", client.Published[^1].Error);
        Assert.Single(Directory.GetFiles(Path.Combine(_reportDirectory, "team-runs"), "*.json"));
    }

    [Fact]
    public async Task Repeated_upload_does_not_duplicate_or_revert_completed_run()
    {
        var caseSet = await CreateCasesAsync();
        var now = DateTimeOffset.UtcNow;
        var running = new SharedQaRun(Guid.NewGuid(), "팀원 A", "local", "prototype",
            SharedRunState.Running, now, now, "FakeService", caseSet, []);
        var completed = running with { State = SharedRunState.Completed, UpdatedAt = now.AddSeconds(1) };
        var client = new DemoTeamRunClient();
        await client.PublishAsync(running);
        await client.PublishAsync(completed);
        await client.PublishAsync(completed);
        await client.PublishAsync(running);
        Assert.Single(await client.ListAsync());
        Assert.Equal(SharedRunState.Completed, (await client.FindAsync(running.RunId))!.State);
        Assert.Null(await client.FindAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Runpod_placeholder_fails_explicitly_instead_of_returning_fake_ai_output()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            new RunpodCaseGenerator().GenerateAsync(new CaseGenerationRequest("새 유형 봇")));
    }

    [Fact]
    public void Remote_sharing_is_disabled_until_authentication_is_implemented()
    {
        Assert.Throws<ArgumentException>(() => new HttpTeamRunClient(new Uri("https://example.test")));
    }

    private static Task<GeneratedCaseSet> CreateCasesAsync() =>
        new DemoCaseGenerator().GenerateAsync(new CaseGenerationRequest("새 유형 봇·사람 예매 세션"));

    // 받은 세션을 기록하고 항상 "정상"이라고 답하는 시험 대상.
    private sealed class RecordingVerdictService : IVerdictService
    {
        public List<string> CaseIds { get; } = [];
        public List<int> StepCounts { get; } = [];
        public Task<string> GetVerdictAsync(Models.SessionCase session, string runId, string sessionKey)
        {
            CaseIds.Add(session.CaseId);
            StepCounts.Add(session.Steps.Count);
            return Task.FromResult("정상");
        }
    }

    private sealed class BrokenVerdictService : IVerdictService
    {
        public Task<string> GetVerdictAsync(Models.SessionCase session, string runId, string sessionKey) =>
            throw new InvalidOperationException("시험 대상 버그");
    }

    private sealed class RecordingTeamClient : ITeamRunClient
    {
        public List<SharedQaRun> Published { get; } = [];
        public Task PublishAsync(SharedQaRun run, CancellationToken cancellationToken = default)
        {
            Published.Add(run);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<SharedQaRun>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SharedQaRun>>(Published);
        public Task<SharedQaRun?> FindAsync(Guid runId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Published.LastOrDefault(run => run.RunId == runId));
    }

    private sealed class UnavailableTeamClient : ITeamRunClient
    {
        public Task PublishAsync(SharedQaRun run, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("공유 서버 연결 실패");
        public Task<IReadOnlyList<SharedQaRun>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new HttpRequestException();
        public Task<SharedQaRun?> FindAsync(Guid runId, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException();
    }
}
