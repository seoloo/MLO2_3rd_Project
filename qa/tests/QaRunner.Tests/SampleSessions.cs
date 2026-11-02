using QaRunner.Models;

namespace QaRunner.Tests;

/// <summary>
/// 여러 시험에서 함께 쓰는 짧은 세션 사례.
/// </summary>
public static class SampleSessions
{
    /// <summary>오픈 1.4초 뒤 대기열에 들어와 좌석을 고르고 결제하는 사람</summary>
    public static SessionCase Human(string caseId = "S-H", string expectedVerdict = "정상") => new(
        caseId, "사람", "오픈 1.4초 뒤 진입", expectedVerdict,
        [
            new SessionStep("page_view", 0, "show"),
            new SessionStep("queue_enter", 1400),
            new SessionStep("seat_click", 11200, X: 212, Y: 344),
            new SessionStep("seat_hold", 11800),
            new SessionStep("payment_request", 25000),
        ]);

    /// <summary>오픈 0.03초 뒤 들어와 0.1초 간격으로 누르는 봇</summary>
    public static SessionCase Bot(string caseId = "S-B", string actor = "봇 A", string expectedVerdict = "추가 확인", int parallelSessions = 1) =>
        new(caseId, actor, "오픈 0.03초 뒤 진입 · 0.1초 간격", expectedVerdict,
        [
            new SessionStep("queue_enter", 30),
            new SessionStep("seat_click", 130, X: 200, Y: 300),
            new SessionStep("seat_hold", 230),
            new SessionStep("payment_request", 330),
        ])
        {
            ParallelSessions = parallelSessions,
        };
}
