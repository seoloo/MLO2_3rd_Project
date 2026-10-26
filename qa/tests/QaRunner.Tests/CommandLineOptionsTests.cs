using QaRunner.Models;

namespace QaRunner.Tests;

/// <summary>
/// 명령줄 옵션을 읽을 때 값이 있으면 그 값을, 없으면 기본값을 쓰는지 확인한다.
/// </summary>
public class CommandLineOptionsTests
{
    [Fact]
    public void Uses_verdict_suite_and_default_values_when_no_options_are_given()
    {
        var options = CommandLineOptions.Parse([]);

        Assert.Equal(TestSuite.Verdict, options.Suite);
        Assert.Equal(CommandLineOptions.DefaultVerdictCasesPath, options.CasesPath);
        Assert.Equal(CommandLineOptions.DefaultReportDirectory, options.ReportDirectory);

        // API 주소가 없으면 가짜 판정 대상을 쓴다는 뜻
        Assert.Null(options.ApiBaseUrl);
    }

    [Fact]
    public void Uses_pipeline_scenario_folder_by_default_for_pipeline_suite()
    {
        var options = CommandLineOptions.Parse(["--suite", "pipeline"]);

        Assert.Equal(TestSuite.Pipeline, options.Suite);
        Assert.Equal(CommandLineOptions.DefaultPipelineCasesPath, options.CasesPath);
    }

    [Fact]
    public void Reads_values_that_follow_option_names()
    {
        var options = CommandLineOptions.Parse(
            ["--reports", "out", "--cases", "cases/more.json", "--api-url", "http://localhost:8000"]);

        Assert.Equal("cases/more.json", options.CasesPath);
        Assert.Equal("out", options.ReportDirectory);
        Assert.Equal("http://localhost:8000", options.ApiBaseUrl);
    }

    [Fact]
    public void Uses_default_when_option_has_no_value()
    {
        // 옵션 이름만 있고 값이 빠진 경우
        var options = CommandLineOptions.Parse(["--cases"]);

        Assert.Equal(CommandLineOptions.DefaultVerdictCasesPath, options.CasesPath);
    }

    [Fact]
    public void Rejects_unknown_suite_name()
    {
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["--suite", "load"]));
    }
}
