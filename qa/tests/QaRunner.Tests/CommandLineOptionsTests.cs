namespace QaRunner.Tests;

/// <summary>
/// 명령줄 옵션을 읽을 때 값이 있으면 그 값을, 없으면 기본값을 쓰는지 확인한다.
/// </summary>
public class CommandLineOptionsTests
{
    [Fact]
    public void Uses_default_values_when_no_options_are_given()
    {
        var options = CommandLineOptions.Parse([]);

        Assert.Equal(CommandLineOptions.DefaultTestCaseFilePath, options.TestCaseFilePath);
        Assert.Equal(CommandLineOptions.DefaultReportDirectory, options.ReportDirectory);
    }

    [Fact]
    public void Reads_values_that_follow_option_names()
    {
        var options = CommandLineOptions.Parse(["--reports", "out", "--cases", "cases/more.json"]);

        Assert.Equal("cases/more.json", options.TestCaseFilePath);
        Assert.Equal("out", options.ReportDirectory);
    }

    [Fact]
    public void Uses_default_when_option_has_no_value()
    {
        // 옵션 이름만 있고 값이 빠진 경우
        var options = CommandLineOptions.Parse(["--cases"]);

        Assert.Equal(CommandLineOptions.DefaultTestCaseFilePath, options.TestCaseFilePath);
    }
}
