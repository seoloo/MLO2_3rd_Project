using QaRunner.Models;

namespace QaRunner.Reports;

/// <summary>
/// 보고서 파일 이름이 겹치지 않는 실행 번호를 만든다.
/// </summary>
public static class RunIdGenerator
{
    /// <summary>
    /// 시작 시각으로 실행 번호를 만들고, 같은 이름의 보고서가 이미 있으면 뒤에 -2, -3을 붙인다.
    /// 같은 초 안에 두 번 실행해도 앞의 보고서를 덮어쓰지 않게 하려는 것이다.
    /// </summary>
    /// <param name="startedAt">실행을 시작한 시각</param>
    /// <param name="reportDirectory">보고서를 저장할 폴더</param>
    public static string CreateUnique(DateTimeOffset startedAt, string reportDirectory)
    {
        var baseRunId = TestRun.CreateRunId(startedAt);
        var runId = baseRunId;
        var sameSecondCount = 1;

        while (ReportExists(reportDirectory, runId))
        {
            sameSecondCount++;
            runId = $"{baseRunId}-{sameSecondCount}";
        }

        return runId;
    }

    // JSON이나 HTML 중 하나라도 같은 이름으로 있으면 이미 쓰인 실행 번호로 본다
    private static bool ReportExists(string reportDirectory, string runId) =>
        File.Exists(Path.Combine(reportDirectory, $"{runId}.json"))
        || File.Exists(Path.Combine(reportDirectory, $"{runId}.html"));
}
