using QaRunner.Models;

namespace QaRunner.Tests;

/// <summary>
/// 여러 테스트에서 같이 쓰는 예시 실행 기록.
/// </summary>
internal static class SampleTestRuns
{
    public static readonly DateTimeOffset StartedAt = new(2026, 10, 26, 14, 30, 15, TimeSpan.FromHours(9));

    /// <summary>통과 1건, 실패 1건이 들어 있는 실행 기록</summary>
    public static TestRun OnePassedOneFailed(string verdictServiceName = "FakeVerdictService") => new(
        RunId: TestRun.CreateRunId(StartedAt),
        StartedAt: StartedAt,
        FinishedAt: StartedAt.AddSeconds(1.5),
        TestCaseFilePath: "cases/rules.json",
        VerdictServiceName: verdictServiceName,
        Results:
        [
            new TestResult("R-001", "인증번호를 알려주세요", ExpectedVerdict: "고위험", ActualVerdict: "고위험"),
            new TestResult("R-002", "앱을 설치하세요", ExpectedVerdict: "고위험", ActualVerdict: "신호 미발견"),
        ]);
}
