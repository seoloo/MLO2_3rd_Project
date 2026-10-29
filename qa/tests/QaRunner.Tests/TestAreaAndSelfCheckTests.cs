using System.Text.Json;
using QaRunner.Models;
using QaRunner.Reports;
using QaRunner.SelfCheck;

namespace QaRunner.Tests;

/// <summary>
/// 시험 영역(서비스 · 앱 · QA 도구)과 QA 도구 점검을 확인한다.
/// </summary>
public class TestAreaAndSelfCheckTests : IDisposable
{
    private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), $"qa-area-test-{Guid.NewGuid():N}");

    public TestAreaAndSelfCheckTests() => Directory.CreateDirectory(_tempFolder);

    public void Dispose() => Directory.Delete(_tempFolder, recursive: true);

    [Fact]
    public void Self_check_belongs_to_qa_tool_area_and_other_suites_to_service()
    {
        Assert.Equal(TestArea.QaTool, TestSuite.SelfCheck.Area());
        Assert.All(new[] { TestSuite.Verdict, TestSuite.Pipeline, TestSuite.Load, TestSuite.Recovery },
            testSuite => Assert.Equal(TestArea.Service, testSuite.Area()));
        Assert.False(TestArea.App.HasRunnableSuites());
        Assert.True(TestArea.QaTool.HasRunnableSuites());
    }

    [Fact]
    public void Parses_selfcheck_option_without_cases_path()
    {
        var options = CommandLineOptions.Parse(["--suite", "selfcheck"]);

        Assert.Equal(TestSuite.SelfCheck, options.Suite);
        Assert.Equal(CommandLineOptions.NoCasesPath, options.CasesPath);
        Assert.Null(options.Measurement);
    }

    [Fact]
    public async Task Self_check_passes_every_item_and_is_recorded_in_qa_tool_area()
    {
        var reportDirectory = Path.Combine(_tempFolder, "reports");
        var options = CommandLineOptions.Parse(["--suite", "selfcheck", "--reports", reportDirectory]);

        var testRun = await QaRunService.RunAsync(QaRunService.Prepare(options), reportDirectory);

        Assert.Equal("QA 도구 점검", testRun.SuiteName);
        Assert.Equal("QA 도구", testRun.AreaName);
        Assert.Equal(QaToolSelfChecker.TargetName, testRun.TargetName);
        Assert.False(testRun.UsedFakeTarget);
        Assert.True(testRun.TotalCount >= 10);
        Assert.All(testRun.Results, testResult => Assert.True(testResult.IsPassed, $"{testResult.CaseId} {testResult.CheckName}: {testResult.Actual}"));
    }

    [Fact]
    public void Reads_reports_saved_before_areas_as_service_area()
    {
        // 영역을 나누기 전의 보고서에는 areaName이 없다
        File.WriteAllText(Path.Combine(_tempFolder, "old.json"), """
            { "runId": "old", "suiteName": "판정 시험", "startedAt": "2026-10-20T09:00:00+09:00", "finishedAt": "2026-10-20T09:00:01+09:00",
              "casesPath": "cases/rules.json", "targetName": "FakeVerdictService",
              "results": [ { "caseId": "R-001", "checkName": "판정", "input": "문자", "expected": "고위험", "actual": "고위험" } ] }
            """);

        var testRun = Assert.Single(ReportHistoryLoader.LoadAll(_tempFolder));

        Assert.Equal("서비스", testRun.AreaName);
    }

    [Fact]
    public void Dashboard_groups_suites_under_area_headings()
    {
        var now = DateTimeOffset.Now;
        var serviceRun = new TestRun("a", "판정 시험", now, now, "cases/rules.json", "FakeVerdictService",
            [new TestResult("R-001", "판정", "문자", "고위험", "고위험")]);
        var selfCheckRun = new TestRun("b", "QA 도구 점검", now.AddMinutes(1), now.AddMinutes(1), CommandLineOptions.NoCasesPath,
            QaToolSelfChecker.TargetName, [new TestResult("SC-01", "점검", "입력", "같음", "같음")]) { AreaName = "QA 도구" };

        var html = DashboardWriter.BuildHtml([serviceRun, selfCheckRun], now);

        var serviceHeading = html.IndexOf("서비스 영역", StringComparison.Ordinal);
        var qaToolHeading = html.IndexOf("QA 도구 영역", StringComparison.Ordinal);
        Assert.True(serviceHeading >= 0 && qaToolHeading > serviceHeading);
        Assert.True(html.IndexOf("QA 도구 점검", qaToolHeading, StringComparison.Ordinal) > qaToolHeading);
        Assert.DoesNotContain("앱 영역", html);
    }

    [Fact]
    public void Saves_area_name_in_json_report()
    {
        var now = DateTimeOffset.Now;
        var testRun = new TestRun("c", "QA 도구 점검", now, now, CommandLineOptions.NoCasesPath, QaToolSelfChecker.TargetName, [])
        {
            AreaName = "QA 도구",
        };

        var json = File.ReadAllText(JsonReportWriter.Save(testRun, _tempFolder));

        Assert.Equal("QA 도구", JsonDocument.Parse(json).RootElement.GetProperty("areaName").GetString());
    }
}
