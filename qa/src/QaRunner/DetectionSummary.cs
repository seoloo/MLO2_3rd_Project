using QaRunner.Models;
using QaRunner.Verdicts;

namespace QaRunner;

/// <summary>
/// 판정 시험 결과를 행위자별로 모아 탐지율·오탐률을 계산한다 (기획서 21쪽 · docs/contract.md 9장).
/// 봇이 "추가 확인 이상"이면 탐지, 사람이 "추가 확인 이상"이면 오탐이다. 응답이 없던 사례는 세지 않는다.
/// </summary>
public static class DetectionSummary
{
    public const string RegularBotDetectionCheckName = "봇 탐지율 (A·B·D)";
    public const string BrowserBotDetectionCheckName = "봇 C 탐지율";
    public const string NewBotDetectionCheckName = "새 유형 봇 탐지율";
    public const string HumanFalsePositiveCheckName = "사람 오탐률";

    /// <summary>봇 A·B·D 탐지율 초기 목표(%)</summary>
    public const double RegularBotDetectionTarget = 90;

    /// <summary>봇 C(브라우저 흉내) 탐지율 초기 목표(%)</summary>
    public const double BrowserBotDetectionTarget = 70;

    /// <summary>사람 오탐률 초기 목표(%)</summary>
    public const double HumanFalsePositiveTarget = 1;

    /// <summary>오탐 0건으로 "1% 이하"를 95% 신뢰로 말하려면 필요한 사람 세션 수 (3 / 0.01)</summary>
    public const int SessionsNeededForOnePercent = 300;

    /// <summary>
    /// 사례별 결과에서 탐지율·오탐률 항목을 만든다. 해당 행위자 사례가 없으면 그 항목은 만들지 않는다.
    /// </summary>
    /// <param name="sessionCases">채점한 세션 사례</param>
    /// <param name="caseResults">VerdictChecker가 낸 사례별 결과 (사례 번호로 연결)</param>
    public static List<TestResult> Build(IReadOnlyList<SessionCase> sessionCases, IReadOnlyList<TestResult> caseResults)
    {
        var resultsByCaseId = caseResults.ToDictionary(result => result.CaseId);

        // 응답을 받은 사례만: (사례, 추가 확인 이상으로 판정됐는지)
        var judged = sessionCases
            .Where(sessionCase => resultsByCaseId.TryGetValue(sessionCase.CaseId, out var result) && !result.IsSystemFailure)
            .Select(sessionCase => (Case: sessionCase,
                Flagged: VerdictCodes.ToGroup(resultsByCaseId[sessionCase.CaseId].Actual) == VerdictCodes.CheckGroup))
            .ToList();

        var summary = new List<TestResult>();
        AddRate(summary, RegularBotDetectionCheckName, judged.Where(item => IsActor(item.Case, "봇 A", "봇 B", "봇 D")).ToList(),
            RegularBotDetectionTarget, higherIsBetter: true, "추가 확인 이상으로 판정된 봇 세션 비율");
        AddRate(summary, BrowserBotDetectionCheckName, judged.Where(item => IsActor(item.Case, "봇 C")).ToList(),
            BrowserBotDetectionTarget, higherIsBetter: true, "화면을 직접 누르는 봇 · 가장 어려운 유형");
        AddRate(summary, NewBotDetectionCheckName, judged.Where(item => IsActor(item.Case, "새 유형")).ToList(),
            target: null, higherIsBetter: true, "학습에 쓰지 않은 봇 · 목표 없이 결과만 공개");

        var humans = judged.Where(item => item.Case.IsHuman).ToList();
        AddRate(summary, HumanFalsePositiveCheckName, humans, HumanFalsePositiveTarget, higherIsBetter: false,
            "추가 확인 이상으로 잘못 판정된 사람 세션 비율"
            + (humans.Count < SessionsNeededForOnePercent
                ? $" · 표본 {humans.Count}개라 참고값 (1% 이하를 말하려면 사람 세션 약 {SessionsNeededForOnePercent}개 필요)"
                : ""));
        return summary;
    }

    private static bool IsActor(SessionCase sessionCase, params string[] actorPrefixes) =>
        actorPrefixes.Any(prefix => sessionCase.Actor.StartsWith(prefix, StringComparison.Ordinal));

    // 비율 항목 하나를 만든다. 목표가 없으면(새 유형 봇) 결과만 보여 주고 통과로 둔다
    private static void AddRate(List<TestResult> summary, string checkName, List<(SessionCase Case, bool Flagged)> items,
        double? target, bool higherIsBetter, string description)
    {
        if (items.Count == 0)
            return;

        var flaggedCount = items.Count(item => item.Flagged);
        var rate = flaggedCount * 100.0 / items.Count;
        var expected = target is null ? "목표 없음 (결과 공개)" : higherIsBetter ? $"{target:0}% 이상" : $"{target:0}% 이하";
        var meetsTarget = target is null || (higherIsBetter ? rate >= target : rate <= target);

        summary.Add(new TestResult(TestResult.WholeRunCaseId, checkName,
            Input: $"{description} · 세션 {items.Count}개",
            Expected: expected,
            Actual: $"{rate:0.#}% ({flaggedCount}/{items.Count})",
            MeetsTarget: meetsTarget));
    }
}
