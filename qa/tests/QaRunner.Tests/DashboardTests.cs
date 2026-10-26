using QaRunner.Measurement;
using QaRunner.Models;
using QaRunner.Reports;

namespace QaRunner.Tests;

/// <summary>
/// 저장된 보고서를 다시 읽어 대시보드를 만드는 과정을 확인한다.
/// </summary>
public class DashboardTests : IDisposable
{
    private readonly string _tempReportDirectory =
        Path.Combine(Path.GetTempPath(), $"qa-dashboard-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempReportDirectory))
            Directory.Delete(_tempReportDirectory, recursive: true);
    }

    // 부하 시험 실행 기록 하나 (Gold 반영 p95 = 1.56초)
    private static TestRun LoadRun(DateTimeOffset startedAt) => new(
        RunId: TestRun.CreateRunId(startedAt),
        SuiteName: TestSuite.Load.ToKoreanName(),
        StartedAt: startedAt,
        FinishedAt: startedAt.AddSeconds(30),
        CasesPath: "cases/rules.json",
        TargetName: "FakeVerdictService + FakeStreamingPipeline",
        Results:
        [
            new TestResult(LoadChecker.ApiCaseId, LoadChecker.ApiLatencyCheckName, "입력", "3초 이하", "0.00초", MeetsTarget: true),
            new TestResult(LoadChecker.StreamCaseId, LoadChecker.GoldLatencyCheckName, "입력", "10초 이하", "1.56초", MeetsTarget: true),
        ]);

    [Fact]
    public void Loads_saved_runs_in_start_time_order_and_skips_unreadable_files()
    {
        JsonReportWriter.Save(LoadRun(SampleTestRuns.StartedAt.AddMinutes(5)), _tempReportDirectory);
        JsonReportWriter.Save(SampleTestRuns.OnePassedOneFailed(), _tempReportDirectory);

        // 깨진 파일, 결과 형태를 통일하기 전의 옛 보고서는 건너뛰어야 한다
        File.WriteAllText(Path.Combine(_tempReportDirectory, "broken.json"), "{ 깨진 파일");
        File.WriteAllText(Path.Combine(_tempReportDirectory, "old.json"), """
            { "runId": "old", "results": [ { "caseId": "R-001", "message": "문자", "expectedVerdict": "고위험" } ] }
            """);

        var testRuns = ReportHistoryLoader.LoadAll(_tempReportDirectory);

        Assert.Equal(2, testRuns.Count);
        Assert.Equal("판정 시험", testRuns[0].SuiteName);
        Assert.Equal("부하 시험", testRuns[1].SuiteName);
        Assert.Equal(2, testRuns[1].Results.Count);
    }

    [Fact]
    public void Returns_empty_list_when_report_folder_does_not_exist()
    {
        Assert.Empty(ReportHistoryLoader.LoadAll(_tempReportDirectory));
    }

    [Fact]
    public void Finds_seconds_value_of_a_measurement()
    {
        var loadRun = LoadRun(SampleTestRuns.StartedAt);

        Assert.Equal(1.56, ReportHistoryLoader.FindSeconds(loadRun, LoadChecker.GoldLatencyCheckName));
        Assert.Null(ReportHistoryLoader.FindSeconds(loadRun, "없는 항목"));
    }

    [Fact]
    public void Dashboard_shows_section_per_suite_with_trend_charts_and_links()
    {
        var testRuns = new List<TestRun>
        {
            SampleTestRuns.OnePassedOneFailed(),
            LoadRun(SampleTestRuns.StartedAt.AddMinutes(5)),
        };

        var html = DashboardWriter.BuildHtml(testRuns, SampleTestRuns.StartedAt.AddMinutes(10));

        Assert.Contains("<h2>판정 시험</h2>", html);
        Assert.Contains("<h2>부하 시험</h2>", html);
        Assert.DoesNotContain("<h2>복구 시험</h2>", html);

        // 부하 시험은 목표 점선이 있는 측정 항목 차트가 있어야 한다
        Assert.Contains("Gold 반영 시간 p95 (목표 10초 이하)", html);
        Assert.Contains("목표 10초", html);

        // 실행마다 HTML 보고서로 가는 링크
        Assert.Contains($"href=\"{SampleTestRuns.OnePassedOneFailed().RunId}.html\"", html);
        Assert.Contains("class=\"fake\"", html);
    }

    [Fact]
    public void Dashboard_explains_when_there_is_no_run_yet()
    {
        var html = DashboardWriter.BuildHtml([], SampleTestRuns.StartedAt);

        Assert.Contains("아직 저장된 보고서가 없습니다", html);
    }

    [Fact]
    public void Saving_reports_also_rebuilds_dashboard()
    {
        var savedReports = QaRunService.SaveReports(SampleTestRuns.OnePassedOneFailed(), _tempReportDirectory);

        Assert.Equal(DashboardWriter.FileName, Path.GetFileName(savedReports.DashboardPath));
        Assert.Contains("<h2>판정 시험</h2>", File.ReadAllText(savedReports.DashboardPath));
    }

    [Fact]
    public void Bar_chart_marks_missing_measurement_instead_of_drawing_a_bar()
    {
        ChartBar[] bars =
        [
            new("10/26 14:30", "측정 성공", 1.5, "1.50초"),
            new("10/26 14:40", "측정 실패", null, "측정값 없음"),
        ];

        var svg = SvgBarChart.Build("시험 차트", bars, axisMaximum: 12, unitSuffix: "초", targetValue: 10);

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(svg, "class=\"bar\""));
        Assert.Contains("<title>측정 실패</title>", svg);
        Assert.Contains("목표 10초", svg);
    }
}
