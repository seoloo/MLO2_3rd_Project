using QaRunner.Models;

namespace QaRunner.AppMock;

/// <summary>
/// 가짜 예매·판정 서버가 받은 요청 한 건의 기록. 데스크탑 앱의 받은 요청 목록과 보고서에 쓴다.
/// </summary>
/// <param name="Number">받은 순서 (1부터)</param>
/// <param name="ReceivedAt">받은 시각</param>
/// <param name="Method">요청 방식</param>
/// <param name="Path">요청 경로</param>
/// <param name="Endpoint">어느 API로 보낸 요청인지</param>
/// <param name="SessionPreview">요청에 담긴 세션 해시 앞부분. 없으면 빈 문자열</param>
/// <param name="Scenario">돌려준 응답 방식</param>
/// <param name="StatusCode">돌려준 상태 코드</param>
/// <param name="ResponseSummary">돌려준 응답 한 줄. 예: "추가 확인 필요 · 202", "계약 오류 · 400"</param>
/// <param name="Problems">계약과 다른 점. 맞으면 빈 목록</param>
public sealed record MockRequestRecord(
    int Number,
    DateTimeOffset ReceivedAt,
    string Method,
    string Path,
    MockEndpoint Endpoint,
    string SessionPreview,
    MockScenario Scenario,
    int StatusCode,
    string ResponseSummary,
    IReadOnlyList<string> Problems)
{
    /// <summary>세션 해시 앞부분으로 남길 최대 글자 수</summary>
    public const int PreviewLength = 24;

    /// <summary>계약대로 보낸 요청인지</summary>
    public bool MatchesContract => Problems.Count == 0;

    /// <summary>세션 해시를 기록에 남길 모양으로 줄인다</summary>
    public static string ToPreview(string? sessionHash)
    {
        if (string.IsNullOrWhiteSpace(sessionHash))
            return "";
        var trimmed = sessionHash.Trim();
        return trimmed.Length <= PreviewLength ? trimmed : trimmed[..PreviewLength] + "…";
    }
}

/// <summary>
/// 가짜 예매·판정 서버가 받은 요청 기록을 보고서용 채점 결과로 바꾼다.
/// 요청 한 건이 확인 항목 하나이고, 계약 3~5장과 같은 형식으로 보냈으면 통과다.
/// </summary>
public static class MockRequestGrader
{
    /// <summary>요청마다 확인하는 항목 이름</summary>
    public const string RequestCheckName = "요청 형식 (계약 3~5장)";

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
                    Input: "서버를 켜 둔 동안 앱이 보낸 예매·이벤트 요청", Expected: "1건 이상", Actual: "0건"),
            ];
        }

        return records
            .OrderBy(record => record.Number)
            .Select(record => new TestResult(
                CaseId: $"요청-{record.Number:000}",
                CheckName: RequestCheckName,
                Input: $"{record.Endpoint.ToKoreanName()} · {record.Method} {record.Path}"
                    + (record.SessionPreview.Length > 0 ? $" · 세션 {record.SessionPreview}" : "")
                    + $" → 돌려준 응답: {record.ResponseSummary}",
                Expected: MatchesContractValue,
                Actual: record.MatchesContract ? MatchesContractValue : string.Join(" · ", record.Problems)))
            .ToList();
    }
}
