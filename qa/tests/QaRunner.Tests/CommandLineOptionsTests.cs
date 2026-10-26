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
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["--suite", "stress"]));
    }

    [Fact]
    public void Measurement_suites_use_plan_defaults_when_no_numbers_are_given()
    {
        var loadOptions = CommandLineOptions.Parse(["--suite", "load"]);
        var recoveryOptions = CommandLineOptions.Parse(["--suite", "recovery"]);

        // 실행안 기준: 부하 300초·초당 5건, 복구 120초·30초 중단
        Assert.Equal(300, loadOptions.Measurement!.DurationSeconds);
        Assert.Equal(5, loadOptions.Measurement.EventsPerSecond);
        Assert.Equal(120, recoveryOptions.Measurement!.DurationSeconds);
        Assert.Equal(30, recoveryOptions.Measurement.StopSeconds);

        // 복구 시험은 입력 파일을 쓰지 않는다
        Assert.Equal(CommandLineOptions.NoCasesPath, recoveryOptions.CasesPath);
    }

    [Fact]
    public void Reads_measurement_numbers()
    {
        var options = CommandLineOptions.Parse(["--suite", "recovery", "--duration", "40", "--rate", "2.5", "--stop-seconds", "8"]);

        Assert.Equal(40, options.Measurement!.DurationSeconds);
        Assert.Equal(2.5, options.Measurement.EventsPerSecond);
        Assert.Equal(8, options.Measurement.StopSeconds);
        Assert.Equal(10, options.Measurement.StopAfterSeconds);
    }

    [Fact]
    public void Non_measurement_suites_have_no_measurement_settings()
    {
        Assert.Null(CommandLineOptions.Parse(["--suite", "pipeline"]).Measurement);
    }

    [Theory]
    [InlineData("--duration", "abc")]
    [InlineData("--duration", "0")]
    [InlineData("--rate", "-1")]
    public void Rejects_wrong_measurement_numbers(string optionName, string optionValue)
    {
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["--suite", "load", optionName, optionValue]));
    }

    [Fact]
    public void Rejects_recovery_when_stop_does_not_end_before_sending_ends()
    {
        // 2.5초 지점(10/4)에서 10초 멈추면 보내기가 끝난 뒤에야 재가동되므로 거절한다
        Assert.Throws<ArgumentException>(
            () => CommandLineOptions.Parse(["--suite", "recovery", "--duration", "10", "--stop-seconds", "10"]));
    }
}
