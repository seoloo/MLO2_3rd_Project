namespace QaRunner.Models;

/// <summary>
/// QA를 한 번 실행한 기록. 보고서 파일 한 쌍(JSON, HTML)이 이 기록 하나에 해당한다.
/// </summary>
/// <param name="RunId">실행 번호. 실행 시작 시각으로 만든다. 예: 2026-10-26-143015</param>
/// <param name="SuiteName">시험 종류. 예: 판정 시험, 파이프라인 시험</param>
/// <param name="StartedAt">실행을 시작한 시각</param>
/// <param name="FinishedAt">실행이 끝난 시각</param>
/// <param name="CasesPath">이번 실행에 쓴 입력값 파일(판정 시험) 또는 시나리오 폴더(파이프라인 시험)</param>
/// <param name="TargetName">시험 대상 이름. 가짜로 돌린 결과인지 실제 시스템으로 돌린 결과인지 구분하는 데 쓴다</param>
/// <param name="Results">확인 항목별 채점 결과</param>
public sealed record TestRun(
    string RunId,
    string SuiteName,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string CasesPath,
    string TargetName,
    IReadOnlyList<TestResult> Results)
{
    /// <summary>AI 제안 라벨이 섞인 탐색 시험인지. 기존 입력값 파일을 쓰는 실행은 false다.</summary>
    public bool HasProvisionalLabels { get; init; }

    /// <summary>
    /// 시험 영역의 한글 이름. 예: 서비스, QA 도구.
    /// 영역을 나누기 전에 만든 보고서에는 이 값이 없으므로 서비스 영역으로 읽는다.
    /// </summary>
    public string AreaName { get; init; } = TestArea.Service.ToKoreanName();

    /// <summary>채점한 확인 항목 수</summary>
    public int TotalCount => Results.Count;

    /// <summary>통과한 확인 항목 수</summary>
    public int PassedCount => Results.Count(testResult => testResult.IsPassed);

    /// <summary>실패한 확인 항목 수 (시스템 실패 포함)</summary>
    public int FailedCount => TotalCount - PassedCount;

    /// <summary>시스템 실패(값을 아예 못 받은 경우) 수</summary>
    public int SystemFailureCount => Results.Count(testResult => testResult.IsSystemFailure);

    /// <summary>통과율(%). 확인 항목이 하나도 없으면 0으로 본다.</summary>
    public double PassRate => TotalCount == 0 ? 0 : PassedCount * 100.0 / TotalCount;

    /// <summary>실행에 걸린 시간</summary>
    public TimeSpan Duration => FinishedAt - StartedAt;

    /// <summary>
    /// 가짜 시험 대상이 하나라도 섞인 실행인지. 가짜 대상 클래스 이름은 모두 Fake로 시작한다.
    /// (부하 시험처럼 대상이 둘이면 "ApiVerdictService + FakeStreamingPipeline"처럼 이름이 이어진다)
    /// </summary>
    public bool UsedFakeTarget => TargetName.Contains("Fake", StringComparison.Ordinal);

    /// <summary>
    /// 실행 시작 시각으로 실행 번호를 만든다.
    /// 보고서 파일 이름으로도 쓰기 때문에 파일 이름에 못 쓰는 콜론(:)은 넣지 않는다.
    /// </summary>
    public static string CreateRunId(DateTimeOffset startedAt) => startedAt.ToString("yyyy-MM-dd-HHmmss");
}
