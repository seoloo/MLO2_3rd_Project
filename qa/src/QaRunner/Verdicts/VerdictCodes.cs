namespace QaRunner.Verdicts;

/// <summary>
/// 판정 값의 영문 코드와 한글 표시를 서로 바꾼다 (docs/contract.md 2장).
/// API·이벤트는 영문 코드(HIGH_RISK 등)를 쓰고, QA 입력값 파일과 보고서는 한글(고위험 등)을 쓴다.
/// </summary>
public static class VerdictCodes
{
    private static readonly Dictionary<string, string> KoreanByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HIGH_RISK"] = "고위험",
        ["CAUTION"] = "주의",
        ["NO_SIGNAL"] = "신호 미발견",
        ["UNDECIDED"] = "판단 보류",
    };

    private static readonly Dictionary<string, string> CodeByKorean =
        KoreanByCode.ToDictionary(codeAndKorean => codeAndKorean.Value, codeAndKorean => codeAndKorean.Key);

    /// <summary>
    /// 영문 코드를 한글 판정으로 바꾼다. 이미 한글이거나 모르는 값이면 그대로 돌려준다
    /// (모르는 값은 정답과 달라서 채점에서 실패로 드러난다).
    /// </summary>
    public static string ToKorean(string verdict) =>
        KoreanByCode.TryGetValue(verdict, out var koreanVerdict) ? koreanVerdict : verdict;

    /// <summary>
    /// 한글 판정을 영문 코드로 바꾼다. 이미 코드이거나 모르는 값이면 그대로 돌려준다.
    /// </summary>
    public static string ToCode(string verdict) =>
        CodeByKorean.TryGetValue(verdict, out var verdictCode) ? verdictCode : verdict;
}
