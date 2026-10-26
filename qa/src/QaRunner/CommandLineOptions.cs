namespace QaRunner;

/// <summary>
/// 실행할 때 넘기는 옵션. 넘기지 않은 옵션은 기본값을 쓴다.
/// 예: dotnet run --project src/QaRunner -- --cases cases/rules.json --reports reports --api-url http://localhost:8000
/// </summary>
/// <param name="TestCaseFilePath">읽을 입력값 파일 경로</param>
/// <param name="ReportDirectory">보고서 파일을 저장할 폴더</param>
/// <param name="ApiBaseUrl">판정 API 주소. 주면 실제 API로, 안 주면(null) 가짜 판정 대상으로 채점한다</param>
public sealed record CommandLineOptions(string TestCaseFilePath, string ReportDirectory, string? ApiBaseUrl)
{
    public const string DefaultTestCaseFilePath = "cases/rules.json";
    public const string DefaultReportDirectory = "reports";

    /// <summary>
    /// 명령줄 인자에서 옵션 값을 읽는다.
    /// </summary>
    /// <param name="args">프로그램에 넘어온 명령줄 인자</param>
    public static CommandLineOptions Parse(string[] args) => new(
        TestCaseFilePath: FindOptionValue(args, "--cases") ?? DefaultTestCaseFilePath,
        ReportDirectory: FindOptionValue(args, "--reports") ?? DefaultReportDirectory,
        ApiBaseUrl: FindOptionValue(args, "--api-url"));

    // "--cases a.json"처럼 옵션 이름 바로 뒤에 오는 값을 찾는다. 없으면 null
    private static string? FindOptionValue(string[] args, string optionName)
    {
        var optionIndex = Array.IndexOf(args, optionName);
        var hasValue = optionIndex >= 0 && optionIndex + 1 < args.Length;
        return hasValue ? args[optionIndex + 1] : null;
    }
}
