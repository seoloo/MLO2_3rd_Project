using QaRunner.Reports;

namespace QaRunner.Tests;

/// <summary>
/// 같은 초에 여러 번 실행해도 실행 번호가 겹치지 않는지 확인한다.
/// </summary>
public class RunIdGeneratorTests : IDisposable
{
    private readonly string _tempReportDirectory =
        Path.Combine(Path.GetTempPath(), $"qa-runid-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempReportDirectory))
            Directory.Delete(_tempReportDirectory, recursive: true);
    }

    [Fact]
    public void Uses_start_time_when_no_report_exists()
    {
        var runId = RunIdGenerator.CreateUnique(SampleTestRuns.StartedAt, _tempReportDirectory);

        Assert.Equal("2026-10-26-143015", runId);
    }

    [Fact]
    public void Adds_number_when_report_with_same_run_id_already_exists()
    {
        // 같은 초에 이미 두 번 실행해 보고서가 있는 상황을 만든다
        Directory.CreateDirectory(_tempReportDirectory);
        File.WriteAllText(Path.Combine(_tempReportDirectory, "2026-10-26-143015.json"), "{}");
        File.WriteAllText(Path.Combine(_tempReportDirectory, "2026-10-26-143015-2.html"), "");

        var runId = RunIdGenerator.CreateUnique(SampleTestRuns.StartedAt, _tempReportDirectory);

        Assert.Equal("2026-10-26-143015-3", runId);
    }
}
