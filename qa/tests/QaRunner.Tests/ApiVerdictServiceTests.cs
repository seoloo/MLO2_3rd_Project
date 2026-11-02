using System.Net;
using System.Text;
using System.Text.Json;
using QaRunner.Verdicts;

namespace QaRunner.Tests;

/// <summary>
/// 실제 서버 없이, 미리 정한 응답을 돌려주는 가짜 HTTP 처리기로 ApiVerdictService를 확인한다.
/// 수집 API(POST /events)로 세션의 행동을 보내고, 판정 조회 API(GET /verdicts/{세션})를 판정이 나올 때까지 부르는지 본다.
/// </summary>
public class ApiVerdictServiceTests
{
    /// <summary>
    /// 받은 요청을 모두 기록하고, 수집 API에는 정해 둔 상태를, 판정 조회에는 정해 둔 응답을 차례로 돌려주는 시험용 HTTP 처리기.
    /// 판정 조회 응답 목록이 끝나면 마지막 응답을 계속 돌려준다.
    /// </summary>
    private sealed class ScriptedHttpHandler(HttpStatusCode eventsStatus, params (HttpStatusCode Status, string Body)[] verdictResponses)
        : HttpMessageHandler
    {
        private int _verdictCallCount;

        public List<(HttpMethod Method, string Uri, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.ToString(), body));

