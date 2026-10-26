using System.Text.Json;
using QaRunner.Reports;

namespace QaRunner.Tests;

/// <summary>
/// JSON·HTML 보고서가 실행 기록을 빠짐없이, 깨지지 않게 담는지 확인한다.
/// </summary>
public class ReportWriterTests : IDisposable
{
    // 테스트마다 새 임시 폴더를 만들고, 끝나면 지운다
    private readonly string _tempReportDirectory =
        Path.Combine(Path.GetTempPath(), $"qa-report-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempReportDirectory))
            Directory.Delete(_tempReportDirectory, recursive: true);
    }

    [Fact]
    public void Json_report_is_saved_with_run_id_as_file_name_and_keeps_counts()
    {
        var testRun = SampleTestRuns.OnePassedOneFailed();

        var reportFilePath = JsonReportWriter.Save(testRun, _tempReportDirectory);

        Assert.Equal("2026-10-26-143015.json", Path.GetFileName(reportFilePath));

        using var savedReport = JsonDocument.Parse(File.ReadAllText(reportFilePath));
        var root = savedReport.RootElement;
        Assert.Equal("2026-10-26-143015", root.GetProperty("runId").GetString());
        Assert.Equal(1, root.GetProperty("failedCount").GetInt32());
        Assert.Equal(2, root.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public void Json_report_keeps_korean_text_readable()
    {
        var reportFilePath = JsonReportWriter.Save(SampleTestRuns.OnePassedOneFailed(), _tempReportDirectory);

        // 한글이 \uXXXX로 바뀌지 않고 그대로 들어 있어야 한다
        Assert.Contains("인증번호를 알려주세요", File.ReadAllText(reportFilePath));
    }

    [Fact]
    public void Html_report_is_saved_with_run_id_as_file_name()
    {
        var reportFilePath = HtmlReportWriter.Save(SampleTestRuns.OnePassedOneFailed(), _tempReportDirectory);

        Assert.Equal("2026-10-26-143015.html", Path.GetFileName(reportFilePath));
        Assert.True(File.Exists(reportFilePath));
    }

    [Fact]
    public void Html_report_shows_failed_cases_before_passed_ones()
    {
        var html = HtmlReportWriter.BuildHtml(SampleTestRuns.OnePassedOneFailed());

        // 입력값 파일에서는 R-001(통과)이 먼저지만, 보고서에서는 R-002(실패)가 먼저 나와야 한다
        Assert.True(html.IndexOf("R-002", StringComparison.Ordinal) < html.IndexOf("R-001", StringComparison.Ordinal));
    }

    [Fact]
    public void Html_report_escapes_special_characters_in_messages()
    {
        var testRun = SampleTestRuns.OnePassedOneFailed();
        var messageWithTag = testRun.Results[0] with { Message = "<script>alert(1)</script>" };
        var testRunWithTag = testRun with { Results = [messageWithTag] };

        var html = HtmlReportWriter.BuildHtml(testRunWithTag);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Html_report_warns_only_when_fake_verdict_service_was_used()
    {
        var fakeHtml = HtmlReportWriter.BuildHtml(SampleTestRuns.OnePassedOneFailed("FakeVerdictService"));
        var realHtml = HtmlReportWriter.BuildHtml(SampleTestRuns.OnePassedOneFailed("ApiVerdictService"));

        Assert.Contains("가짜 판정 대상으로 실행한 결과입니다", fakeHtml);
        Assert.DoesNotContain("가짜 판정 대상으로 실행한 결과입니다", realHtml);
    }
}
