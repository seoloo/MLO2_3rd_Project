using System.Globalization;
using System.Text.Json;
using QaRunner.Pipeline;

namespace QaRunner.AppMock;

/// <summary>
/// 가짜 예매·판정 서버가 받은 HTTP 요청 하나.
/// </summary>
/// <param name="Method">요청 방식. 예: POST</param>
/// <param name="Path">요청 경로. 물음표 뒤(쿼리)는 뺀다. 예: /seats/hold</param>
/// <param name="ContentType">Content-Type 헤더 값. 없으면 null</param>
/// <param name="Body">요청 본문 (UTF-8로 읽은 글자)</param>
/// <param name="ScenarioOverride">이 요청만 다른 응답 방식을 쓰라는 표시 (헤더 X-Mock-Scenario 또는 ?mock=). 없으면 null</param>
public sealed record MockHttpRequest(string Method, string Path, string? ContentType, string Body, string? ScenarioOverride = null);

/// <summary>계약 3~5장의 API 중 어느 것인지</summary>
public enum MockEndpoint
{
    /// <summary>계약에 없는 경로</summary>
    Unknown,

    /// <summary>POST /queue/enter 대기열 진입</summary>
    QueueEnter,

    /// <summary>GET /queue/{queueToken} 대기 순번 조회</summary>
    QueueStatus,

    /// <summary>POST /seats/hold 좌석 점유</summary>
    SeatHold,

    /// <summary>POST /payments 결제</summary>
    Payment,

    /// <summary>POST /verifications/{verifyId} 추가 확인</summary>
    Verification,

    /// <summary>POST /events 행동 이벤트 수집</summary>
    Events,

    /// <summary>GET /verdicts/{sessionHash} 판정 조회</summary>
    VerdictLookup,
}

public static class MockEndpointExtensions
{
    /// <summary>화면·보고서에 보여 줄 한글 이름</summary>
    public static string ToKoreanName(this MockEndpoint endpoint) => endpoint switch
    {
        MockEndpoint.QueueEnter => "대기열 진입",
        MockEndpoint.QueueStatus => "대기 순번 조회",
        MockEndpoint.SeatHold => "좌석 점유",
        MockEndpoint.Payment => "결제",
        MockEndpoint.Verification => "추가 확인",
        MockEndpoint.Events => "이벤트 수집",
        MockEndpoint.VerdictLookup => "판정 조회",
        _ => "알 수 없는 요청",
    };
}

/// <summary>
/// 요청을 계약과 비교한 결과.
/// </summary>
/// <param name="Endpoint">어느 API로 보낸 요청인지. 경로가 계약에 없으면 Unknown</param>
/// <param name="RouteValue">경로 안의 값 (queueToken, verifyId, sessionHash). 없으면 null</param>
/// <param name="Problems">계약과 다른 점. 맞으면 빈 목록</param>
/// <param name="ErrorStatusCode">계약과 달라서 돌려줄 상태 코드 (경로 404, 방식 405, 본문 400). 맞으면 null</param>
/// <param name="Body">본문 JSON. 읽지 못했으면 null</param>
/// <param name="SessionHash">요청에 담긴 세션 해시 (본문, 첫 이벤트, 판정 조회 경로 순). 없으면 null</param>
public sealed record MockRequestInspection(
    MockEndpoint Endpoint,
    string? RouteValue,
    IReadOnlyList<string> Problems,
    int? ErrorStatusCode,
    JsonElement? Body,
    string? SessionHash)
{
    /// <summary>계약대로 보낸 요청인지</summary>
    public bool MatchesContract => Problems.Count == 0;
}

/// <summary>
/// 앱이 보낸 요청이 연결 계약 3~5장(예매 API·수집 API·판정 조회 API)과 같은지 확인한다.
/// </summary>
public static class MockRequestInspector
{
    // POST 본문마다 반드시 있어야 하는 문자열 필드 (계약 3장)
    private static readonly Dictionary<MockEndpoint, string[]> RequiredBodyFields = new()
    {
        [MockEndpoint.QueueEnter] = ["showId", "sessionHash", "deviceHash"],
        [MockEndpoint.SeatHold] = ["showId", "seatId", "sessionHash"],
        [MockEndpoint.Payment] = ["holdId", "sessionHash"],
        [MockEndpoint.Verification] = ["code"],
    };

    // 이벤트 한 건에 반드시 있어야 하는 문자열 필드 (계약 4-1장)
    private static readonly string[] RequiredEventFields =
        ["eventId", "type", "origin", "sessionHash", "deviceHash", "showId", "occurredAt", "source"];

    // 이벤트 문제를 너무 많이 적지 않는다 (같은 실수가 이벤트마다 반복되는 경우가 많다)
    private const int MaxEventProblems = 3;