            var (status, responseBody) = request.Method == HttpMethod.Post
                ? (eventsStatus, """{ "accepted": 1 }""")
                : verdictResponses[Math.Min(_verdictCallCount++, verdictResponses.Length - 1)];
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        }
    }

    private static ApiVerdictService CreateService(ScriptedHttpHandler httpHandler) => new(
        new HttpClient(httpHandler) { BaseAddress = new Uri("http://qa-test.local/api/") },
        verdictWaitLimit: TimeSpan.FromMilliseconds(300), pollInterval: TimeSpan.FromMilliseconds(20));

    private static (HttpStatusCode, string) Verdict(string code) => (HttpStatusCode.OK, $$"""{ "sessionHash": "x", "verdict": "{{code}}" }""");

    private static readonly (HttpStatusCode, string) NotYet = (HttpStatusCode.NotFound, """{ "error": "판정 전" }""");

    [Fact]
    public async Task Posts_session_events_then_polls_verdict_until_it_appears()
    {
        var httpHandler = new ScriptedHttpHandler(HttpStatusCode.Accepted, NotYet, NotYet, Verdict("VERIFY"));
        var verdictService = CreateService(httpHandler);

        var verdict = await verdictService.GetVerdictAsync(SampleSessions.Bot("S-004"), "run-1", "run-1-S-004");

        // 계약대로 영문 코드가 오면 사례 파일의 정답(한글)과 비교할 수 있게 한글로 바꾼다
        Assert.Equal("추가 확인", verdict);
        var (method, uri, body) = httpHandler.Requests[0];
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("http://qa-test.local/api/events", uri);
        Assert.Equal(["http://qa-test.local/api/verdicts/qa-run-1-S-004"],
            httpHandler.Requests.Skip(1).Select(request => request.Uri).Distinct());
        Assert.Equal(3, httpHandler.Requests.Count(request => request.Method == HttpMethod.Get));

        // 본문은 계약 4-1장의 이벤트 배열이고, 행위자·정답은 들어 있지 않다
        using var document = JsonDocument.Parse(body!);
        var events = document.RootElement.GetProperty("events");
        Assert.Equal(4, events.GetArrayLength());
        var first = events[0];
        Assert.Equal("queue_enter", first.GetProperty("type").GetString());
        Assert.Equal("server", first.GetProperty("origin").GetString());
        Assert.Equal("qa-run-1-S-004", first.GetProperty("sessionHash").GetString());
        Assert.Equal("qa", first.GetProperty("source").GetString());
        Assert.Equal("S-004", first.GetProperty("caseId").GetString());
        Assert.DoesNotContain("봇", body);
        Assert.DoesNotContain("추가 확인", body);

        // 모든 행동이 이미 지난 일이 되도록 보낸다 (미래 시각이 아님)
        var lastOccurredAt = DateTimeOffset.Parse(events[3].GetProperty("occurredAt").GetString()!);
        Assert.True(lastOccurredAt < DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Parallel_sessions_share_a_device_and_the_first_session_is_judged()
    {
        var httpHandler = new ScriptedHttpHandler(HttpStatusCode.Accepted, Verdict("HOLD"));

        var verdict = await CreateService(httpHandler).GetVerdictAsync(
            SampleSessions.Bot("S-007", "봇 D", parallelSessions: 3), "run-1", "run-1-S-007");

        Assert.Equal("보류", verdict);
        using var document = JsonDocument.Parse(httpHandler.Requests[0].Body!);
        var events = document.RootElement.GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(12, events.Count);
        Assert.Equal(3, events.Select(item => item.GetProperty("sessionHash").GetString()).Distinct().Count());
        Assert.Single(events.Select(item => item.GetProperty("deviceHash").GetString()).Distinct());
        Assert.EndsWith("/verdicts/qa-run-1-S-007-01", httpHandler.Requests[1].Uri);
    }

    [Fact]
    public async Task Throws_when_collection_api_returns_error_status()
    {
        var verdictService = CreateService(new ScriptedHttpHandler(HttpStatusCode.InternalServerError, Verdict("NORMAL")));

        await Assert.ThrowsAsync<HttpRequestException>(() => verdictService.GetVerdictAsync(SampleSessions.Human(), "run-1", "k"));
    }

    [Fact]
    public async Task Throws_when_verdict_lookup_returns_error_status()
    {
        var verdictService = CreateService(new ScriptedHttpHandler(HttpStatusCode.Accepted, (HttpStatusCode.ServiceUnavailable, "{}")));

        await Assert.ThrowsAsync<HttpRequestException>(() => verdictService.GetVerdictAsync(SampleSessions.Human(), "run-1", "k"));
    }

    [Fact]
    public async Task Throws_when_verdict_does_not_appear_in_time()
    {
        var verdictService = CreateService(new ScriptedHttpHandler(HttpStatusCode.Accepted, NotYet));

        var exception = await Assert.ThrowsAsync<VerdictServiceException>(
            () => verdictService.GetVerdictAsync(SampleSessions.Human(), "run-1", "k"));
        Assert.Contains("판정이 나오지 않았습니다", exception.Message);
    }

    [Fact]
    public async Task Throws_when_response_has_no_verdict()
    {
        var verdictService = CreateService(new ScriptedHttpHandler(HttpStatusCode.Accepted, (HttpStatusCode.OK, """{ "result": "ok" }""")));

        await Assert.ThrowsAsync<VerdictServiceException>(() => verdictService.GetVerdictAsync(SampleSessions.Human(), "run-1", "k"));
    }

    [Theory]
    [InlineData("http://localhost:8000", "http://localhost:8000/")]
    [InlineData("http://localhost:8000/api", "http://localhost:8000/api/")]
    [InlineData("https://staging.example.test/", "https://staging.example.test/")]
    public void Adds_trailing_slash_to_api_address(string apiBaseUrl, string expectedBaseAddress)
    {
        using var httpClient = ApiVerdictService.CreateHttpClient(apiBaseUrl);

        Assert.Equal(expectedBaseAddress, httpClient.BaseAddress!.ToString());
    }

    [Theory]
    [InlineData("localhost:8000")]
    [InlineData("ftp://localhost")]
    public void Rejects_address_without_http_scheme(string apiBaseUrl)
    {
        Assert.Throws<ArgumentException>(() => ApiVerdictService.CreateHttpClient(apiBaseUrl));
    }
}
