using QaRunner.Contracts;

namespace QaRunner.Collaboration;

/// <summary>
/// 외부 AI 없이 화면과 데이터 흐름을 확인하는 합성 세션 생성기. 라벨은 검수 전으로 표시한다.
/// Runpod이 연결되면 "새 유형 봇 시나리오"를 만드는 RunpodCaseGenerator로 바꿔 끼운다.
/// </summary>
public sealed class DemoCaseGenerator : ICaseGenerator
{
    public Task<GeneratedCaseSet> GenerateAsync(CaseGenerationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Scenario) || request.Count is < 1 or > 4)
            throw new ArgumentException("예제 생성은 시나리오와 1~4개의 사례 수를 받습니다.");

        GeneratedCase[] examples =
        [
            new("DEMO-001", "봇 A", "오픈 0.04초 뒤 진입 · 0.1초 간격으로 일정하게 클릭", "VERIFY",
                [Step("queue_enter", 40), Step("seat_click", 140, 212, 344), Step("seat_click", 240, 230, 344),
                 Step("seat_hold", 340), Step("payment_request", 440)],
                "사람이 낼 수 없는 진입 속도와 일정한 클릭 간격"),
            new("DEMO-002", "사람", "오픈 1.6초 뒤 진입 · 좌석을 고민하다 되돌아감", "NORMAL",
                [Step("page_view", 0, screen: "show"), Step("queue_enter", 1600), Step("page_view", 9800, screen: "seat"),
                 Step("seat_click", 13200, 180, 300), Step("back_navigation", 15900), Step("seat_click", 19400, 260, 312),
                 Step("seat_hold", 20100), Step("payment_request", 34500)],
                "반응 시간만큼 늦고 간격이 고르지 않은 사람 행동"),
            new("DEMO-003", "새 유형 봇", "사람 속도로 들어오지만 좌석 화면 없이 점유 요청", "VERIFY",
                [Step("queue_enter", 1300), Step("seat_hold", 4100), Step("payment_request", 4600)],
                "화면 기록 없이 서버 요청만 있는 세션 (화면 흐름 건너뜀)"),
            new("DEMO-004", "사람", "빠른 숙련자 · 오픈 0.7초 뒤 진입", "NORMAL",
                [Step("page_view", 0, screen: "show"), Step("queue_enter", 700), Step("page_view", 6200, screen: "seat"),
                 Step("seat_click", 7100, 300, 280), Step("seat_hold", 7600), Step("payment_request", 15800)],
                "빠르지만 화면을 모두 거치고 간격이 고르지 않음"),
        ];
        return Task.FromResult(new GeneratedCaseSet(
            Guid.NewGuid().ToString("N"), "DemoCaseGenerator (AI 호출 없음)", "demo-v2",
            examples.Take(request.Count).ToArray()));
    }

    private static GeneratedStep Step(string type, int atMs, int? x = null, int? y = null, string? screen = null) =>
        new(type, atMs, screen, x, y);
}