    /// <summary>
    /// 요청을 계약과 비교한다. 다른 점을 모두 모아서 알려 준다.
    /// </summary>
    /// <param name="request">받은 요청</param>
    public static MockRequestInspection Inspect(MockHttpRequest request)
    {
        var problems = new List<string>();
        var (endpoint, expectedMethod, routeValue) = Route(request.Path);

        if (endpoint == MockEndpoint.Unknown)
        {
            problems.Add($"계약에 없는 경로: {request.Path}");
            return new MockRequestInspection(endpoint, null, problems, 404, null, null);
        }

        if (!string.Equals(request.Method, expectedMethod, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"{expectedMethod}가 아니라 {request.Method}로 보냄");
            return new MockRequestInspection(endpoint, routeValue, problems, 405, null, null);
        }

        if (expectedMethod == "GET")
        {
            var sessionFromRoute = endpoint == MockEndpoint.VerdictLookup ? routeValue : null;
            return new MockRequestInspection(endpoint, routeValue, problems, null, null, sessionFromRoute);
        }

        var isJsonContentType = request.ContentType?.Split(';')[0].Trim()
            .Equals("application/json", StringComparison.OrdinalIgnoreCase) == true;
        if (!isJsonContentType)
            problems.Add(request.ContentType is null
                ? "Content-Type 헤더 없음"
                : $"Content-Type이 application/json이 아니라 {request.ContentType}");

        var body = ReadBody(request.Body, problems);
        string? sessionHash = null;
        if (body is { } root)
        {
            if (endpoint == MockEndpoint.Events)
                sessionHash = CheckEvents(root, problems);
            else
                sessionHash = CheckFields(root, RequiredBodyFields[endpoint], "", problems).GetValueOrDefault("sessionHash");
        }

        return new MockRequestInspection(endpoint, routeValue, problems, problems.Count > 0 ? 400 : null, body, sessionHash);
    }

    // 경로로 API를 찾는다: (API, 계약상 요청 방식, 경로 안의 값)
    private static (MockEndpoint Endpoint, string Method, string? RouteValue) Route(string path)
    {
        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments switch
        {
            ["queue", "enter"] => (MockEndpoint.QueueEnter, "POST", null),
            ["queue", var token] => (MockEndpoint.QueueStatus, "GET", Uri.UnescapeDataString(token)),
            ["seats", "hold"] => (MockEndpoint.SeatHold, "POST", null),
            ["payments"] => (MockEndpoint.Payment, "POST", null),
            ["verifications", var verifyId] => (MockEndpoint.Verification, "POST", Uri.UnescapeDataString(verifyId)),
            ["events"] => (MockEndpoint.Events, "POST", null),
            ["verdicts", var sessionHash] => (MockEndpoint.VerdictLookup, "GET", Uri.UnescapeDataString(sessionHash)),
            _ => (MockEndpoint.Unknown, "", null),
        };
    }

    // 본문을 JSON 객체로 읽는다. 형식이 틀리면 문제 목록에 적고 null
    private static JsonElement? ReadBody(string body, List<string> problems)
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
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            problems.Add("본문이 JSON 형식이 아님");
            return null;
        }
    }

    // 필수 문자열 필드가 비어 있지 않은지 본다. 읽은 값을 돌려준다
    private static Dictionary<string, string> CheckFields(JsonElement element, string[] fieldNames, string prefix, List<string> problems)
    {
        var values = new Dictionary<string, string>();
        foreach (var fieldName in fieldNames)
        {
            if (!element.TryGetProperty(fieldName, out var value))
                problems.Add($"{prefix}{fieldName} 필드 없음");
            else if (value.ValueKind != JsonValueKind.String)
                problems.Add($"{prefix}{fieldName}가 문자열이 아님");
            else if (string.IsNullOrWhiteSpace(value.GetString()))
                problems.Add($"{prefix}{fieldName}가 비어 있음");
            else
                values[fieldName] = value.GetString()!;
        }
        return values;
    }

    // 수집 API 본문 { "events": [ ... ] }를 계약 4-1장과 비교한다. 첫 이벤트의 세션 해시를 돌려준다
    private static string? CheckEvents(JsonElement root, List<string> problems)
    {
        if (!root.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
        {
            problems.Add("본문에 events 배열 없음");
            return null;
        }
        if (events.GetArrayLength() == 0)
        {
            problems.Add("events 배열이 비어 있음");
            return null;
        }

        string? firstSessionHash = null;
        var eventProblems = new List<string>();
        var index = 0;
        foreach (var contractEvent in events.EnumerateArray())
        {
            var prefix = $"events[{index++}].";
            if (contractEvent.ValueKind != JsonValueKind.Object)
            {
                eventProblems.Add($"{prefix[..^1]}이 객체가 아님");
                continue;
            }

            var values = CheckFields(contractEvent, RequiredEventFields, prefix, eventProblems);
            firstSessionHash ??= values.GetValueOrDefault("sessionHash");
            if (values.TryGetValue("type", out var type) && !EventTypes.IsKnown(type))
                eventProblems.Add($"{prefix}type '{type}'은 계약에 없음");
            if (values.TryGetValue("origin", out var origin) && origin is not (EventTypes.ClientOrigin or EventTypes.ServerOrigin))
                eventProblems.Add($"{prefix}origin은 client 또는 server여야 함");
            if (values.TryGetValue("source", out var source) && source is not ("app" or "qa"))
                eventProblems.Add($"{prefix}source는 app 또는 qa여야 함");
            if (values.TryGetValue("occurredAt", out var occurredAt)
                && !DateTimeOffset.TryParse(occurredAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
                eventProblems.Add($"{prefix}occurredAt이 ISO 8601 시각이 아님");
        }

        problems.AddRange(eventProblems.Take(MaxEventProblems));
        if (eventProblems.Count > MaxEventProblems)
            problems.Add($"그 밖에 이벤트 문제 {eventProblems.Count - MaxEventProblems}개");
        return firstSessionHash;
    }
}
