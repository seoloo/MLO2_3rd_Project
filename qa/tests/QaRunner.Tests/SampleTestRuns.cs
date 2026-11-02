using QaRunner.Models;

namespace QaRunner.Tests;

/// <summary>
/// 여러 테스트에서 같이 쓰는 예시 실행 기록.
/// </summary>
internal static class SampleTestRuns
{
    public static readonly DateTimeOffset StartedAt = new(2026, 10, 26, 14, 30, 15, TimeSpan.FromHours(9));

    /// <summary>통과 1건, 실패 1건이 들어 있는 판정 시험 실행 기록</summary>
    public static TestRun OnePassedOneFailed(string targetName = "FakeVerdictService") => new(
        RunId: TestRun.CreateRunId(StartedAt),
        SuiteName: TestSuite.Verdict.ToKoreanName(),
        StartedAt: StartedAt,
        FinishedAt: StartedAt.AddSeconds(1.5),
        CasesPath: "cases/sessions.json",
        TargetName: targetName,
        Results:
        [
            new TestResult("S-001", "판정", "봇 A · 오픈 0.03초 뒤 진입", Expected: "추가 확인", Actual: "추가 확인"),
            new TestResult("S-002", "판정", "봇 D · 한 기기 세션 20개", Expected: "추가 확인", Actual: "정상"),
        ]);
}
