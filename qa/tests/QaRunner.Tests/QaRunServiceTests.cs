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
        var rulesFilePath = Path.Combine(_tempFolder, "rules.json");
        File.WriteAllText(rulesFilePath, """
            [ { "caseId": "R-001", "message": "인증번호를 알려주세요", "expectedVerdict": "고위험" } ]
            """);
        var options = new CommandLineOptions(TestSuite.Verdict, rulesFilePath, ReportDirectory, ApiBaseUrl: null);

        var preparedRun = QaRunService.Prepare(options);
        var testRun = await QaRunService.RunAsync(preparedRun, ReportDirectory);
        var savedReports = QaRunService.SaveReports(testRun, ReportDirectory);

        Assert.Equal("판정 시험", testRun.SuiteName);
        Assert.Equal("FakeVerdictService", testRun.TargetName);
        Assert.Equal(1, testRun.PassedCount);
        Assert.True(File.Exists(savedReports.JsonReportPath));
        Assert.True(File.Exists(savedReports.HtmlReportPath));
    }

    [Fact]
    public async Task Runs_pipeline_suite_with_fake_pipeline()
    {
        var scenarioFolder = Path.Combine(_tempFolder, "pipeline");
        Directory.CreateDirectory(scenarioFolder);
        File.WriteAllText(Path.Combine(scenarioFolder, "dup.json"), """
            { "caseId": "DUP-001", "events": [ { "eventId": "e1", "scanId": "s1" } ], "injection": { "duplicateCount": 3 } }
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
