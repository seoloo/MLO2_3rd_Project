namespace QaRunner.Models;

/// <summary>
/// QA를 한 번 실행한 기록. 보고서 파일 한 쌍(JSON, HTML)이 이 기록 하나에 해당한다.
/// </summary>
/// <param name="RunId">실행 번호. 실행 시작 시각으로 만든다. 예: 2026-10-26-143015</param>
/// <param name="StartedAt">실행을 시작한 시각</param>
/// <param name="FinishedAt">실행이 끝난 시각</param>
/// <param name="TestCaseFilePath">이번 실행에 쓴 입력값 파일 경로</param>
/// <param name="VerdictServiceName">판정 대상 이름. 가짜로 돌린 결과인지 실제 API로 돌린 결과인지 구분하는 데 쓴다</param>
/// <param name="Results">케이스별 채점 결과</param>
public sealed record TestRun(
    string RunId,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string TestCaseFilePath,
    string VerdictServiceName,
    IReadOnlyList<TestResult> Results)
{
    /// <summary>채점한 케이스 수</summary>
    public int TotalCount => Results.Count;

    /// <summary>통과한 케이스 수</summary>
    public int PassedCount => Results.Count(testResult => testResult.IsPassed);

    /// <summary>실패한 케이스 수</summary>
    public int FailedCount => TotalCount - PassedCount;

    /// <summary>통과율(%). 케이스가 하나도 없으면 0으로 본다.</summary>
    public double PassRate => TotalCount == 0 ? 0 : PassedCount * 100.0 / TotalCount;

    /// <summary>실행에 걸린 시간</summary>
    public TimeSpan Duration => FinishedAt - StartedAt;

    /// <summary>
    /// 실행 시작 시각으로 실행 번호를 만든다.
    /// 보고서 파일 이름으로도 쓰기 때문에 파일 이름에 못 쓰는 콜론(:)은 넣지 않는다.
    /// </summary>
    public static string CreateRunId(DateTimeOffset startedAt) => startedAt.ToString("yyyy-MM-dd-HHmmss");
}
