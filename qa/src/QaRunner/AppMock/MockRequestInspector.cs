using System.Text.Json;

namespace QaRunner.AppMock;

/// <summary>
/// 가짜 판정 서버가 받은 HTTP 요청 하나.
/// </summary>
/// <param name="Method">요청 방식. 예: POST</param>
/// <param name="Path">요청 경로. 물음표 뒤(쿼리)는 뺀다. 예: /checks</param>
/// <param name="ContentType">Content-Type 헤더 값. 없으면 null</param>
/// <param name="Body">요청 본문 (UTF-8로 읽은 글자)</param>
public sealed record MockHttpRequest(string Method, string Path, string? ContentType, string Body);

/// <summary>
/// 요청이 계약과 맞는지 살펴본 결과.
/// </summary>
/// <param name="Problems">계약과 다른 점. 맞으면 빈 목록</param>
/// <param name="Message">본문에서 읽은 문자 내용. 읽지 못했으면 null</param>
/// <param name="ErrorStatusCode">계약과 달라서 돌려줄 상태 코드 (경로 404, 방식 405, 본문 400). 맞으면 null</param>
public sealed record MockRequestInspection(IReadOnlyList<string> Problems, string? Message, int? ErrorStatusCode)
{
    /// <summary>계약대로 보낸 요청인지</summary>
    public bool MatchesContract => Problems.Count == 0;
}

/// <summary>
/// 앱이 보낸 요청이 연결 계약 3장(판정 API)과 같은지 확인한다.
///   요청: POST /checks, Content-Type: application/json, 본문 { "message": "비어 있지 않은 문자" }
/// </summary>
public static class MockRequestInspector
{
    /// <summary>판정 요청 경로</summary>
    public const string CheckPath = "/checks";

    /// <summary>
    /// 요청을 계약과 비교한다. 다른 점을 모두 모아서 알려 준다.
    /// </summary>
    /// <param name="request">받은 요청</param>
    public static MockRequestInspection Inspect(MockHttpRequest request)
    {
        var problems = new List<string>();
        int? errorStatusCode = null;

        if (!string.Equals(request.Path, CheckPath, StringComparison.Ordinal))
        {
            problems.Add($"경로가 {CheckPath}가 아니라 {request.Path}");
            errorStatusCode = 404;
        }

        if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"POST가 아니라 {request.Method}로 보냄");
            errorStatusCode ??= 405;
        }

        var isJsonContentType = request.ContentType?.Split(';')[0].Trim()
            .Equals("application/json", StringComparison.OrdinalIgnoreCase) == true;
        if (!isJsonContentType)
            problems.Add(request.ContentType is null
                ? "Content-Type 헤더 없음"
                : $"Content-Type이 application/json이 아니라 {request.ContentType}");

        var message = ReadMessage(request.Body, problems);

        if (problems.Count > 0)
            errorStatusCode ??= 400;

        return new MockRequestInspection(problems, message, errorStatusCode);
    }

    // 본문에서 message 값을 읽는다. 형식이 틀리면 문제 목록에 적고 null
    private static string? ReadMessage(string body, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            problems.Add("본문이 비어 있음");
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                problems.Add("본문이 JSON 객체({ })가 아님");
                return null;
            }

            if (!document.RootElement.TryGetProperty("message", out var messageElement))
            {
                problems.Add("본문에 message 필드 없음");
                return null;
            }

            if (messageElement.ValueKind != JsonValueKind.String)
            {
                problems.Add("message가 문자열이 아님");
                return null;
            }

            var message = messageElement.GetString()!;
            if (string.IsNullOrWhiteSpace(message))
                problems.Add("message가 비어 있음");
            return message;
        }
        catch (JsonException)
        {
            problems.Add("본문이 JSON 형식이 아님");
            return null;
        }
    }
}
