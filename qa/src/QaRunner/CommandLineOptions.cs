using QaRunner.Models;

namespace QaRunner;

/// <summary>
/// 실행할 때 넘기는 옵션. 넘기지 않은 옵션은 기본값을 쓴다.
/// 예: dotnet run --project src/QaRunner -- --suite pipeline --cases cases/pipeline --reports reports
/// </summary>
/// <param name="Suite">돌릴 시험 종류 (판정 시험 또는 파이프라인 시험)</param>
/// <param name="CasesPath">판정 시험은 입력값 파일, 파이프라인 시험은 시나리오 폴더 경로</param>
/// <param name="ReportDirectory">보고서 파일을 저장할 폴더</param>
/// <param name="ApiBaseUrl">판정 API 주소. 주면 실제 API로, 안 주면(null) 가짜 판정 대상으로 채점한다 (판정 시험에서만 씀)</param>
public sealed record CommandLineOptions(TestSuite Suite, string CasesPath, string ReportDirectory, string? ApiBaseUrl)
{
    public const string DefaultVerdictCasesPath = "cases/rules.json";
    public const string DefaultPipelineCasesPath = "cases/pipeline";
    public const string DefaultReportDirectory = "reports";

    /// <summary>
    /// 명령줄 인자에서 옵션 값을 읽는다. --suite 값이 verdict, pipeline이 아니면 ArgumentException을 던진다.
    /// </summary>
    /// <param name="args">프로그램에 넘어온 명령줄 인자</param>
    public static CommandLineOptions Parse(string[] args)
    {
        var suite = ParseSuite(FindOptionValue(args, "--suite"));

        // --cases를 주지 않으면 시험 종류에 맞는 기본 경로를 쓴다
        var defaultCasesPath = suite == TestSuite.Pipeline ? DefaultPipelineCasesPath : DefaultVerdictCasesPath;

        return new CommandLineOptions(
            Suite: suite,
            CasesPath: FindOptionValue(args, "--cases") ?? defaultCasesPath,
            ReportDirectory: FindOptionValue(args, "--reports") ?? DefaultReportDirectory,
            ApiBaseUrl: FindOptionValue(args, "--api-url"));
    }

    // --suite 값을 시험 종류로 바꾼다. 값이 없으면 판정 시험
    private static TestSuite ParseSuite(string? suiteValue) => suiteValue?.ToLowerInvariant() switch
    {
        null or "verdict" => TestSuite.Verdict,
        "pipeline" => TestSuite.Pipeline,
        _ => throw new ArgumentException($"--suite 값은 verdict 또는 pipeline이어야 합니다: {suiteValue}"),
    };

    // "--cases a.json"처럼 옵션 이름 바로 뒤에 오는 값을 찾는다. 없으면 null
    private static string? FindOptionValue(string[] args, string optionName)
    {
        var optionIndex = Array.IndexOf(args, optionName);
        var hasValue = optionIndex >= 0 && optionIndex + 1 < args.Length;
        return hasValue ? args[optionIndex + 1] : null;
    }
}
