using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using QaRunner.Models;

namespace QaRunner.Reports;

/// <summary>
/// 보고서 폴더에 쌓인 JSON 보고서들을 읽어 실행 기록 목록으로 되돌린다. 대시보드가 이 목록으로 추이를 그린다.
/// </summary>
public static partial class ReportHistoryLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 보고서 폴더의 모든 실행 기록을 시작 시각 순서로 읽는다.
    /// 읽을 수 없는 파일이나, 보고서 형식이 바뀌기 전에 만든 옛 파일은 건너뛴다.
    /// </summary>
    /// <param name="reportDirectory">보고서 폴더. 없으면 빈 목록</param>
    public static List<TestRun> LoadAll(string reportDirectory)
    {
        if (!Directory.Exists(reportDirectory))
            return [];

        var testRuns = new List<TestRun>();
        foreach (var reportFilePath in Directory.GetFiles(reportDirectory, "*.json"))
        {
            var testRun = TryLoad(reportFilePath);
            if (testRun is not null)
                testRuns.Add(testRun);
        }

        return testRuns.OrderBy(testRun => testRun.StartedAt).ToList();
    }

    /// <summary>
    /// 실행 기록에서 "1.56초"처럼 초로 적힌 측정값을 숫자로 꺼낸다.
    /// 그 확인 항목이 없거나, 시스템 실패라서 값이 없으면 null.
    /// </summary>
    /// <param name="testRun">찾을 실행 기록</param>
    /// <param name="checkName">확인 항목 이름. 예: Gold 반영 시간 p95</param>
    public static double? FindSeconds(TestRun testRun, string checkName)
    {
        var testResult = testRun.Results.FirstOrDefault(result => result.CheckName == checkName);
        if (testResult is null || testResult.IsSystemFailure)
            return null;

        var secondsMatch = SecondsPattern().Match(testResult.Actual);
        return secondsMatch.Success
            ? double.Parse(secondsMatch.Groups["seconds"].Value, CultureInfo.InvariantCulture)
            : null;
    }

    // 파일 하나를 읽는다. 형식이 맞지 않으면 null
    private static TestRun? TryLoad(string reportFilePath)
    {
        try
        {
            var testRun = JsonSerializer.Deserialize<TestRun>(File.ReadAllText(reportFilePath), JsonOptions);

            // 결과 형태를 통일하기 전(판정 시험만 있던 때)의 보고서에는 시험 종류와 확인 항목이 없다
            var hasCurrentFormat = testRun is { SuiteName: not null, Results: not null }
                && testRun.Results.All(testResult => testResult.CheckName is not null && testResult.Actual is not null);
            return hasCurrentFormat ? testRun : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return null;
        }
    }

    // "1.56초", "12초"에서 숫자 부분
    [GeneratedRegex(@"^(?<seconds>\d+(\.\d+)?)초$")]
    private static partial Regex SecondsPattern();
}
