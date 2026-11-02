using QaRunner.Models;

namespace QaRunner.Tests;

/// <summary>
/// 콘솔과 데스크탑 앱이 함께 쓰는 실행 절차(준비 → 채점 → 보고서 저장)를 처음부터 끝까지 확인한다.
/// </summary>
public class QaRunServiceTests : IDisposable
{
    private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), $"qa-run-service-test-{Guid.NewGuid():N}");

    public QaRunServiceTests() => Directory.CreateDirectory(_tempFolder);

    public void Dispose() => Directory.Delete(_tempFolder, recursive: true);

    private string ReportDirectory => Path.Combine(_tempFolder, "reports");

    [Fact]
    public async Task Runs_verdict_suite_and_saves_both_reports()
    {
        var sessionsFilePath = Path.Combine(_tempFolder, "sessions.json");
        File.WriteAllText(sessionsFilePath, """
            [ { "caseId": "S-001", "actor": "봇 A", "description": "오픈 직후 진입", "expectedVerdict": "추가 확인",
                "steps": [ { "type": "queue_enter", "atMs": 30 }, { "type": "seat_hold", "atMs": 130 } ] } ]
            """);
        var options = new CommandLineOptions(TestSuite.Verdict, sessionsFilePath, ReportDirectory, ApiBaseUrl: null);

        var preparedRun = QaRunService.Prepare(options);
        var testRun = await QaRunService.RunAsync(preparedRun, ReportDirectory);
        var savedReports = QaRunService.SaveReports(testRun, ReportDirectory);

        Assert.Equal("판정 시험", testRun.SuiteName);
        Assert.Equal("FakeVerdictService", testRun.TargetName);

        // 사례 1건 + 봇 탐지율 1건. 연습용 판정기도 오픈 직후 진입한 봇은 잡는다
        Assert.Equal(["S-001", TestResult.WholeRunCaseId], testRun.Results.Select(result => result.CaseId));
        Assert.Equal(2, testRun.PassedCount);
        Assert.True(File.Exists(savedReports.JsonReportPath));
        Assert.True(File.Exists(savedReports.HtmlReportPath));
    }

    [Fact]
    public async Task Runs_pipeline_suite_with_fake_pipeline()
    {
        var scenarioFolder = Path.Combine(_tempFolder, "pipeline");
        Directory.CreateDirectory(scenarioFolder);
        File.WriteAllText(Path.Combine(scenarioFolder, "dup.json"), """
            { "caseId": "DUP-001", "events": [ { "eventId": "e1", "sessionHash": "s1" } ], "injection": { "duplicateCount": 3 } }
            """);
        var options = new CommandLineOptions(TestSuite.Pipeline, scenarioFolder, ReportDirectory, ApiBaseUrl: null);

        var testRun = await QaRunService.RunAsync(QaRunService.Prepare(options), ReportDirectory);

        Assert.Equal("파이프라인 시험", testRun.SuiteName);
        Assert.Equal("FakePipeline", testRun.TargetName);
        Assert.Equal(0, testRun.FailedCount);
    }

    [Fact]
    public void Missing_cases_file_is_a_preparation_error()
    {
        var options = new CommandLineOptions(TestSuite.Verdict, Path.Combine(_tempFolder, "없는파일.json"), ReportDirectory, null);

        var exception = Record.Exception(() => QaRunService.Prepare(options));

        Assert.NotNull(exception);
        Assert.True(QaRunService.IsPreparationError(exception));
    }
}
