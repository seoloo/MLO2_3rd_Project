namespace QaRunner.Verdicts;

/// <summary>
/// 판정 값의 영문 코드와 한글 표시를 서로 바꾸고, 탐지율·오탐률을 셀 때 쓰는 묶음을 정한다 (docs/contract.md 2장).
/// API·이벤트·DB는 영문 코드(VERIFY 등)를 쓰고, QA 사례 파일과 보고서는 한글(추가 확인 등)을 쓴다.
/// </summary>
public static class VerdictCodes
{
    /// <summary>정상·관찰 묶음 이름. 사람은 이 묶음이어야 통과다</summary>
    public const string PassThroughGroup = "정상 처리";

    /// <summary>추가 확인·보류 묶음 이름. 봇은 이 묶음이어야 탐지한 것이다</summary>
    public const string CheckGroup = "추가 확인 이상";

    private static readonly Dictionary<string, string> KoreanByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NORMAL"] = "정상",
        ["WATCH"] = "관찰",
        ["VERIFY"] = "추가 확인",
        ["HOLD"] = "보류",
    };

    private static readonly Dictionary<string, string> CodeByKorean =
        KoreanByCode.ToDictionary(codeAndKorean => codeAndKorean.Value, codeAndKorean => codeAndKorean.Key);

    /// <summary>계약에 있는 판정 코드 네 가지 (점수가 낮은 단계부터)</summary>
    public static IReadOnlyList<string> AllCodes { get; } = ["NORMAL", "WATCH", "VERIFY", "HOLD"];

    /// <summary>
    /// 영문 코드를 한글 판정으로 바꾼다. 이미 한글이거나 모르는 값이면 그대로 돌려준다
    /// (모르는 값은 어느 묶음에도 들지 않아 채점에서 실패로 드러난다).
    /// </summary>
    public static string ToKorean(string verdict) =>
        KoreanByCode.TryGetValue(verdict, out var koreanVerdict) ? koreanVerdict : verdict;

    /// <summary>
    /// 한글 판정을 영문 코드로 바꾼다. 이미 코드이거나 모르는 값이면 그대로 돌려준다.
    /// </summary>
    public static string ToCode(string verdict) =>
        CodeByKorean.TryGetValue(verdict, out var verdictCode) ? verdictCode : verdict;

    /// <summary>계약에 있는 판정 값(코드 또는 한글)인지</summary>
    public static bool IsKnown(string verdict) => KoreanByCode.ContainsKey(ToCode(verdict));

    /// <summary>
    /// 판정이 속한 묶음. 정상·관찰은 "정상 처리", 추가 확인·보류는 "추가 확인 이상", 모르는 값은 null.
    /// </summary>
    public static string? ToGroup(string verdict) => ToCode(verdict).ToUpperInvariant() switch
    {
        "NORMAL" or "WATCH" => PassThroughGroup,
        "VERIFY" or "HOLD" => CheckGroup,
        _ => null,
    };
}
