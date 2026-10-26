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
        CasesPath: "cases/rules.json",
        TargetName: targetName,
        Results:
        [
            new TestResult("R-001", "판정", "인증번호를 알려주세요", Expected: "고위험", Actual: "고위험"),
            new TestResult("R-002", "판정", "앱을 설치하세요", Expected: "고위험", Actual: "신호 미발견"),
        ]);
}
