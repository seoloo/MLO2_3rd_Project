using QaRunner.Models;
using QaRunner.Verdicts;

namespace QaRunner.AppMock;

/// <summary>
/// 가짜 판정 서버가 받은 판정 요청 한 건의 기록. 데스크탑 앱의 받은 요청 목록과 보고서에 쓴다.
/// </summary>
/// <param name="Number">받은 순서 (1부터)</param>
/// <param name="ReceivedAt">받은 시각</param>
/// <param name="Method">요청 방식</param>
/// <param name="Path">요청 경로</param>
/// <param name="MessagePreview">문자 앞부분 (긴 문자는 줄여서 남긴다). 문자를 읽지 못했으면 빈 문자열</param>
/// <param name="Scenario">돌려준 응답 방식</param>
/// <param name="StatusCode">돌려준 상태 코드</param>
/// <param name="VerdictCode">돌려준 판정 코드. 오류 응답이면 null</param>
/// <param name="Problems">계약과 다른 점. 맞으면 빈 목록</param>
public sealed record MockRequestRecord(
    int Number,
    DateTimeOffset ReceivedAt,
    string Method,
    string Path,
    string MessagePreview,
    MockScenario Scenario,
    int StatusCode,
    string? VerdictCode,
    IReadOnlyList<string> Problems)
{
    /// <summary>문자 앞부분으로 남길 최대 글자 수</summary>
    public const int PreviewLength = 40;

    /// <summary>계약대로 보낸 요청인지</summary>
    public bool MatchesContract => Problems.Count == 0;

    /// <summary>
    /// 돌려준 응답을 한 줄로. 예: "고위험 · 200", "3초 늦은 응답 · 주의 · 200", "서버 오류 500".
    /// 계약과 다른 요청에는 응답 방식 대신 오류 응답을 돌려줬으므로 "계약 오류 · 404"처럼 적는다.
    /// </summary>
    public string ResponseSummary
    {
        get
        {
            if (!MatchesContract)
                return $"계약 오류 · {StatusCode}";

            var scenarioName = Scenario.ToKoreanName();
            if (VerdictCode is null)
                return scenarioName.EndsWith(StatusCode.ToString(), StringComparison.Ordinal) ? scenarioName : $"{scenarioName} · {StatusCode}";

            // 판정만 정하는 응답 방식(자동·항상 ○○)은 판정만, 나머지는 응답 방식 이름도 함께 적는다
            var verdictText = $"{VerdictCodes.ToKorean(VerdictCode)} · {StatusCode}";
            var decidesVerdictOnly = Scenario is MockScenario.Auto or MockScenario.HighRisk or MockScenario.Caution
                or MockScenario.NoSignal or MockScenario.Undecided;
            return decidesVerdictOnly ? verdictText : $"{scenarioName} · {verdictText}";
        }
    }

    /// <summary>문자를 기록에 남길 모양으로 줄인다. 줄바꿈은 띄어쓰기로 바꾼다</summary>
    public static string ToPreview(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return "";
        var oneLine = message.ReplaceLineEndings(" ").Trim();
        return oneLine.Length <= PreviewLength ? oneLine : oneLine[..PreviewLength] + "…";
    }
}

/// <summary>
/// 가짜 판정 서버가 받은 요청 기록을 보고서용 채점 결과로 바꾼다.
/// 요청 한 건이 확인 항목 하나이고, 계약 3장과 같은 형식으로 보냈으면 통과다.
/// </summary>
public static class MockRequestGrader
{
    /// <summary>요청마다 확인하는 항목 이름</summary>
    public const string RequestCheckName = "요청 형식 (계약 3장)";

    /// <summary>계약과 같은 요청의 실제 값이자 정답</summary>
    public const string MatchesContractValue = "계약과 같음";

    /// <summary>요청을 한 건도 받지 못했을 때 남기는 확인 항목 이름</summary>
    public const string NoRequestCheckName = "앱 요청 받기";

    /// <summary>
    /// 받은 요청을 받은 순서대로 채점한다. 한 건도 없으면 "앱 요청 받기" 항목 하나를 실패로 남긴다.
    /// </summary>
    /// <param name="records">서버가 받은 요청 기록</param>
    public static List<TestResult> Grade(IReadOnlyList<MockRequestRecord> records)
    {
        if (records.Count == 0)
        {
            return
            [
                new TestResult(TestResult.WholeRunCaseId, NoRequestCheckName,
                    Input: "서버를 켜 둔 동안 앱이 보낸 판정 요청", Expected: "1건 이상", Actual: "0건"),
            ];
        }

        return records
            .OrderBy(record => record.Number)
            .Select(record => new TestResult(
                CaseId: $"요청-{record.Number:000}",
                CheckName: RequestCheckName,
                Input: $"{record.Method} {record.Path} · \"{record.MessagePreview}\" → 돌려준 응답: {record.ResponseSummary}",
                Expected: MatchesContractValue,
                Actual: record.MatchesContract ? MatchesContractValue : string.Join(" · ", record.Problems)))
            .ToList();
    }
}
