using System.Globalization;
using QaRunner.Measurement;
using QaRunner.Models;

namespace QaRunner;

/// <summary>
/// 실행할 때 넘기는 옵션. 넘기지 않은 옵션은 기본값을 쓴다.
/// 예: dotnet run --project src/QaRunner -- --suite load --duration 60 --rate 5
/// </summary>
/// <param name="Suite">돌릴 시험 종류</param>
/// <param name="CasesPath">판정·부하 시험은 입력값 파일, 파이프라인 시험은 시나리오 폴더 경로 (복구 시험은 쓰지 않음)</param>
/// <param name="ReportDirectory">보고서 파일을 저장할 폴더</param>
/// <param name="ApiBaseUrl">판정 API 주소. 주면 실제 API로, 안 주면(null) 가짜 판정 대상으로 채점한다 (판정·부하 시험에서 씀)</param>
/// <param name="Measurement">부하·복구 시험의 측정 조건. 다른 시험에서는 null</param>
public sealed record CommandLineOptions(
    TestSuite Suite,
    string CasesPath,
    string ReportDirectory,
    string? ApiBaseUrl,
    MeasurementSettings? Measurement = null)
{
    public const string DefaultVerdictCasesPath = "cases/rules.json";
    public const string DefaultPipelineCasesPath = "cases/pipeline";
    public const string DefaultReportDirectory = "reports";

    /// <summary>복구 시험처럼 입력 파일을 쓰지 않을 때 보고서에 남기는 말</summary>
    public const string NoCasesPath = "(입력 파일 없음)";

    /// <summary>
    /// 명령줄 인자에서 옵션 값을 읽는다. 값이 틀리면 ArgumentException을 던진다.
    /// </summary>
    /// <param name="args">프로그램에 넘어온 명령줄 인자</param>
    public static CommandLineOptions Parse(string[] args)
    {
        var suite = TestSuiteExtensions.ParseOptionValue(FindOptionValue(args, "--suite"));

        return new CommandLineOptions(
            Suite: suite,
            CasesPath: FindOptionValue(args, "--cases") ?? DefaultCasesPathFor(suite),
            ReportDirectory: FindOptionValue(args, "--reports") ?? DefaultReportDirectory,
            ApiBaseUrl: FindOptionValue(args, "--api-url"),
            Measurement: suite.IsMeasurementSuite() ? ParseMeasurement(args, suite) : null);
    }

    /// <summary>
    /// --cases를 주지 않았을 때 시험 종류에 맞는 기본 경로
    /// </summary>
    public static string DefaultCasesPathFor(TestSuite testSuite) => testSuite switch
    {
        TestSuite.Pipeline => DefaultPipelineCasesPath,
        TestSuite.Recovery => NoCasesPath,
        _ => DefaultVerdictCasesPath,
    };

    // --duration, --rate, --stop-seconds를 읽는다. 주지 않은 값은 실행안 기준 기본값을 쓴다
    private static MeasurementSettings ParseMeasurement(string[] args, TestSuite suite)
    {
        var defaults = MeasurementSettings.DefaultFor(suite);

        var measurement = defaults with
        {
            DurationSeconds = ParseInt(args, "--duration") ?? defaults.DurationSeconds,
            EventsPerSecond = ParseDouble(args, "--rate") ?? defaults.EventsPerSecond,
            StopSeconds = ParseInt(args, "--stop-seconds") ?? defaults.StopSeconds,
        };
        measurement.Validate(suite);
        return measurement;
    }

    private static int? ParseInt(string[] args, string optionName)
    {
        var optionValue = FindOptionValue(args, optionName);
        if (optionValue is null)
            return null;

        return int.TryParse(optionValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new ArgumentException($"{optionName} 값은 정수여야 합니다: {optionValue}");
    }

    private static double? ParseDouble(string[] args, string optionName)
    {
        var optionValue = FindOptionValue(args, optionName);
        if (optionValue is null)
            return null;

        return double.TryParse(optionValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new ArgumentException($"{optionName} 값은 숫자여야 합니다: {optionValue}");
    }

    // "--cases a.json"처럼 옵션 이름 바로 뒤에 오는 값을 찾는다. 없으면 null
    private static string? FindOptionValue(string[] args, string optionName)
    {
        var optionIndex = Array.IndexOf(args, optionName);
        var hasValue = optionIndex >= 0 && optionIndex + 1 < args.Length;
        return hasValue ? args[optionIndex + 1] : null;
    }
}
