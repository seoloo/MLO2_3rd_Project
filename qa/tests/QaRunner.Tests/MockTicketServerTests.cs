using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using QaRunner.AppMock;
using QaRunner.Models;
using QaRunner.Verdicts;

namespace QaRunner.Tests;

/// <summary>
/// 앱 영역의 가짜 예매·판정 서버를 확인한다: 응답 방식, 요청 계약 검사, 예매 흐름(대기열 → 좌석 → 결제 → 추가 확인),
/// 판정 조회, 실제 HTTP 왕복, 포트, 보고서 채점.
/// </summary>
public class MockTicketServerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 11, 0, 0, TimeSpan.Zero);
    private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), $"qa-mock-test-{Guid.NewGuid():N}");

    public MockTicketServerTests() => Directory.CreateDirectory(_tempFolder);

    public void Dispose() => Directory.Delete(_tempFolder, recursive: true);

    // ---------- 응답 방식 ----------

    [Fact]
    public void Every_scenario_has_korean_name_description_and_app_expectation()
    {
        Assert.All(Enum.GetValues<MockScenario>(), scenario =>
        {
            Assert.NotEqual(scenario.ToString(), scenario.ToKoreanName());
            Assert.NotEmpty(scenario.DescribeResponse());
            Assert.NotEmpty(scenario.DescribeAppExpectation());
            Assert.Equal(scenario, MockScenarioExtensions.ParseOptionValue(scenario.ToOptionValue()));
            Assert.StartsWith("?mock=", scenario.ToQueryTag());
        });
        Assert.Throws<ArgumentException>(() => MockScenarioExtensions.ParseOptionValue("teapot"));
    }

    // ---------- 요청 계약 검사 ----------

    private static MockHttpRequest Post(string path, string body, string? contentType = "application/json") =>
        new("POST", path, contentType, body);

    [Theory]
    [InlineData("/queue/enter", """{ "showId": "p-1", "sessionHash": "s-1", "deviceHash": "d-1" }""", MockEndpoint.QueueEnter)]
    [InlineData("/seats/hold", """{ "showId": "p-1", "seatId": "A-1", "sessionHash": "s-1" }""", MockEndpoint.SeatHold)]
    [InlineData("/payments", """{ "holdId": "h-1", "sessionHash": "s-1" }""", MockEndpoint.Payment)]
    [InlineData("/verifications/v-1", """{ "code": "123456" }""", MockEndpoint.Verification)]
    public void Contract_requests_pass_inspection(string path, string body, MockEndpoint endpoint)
    {
        var inspection = MockRequestInspector.Inspect(Post(path, body));

        Assert.True(inspection.MatchesContract, string.Join(", ", inspection.Problems));
        Assert.Equal(endpoint, inspection.Endpoint);
        Assert.Null(inspection.ErrorStatusCode);
    }

    [Fact]
    public void Get_requests_and_events_pass_inspection_and_carry_session()
    {
        var verdictLookup = MockRequestInspector.Inspect(new MockHttpRequest("GET", "/verdicts/s-9", null, ""));
        Assert.Equal(MockEndpoint.VerdictLookup, verdictLookup.Endpoint);
        Assert.Equal("s-9", verdictLookup.SessionHash);
        Assert.True(MockRequestInspector.Inspect(new MockHttpRequest("GET", "/queue/q-1", null, "")).MatchesContract);

        var events = MockRequestInspector.Inspect(Post("/events", EventsBody("s-7", ("seat_click", "client", 0))));
        Assert.True(events.MatchesContract, string.Join(", ", events.Problems));
        Assert.Equal("s-7", events.SessionHash);
    }

    [Theory]
    [InlineData("POST", "/checks", "application/json", "{}", 404, "계약에 없는 경로: /checks")]
    [InlineData("GET", "/payments", null, "", 405, "POST가 아니라 GET로 보냄")]
    [InlineData("POST", "/queue/enter", null, """{ "showId": "p", "sessionHash": "s", "deviceHash": "d" }""", 400, "Content-Type 헤더 없음")]
    [InlineData("POST", "/queue/enter", "application/json", """{ "showId": "p", "sessionHash": "s" }""", 400, "deviceHash 필드 없음")]
    [InlineData("POST", "/seats/hold", "application/json", """{ "showId": "p", "seatId": 3, "sessionHash": "s" }""", 400, "seatId가 문자열이 아님")]
    [InlineData("POST", "/payments", "application/json", "holdId=h", 400, "본문이 JSON 형식이 아님")]
    [InlineData("POST", "/events", "application/json", """{ "events": [] }""", 400, "events 배열이 비어 있음")]
    [InlineData("POST", "/events", "application/json", """{ "items": [] }""", 400, "본문에 events 배열 없음")]
    public void Request_that_breaks_contract_is_reported(string method, string path, string? contentType, string body, int statusCode, string problem)
    {
        var inspection = MockRequestInspector.Inspect(new MockHttpRequest(method, path, contentType, body));

        Assert.False(inspection.MatchesContract);
        Assert.Equal(statusCode, inspection.ErrorStatusCode);
        Assert.Contains(problem, inspection.Problems);
    }

    [Fact]
    public void Event_fields_are_checked_against_contract_4_1()
    {
        const string body = """
            { "events": [ { "eventId": "e1", "type": "jump", "origin": "phone", "sessionHash": "s", "deviceHash": "d",
                            "showId": "p", "occurredAt": "어제", "source": "web" } ] }
            """;

        var problems = MockRequestInspector.Inspect(Post("/events", body)).Problems;

        Assert.Contains("events[0].type 'jump'은 계약에 없음", problems);
        Assert.Contains("events[0].origin은 client 또는 server여야 함", problems);
        Assert.Contains("events[0].source는 app 또는 qa여야 함", problems);
        Assert.Contains("그 밖에 이벤트 문제 1개", problems);
    }

    // ---------- 예매 흐름 (속마음) ----------

    private static string EventsBody(string sessionHash, params (string Type, string Origin, int AtMs)[] events) =>
        JsonSerializer.Serialize(new
        {
            events = events.Select((item, index) => new
            {
                eventId = $"{sessionHash}-{index}", type = item.Type, origin = item.Origin, sessionHash, deviceHash = "d-1",
                showId = "p-1", occurredAt = Now.AddMilliseconds(item.AtMs).ToString("O"), source = "app",
            }),
        });

    // 계약과 맞는 요청을 속마음에 바로 넣는다
    private static MockResponse Call(MockTicketBackend backend, string method, string path, string body = "",
        MockScenario scenario = MockScenario.Auto, DateTimeOffset? now = null)
    {
        var inspection = MockRequestInspector.Inspect(new MockHttpRequest(method, path, method == "POST" ? "application/json" : null, body));
        Assert.True(inspection.MatchesContract, string.Join(", ", inspection.Problems));
        return backend.Respond(inspection, scenario, now ?? Now);
    }

    private static JsonElement Json(MockResponse response) => JsonDocument.Parse(response.Body).RootElement;

    private static string HoldSeat(MockTicketBackend backend, string sessionHash, string seatId = "A-1", MockScenario scenario = MockScenario.Auto)
    {
        var hold = Call(backend, "POST", "/seats/hold", $$"""{ "showId": "p-1", "seatId": "{{seatId}}", "sessionHash": "{{sessionHash}}" }""", scenario);
        Assert.Equal(200, hold.StatusCode);
        return Json(hold).GetProperty("holdId").GetString()!;
    }

    private static MockResponse Pay(MockTicketBackend backend, string sessionHash, string holdId, MockScenario scenario = MockScenario.Auto, DateTimeOffset? now = null) =>
        Call(backend, "POST", "/payments", $$"""{ "holdId": "{{holdId}}", "sessionHash": "{{sessionHash}}" }""", scenario, now);

    [Fact]
    public void Fast_clicking_session_must_verify_before_payment_and_seat_is_sold_after()
    {
        var backend = new MockTicketBackend();
        var enter = Call(backend, "POST", "/queue/enter", """{ "showId": "p-1", "sessionHash": "s-bot", "deviceHash": "d-1" }""");
        Assert.Equal(0, Json(enter).GetProperty("position").GetInt32());

        Call(backend, "POST", "/events", EventsBody("s-bot", ("page_view", "client", 0), ("seat_click", "client", 100), ("seat_click", "client", 200)));
        var holdId = HoldSeat(backend, "s-bot");

        // 클릭 간격 0.1초 → 자동 판정 추가 확인 → 결제 전에 본인 확인
        var payment = Pay(backend, "s-bot", holdId);
        Assert.Equal(202, payment.StatusCode);
        Assert.Equal("VERIFY_REQUIRED", Json(payment).GetProperty("status").GetString());
        var verifyId = Json(payment).GetProperty("verifyId").GetString();

        Assert.Equal(400, Call(backend, "POST", $"/verifications/{verifyId}", """{ "code": "000000" }""").StatusCode);
        var verified = Call(backend, "POST", $"/verifications/{verifyId}", """{ "code": "123456" }""");
        Assert.Equal("PAID", Json(verified).GetProperty("status").GetString());

        // 판정 조회에는 근거 규칙이 붙고, 팔린 좌석은 다시 점유할 수 없다
        var verdict = Json(Call(backend, "GET", "/verdicts/s-bot"));
        Assert.Equal("VERIFY", verdict.GetProperty("verdict").GetString());
        Assert.Equal("R1", verdict.GetProperty("rules")[0].GetProperty("ruleId").GetString());
        Assert.Equal(410, Call(backend, "POST", "/seats/hold", """{ "showId": "p-1", "seatId": "A-1", "sessionHash": "s-2" }""").StatusCode);
    }

    [Fact]
    public void Human_like_session_pays_immediately_and_api_only_session_is_watched()
    {
        var backend = new MockTicketBackend();
        Call(backend, "POST", "/events", EventsBody("s-human", ("page_view", "client", 0), ("seat_click", "client", 3200), ("seat_click", "client", 6100)));
        var humanPayment = Pay(backend, "s-human", HoldSeat(backend, "s-human", "A-1"));
        Assert.Equal(200, humanPayment.StatusCode);
        Assert.Equal("결제 완료 · 200", humanPayment.Summary);

        // 화면 기록 없이 예매 API만 부른 세션은 관찰 (사용자에겐 변화 없음)
        var apiOnlyPayment = Pay(backend, "s-api", HoldSeat(backend, "s-api", "A-2"));
        Assert.Equal("PAID", Json(apiOnlyPayment).GetProperty("status").GetString());
        Assert.Equal("WATCH", Json(Call(backend, "GET", "/verdicts/s-api")).GetProperty("verdict").GetString());
        Assert.Equal(404, Call(backend, "GET", "/verdicts/s-unknown").StatusCode);
    }

    [Theory]
    [InlineData(MockScenario.AlwaysNormal, 200, "PAID")]
    [InlineData(MockScenario.AlwaysWatch, 200, "PAID")]
    [InlineData(MockScenario.AlwaysVerify, 202, "VERIFY_REQUIRED")]
    [InlineData(MockScenario.AlwaysHold, 202, "ON_HOLD")]
    public void Fixed_verdict_scenarios_decide_payment(MockScenario scenario, int statusCode, string status)
    {
        var backend = new MockTicketBackend();

        var payment = Pay(backend, "s-1", HoldSeat(backend, "s-1"), scenario);

        Assert.Equal(statusCode, payment.StatusCode);
        Assert.Equal(status, Json(payment).GetProperty("status").GetString());
    }

    [Fact]
    public void Seat_conflicts_sold_out_and_expired_holds_follow_contract()
    {
        var backend = new MockTicketBackend();
        var holdId = HoldSeat(backend, "s-1", "B-7");

        // 다른 세션이 점유 중인 좌석은 409, 매진이면 410, 이미 점유된 좌석 방식이면 409
        Assert.Equal(409, Call(backend, "POST", "/seats/hold", """{ "showId": "p-1", "seatId": "B-7", "sessionHash": "s-2" }""").StatusCode);
        Assert.Equal(410, Call(backend, "POST", "/seats/hold", """{ "showId": "p-1", "seatId": "C-1", "sessionHash": "s-2" }""", MockScenario.SoldOut).StatusCode);
        Assert.Equal(409, Call(backend, "POST", "/seats/hold", """{ "showId": "p-1", "seatId": "C-2", "sessionHash": "s-2" }""", MockScenario.SeatTaken).StatusCode);

        // 남의 점유로 결제하면 409, 점유 시간이 지나면 404
        Assert.Equal(409, Pay(backend, "s-2", holdId).StatusCode);
        Assert.Equal(404, Pay(backend, "s-1", holdId, now: Now + MockTicketBackend.HoldDuration + TimeSpan.FromSeconds(1)).StatusCode);
        Assert.Equal(404, Pay(backend, "s-3", HoldSeat(backend, "s-3", "D-1"), MockScenario.HoldExpired).StatusCode);
    }

    [Fact]
    public void Delayed_verdict_makes_payment_wait_then_require_verification()
    {
        var backend = new MockTicketBackend();
        Call(backend, "POST", "/events", EventsBody("s-1", ("page_view", "client", 0)), MockScenario.VerdictDelayed);
        var holdId = HoldSeat(backend, "s-1", scenario: MockScenario.VerdictDelayed);

        Assert.Equal(404, Call(backend, "GET", "/verdicts/s-1", scenario: MockScenario.VerdictDelayed).StatusCode);

        // 계약 3장: 판정이 없으면 2초 기다린 뒤 추가 확인으로 (스트림이 멈춰도 그냥 통과시키지 않음)
        var payment = Pay(backend, "s-1", holdId, MockScenario.VerdictDelayed);
        Assert.Equal(202, payment.StatusCode);
        Assert.Equal("VERIFY_REQUIRED", Json(payment).GetProperty("status").GetString());
        Assert.Equal(MockTicketBackend.PaymentVerdictWait, payment.Delay);

        var later = Now + MockTicketBackend.VerdictDelay + TimeSpan.FromSeconds(1);
        Assert.Equal(200, Call(backend, "GET", "/verdicts/s-1", scenario: MockScenario.VerdictDelayed, now: later).StatusCode);
    }

    [Fact]
    public void Long_queue_shrinks_until_admitted()
    {
        var backend = new MockTicketBackend();
        var enter = Json(Call(backend, "POST", "/queue/enter", """{ "showId": "p-1", "sessionHash": "s-1", "deviceHash": "d-1" }""", MockScenario.LongQueue));
        var token = enter.GetProperty("queueToken").GetString();
        Assert.True(enter.GetProperty("position").GetInt32() >= MockTicketBackend.LongQueueStart);

        var afterTwoSeconds = Json(Call(backend, "GET", $"/queue/{token}", now: Now.AddSeconds(2)));
        Assert.False(afterTwoSeconds.GetProperty("admitted").GetBoolean());
        Assert.True(afterTwoSeconds.GetProperty("position").GetInt32() < MockTicketBackend.LongQueueStart);

        var afterTenSeconds = Json(Call(backend, "GET", $"/queue/{token}", now: Now.AddSeconds(10)));
        Assert.True(afterTenSeconds.GetProperty("admitted").GetBoolean());
        Assert.Equal(404, Call(backend, "GET", "/queue/q-unknown").StatusCode);
    }

    [Theory]
    [InlineData(MockScenario.BadRequest, 400)]
    [InlineData(MockScenario.ServerError, 500)]
    [InlineData(MockScenario.Unavailable, 503)]
    public void Error_scenarios_answer_every_endpoint_with_error(MockScenario scenario, int statusCode)
    {
        var response = Call(new MockTicketBackend(), "POST", "/queue/enter", """{ "showId": "p", "sessionHash": "s", "deviceHash": "d" }""", scenario);

        Assert.Equal(statusCode, response.StatusCode);
        Assert.Contains(scenario.ToQueryTag(), Json(response).GetProperty("error").GetString());
    }

    [Fact]
    public void Slow_and_broken_scenarios_change_delay_and_body()
    {
        var backend = new MockTicketBackend();
        Assert.Equal(MockTicketBackend.SlowDelay, Call(backend, "GET", "/queue/q-x", scenario: MockScenario.Slow).Delay);
        Assert.True(Call(backend, "GET", "/queue/q-x", scenario: MockScenario.Timeout).Delay > ApiVerdictService.RequestTimeout);
        var broken = Call(backend, "POST", "/payments", """{ "holdId": "h", "sessionHash": "s" }""", MockScenario.BrokenJson);
        Assert.Equal(200, broken.StatusCode);
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(broken.Body));
    }

    // ---------- 실제 HTTP 왕복 ----------

    [Fact]
    public async Task Qa_api_client_gets_verdicts_from_the_mock_server_over_http()
    {
        using var server = MockTicketServer.Start(MockServerSettings.Default with { Port = 0 });
        var apiVerdictService = new ApiVerdictService(ApiVerdictService.CreateHttpClient($"http://127.0.0.1:{server.Port}"));

        // QA 러너의 실제 판정 대상으로 부르면 수집 API → 판정 조회 API를 계약대로 지나간다
        var botSession = new SessionCase("S-B", "봇 A", "0.1초 간격", "추가 확인",
            [new SessionStep("queue_enter", 30), new SessionStep("seat_click", 130), new SessionStep("seat_click", 230)]);
        Assert.Equal("추가 확인", await apiVerdictService.GetVerdictAsync(botSession, "run-1", "run-1-S-B"));
        Assert.Equal("정상", await apiVerdictService.GetVerdictAsync(SampleSessions.Human("S-H"), "run-1", "run-1-S-H"));

        var records = server.Records;
        Assert.Equal([MockEndpoint.Events, MockEndpoint.VerdictLookup, MockEndpoint.Events, MockEndpoint.VerdictLookup],
            records.Select(record => record.Endpoint));
        Assert.True(records.All(record => record.MatchesContract));
        Assert.Equal("qa-run-1-S-B", records[0].SessionPreview);
        Assert.Equal("이벤트 3건 · 202", records[0].ResponseSummary);
    }

    [Fact]
    public async Task Header_and_query_override_the_scenario_for_one_request_and_wrong_paths_are_recorded()
    {
        using var server = MockTicketServer.Start(MockServerSettings.Default with { Port = 0 });
        using var httpClient = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}/") };
        var enterBody = new { showId = "p-1", sessionHash = "s-1", deviceHash = "d-1" };

        using var normal = await httpClient.PostAsJsonAsync("queue/enter", enterBody);
        using var byQuery = await httpClient.PostAsJsonAsync("queue/enter?mock=503", enterBody);
        using var byHeaderRequest = new HttpRequestMessage(HttpMethod.Post, "queue/enter") { Content = JsonContent.Create(enterBody) };
        byHeaderRequest.Headers.Add(MockTicketServer.ScenarioHeader, "500");
        using var byHeader = await httpClient.SendAsync(byHeaderRequest);
        using var wrongPath = await httpClient.PostAsync("checks", new StringContent("hello", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.OK, normal.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, byQuery.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, byHeader.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrongPath.StatusCode);

        // 브라우저 사전 확인과 주소 확인은 기록하지 않는다
        using var preflight = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Options, "payments"));
        Assert.Equal(HttpStatusCode.NoContent, preflight.StatusCode);
        Assert.Contains(MockTicketServer.ScenarioHeader, preflight.Headers.GetValues("Access-Control-Allow-Headers").Single());
        Assert.Contains("가짜 예매·판정 서버", await httpClient.GetStringAsync(""));

        var records = server.Records;
        Assert.Equal(4, records.Count);
        Assert.Equal([MockScenario.Auto, MockScenario.Unavailable, MockScenario.ServerError], records.Take(3).Select(record => record.Scenario));
        Assert.Equal("계약 오류 · 404", records[3].ResponseSummary);
        Assert.False(records[3].MatchesContract);
    }

    [Fact]
    public async Task Stopping_cancels_a_slow_response_and_the_server_stops_after_duration()
    {
        using var server = MockTicketServer.Start(MockServerSettings.Default with { Port = 0, Scenario = MockScenario.Timeout });
        using var httpClient = new HttpClient();
        var pendingRequest = httpClient.PostAsJsonAsync($"http://127.0.0.1:{server.Port}/queue/enter",
            new { showId = "p-1", sessionHash = "s-1", deviceHash = "d-1" });

        // 정한 시간이 지나면 스스로 꺼지고, 12초를 기다리던 응답은 끊긴다
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await server.WaitUntilStoppedAsync(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<Exception>(() => pendingRequest);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Single(server.Records);
    }

    [Fact]
    public async Task Localhost_reaches_the_server_over_ipv6_without_waiting()
    {
        // Windows는 localhost를 ::1로 먼저 시도한다. ::1에서도 받아야 요청마다 2초씩 헛걸음하지 않는다
        if (!Socket.OSSupportsIPv6)
            return;
        using var server = MockTicketServer.Start(MockServerSettings.Default with { Port = 0 });
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };

        Assert.Contains("가짜 예매·판정 서버", await httpClient.GetStringAsync($"http://[::1]:{server.Port}/"));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await httpClient.GetStringAsync($"http://localhost:{server.Port}/");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"localhost 요청이 {stopwatch.Elapsed.TotalSeconds:0.0}초 걸렸어요");
    }

    // ---------- 포트 ----------

    [Fact]
    public void Port_in_use_is_a_preparation_error()
    {
        using var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();
        var port = ((IPEndPoint)occupied.LocalEndpoint).Port;

        var exception = Assert.Throws<MockServerStartException>(() => MockTicketServer.Start(MockServerSettings.Default with { Port = port }));

        Assert.Contains($"{port}번 포트", exception.Message);
        Assert.True(QaRunService.IsPreparationError(exception));
    }

    [Fact]
    public void Port_opened_on_all_addresses_by_another_server_is_also_in_use()
    {
        // uvicorn --host 0.0.0.0처럼 다른 서버가 모든 주소로 먼저 열었으면, 127.0.0.1로 함께 켜지지 않고 알려 준다
        using var apiOnAllAddresses = new TcpListener(IPAddress.Any, 0);
        apiOnAllAddresses.Start();
        var port = ((IPEndPoint)apiOnAllAddresses.LocalEndpoint).Port;

        Assert.Throws<MockServerStartException>(() => MockTicketServer.Start(MockServerSettings.Default with { Port = port }));
    }

    [Fact]
    public void Another_server_cannot_open_the_same_port_while_mock_server_runs()
    {
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        using var server = MockTicketServer.Start(MockServerSettings.Default with { Port = port });

        // 나중에 켠 백엔드 API는 127.0.0.1로든 0.0.0.0으로든 같은 포트를 열지 못한다
        foreach (var address in new[] { IPAddress.Loopback, IPAddress.Any })
        {
            using var lateApi = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Assert.Throws<SocketException>(() => lateApi.Bind(new IPEndPoint(address, port)));
        }

        // 서버를 끄면 포트를 돌려준다
        server.Stop();
        using var afterStop = new TcpListener(IPAddress.Any, port);
        afterStop.Start();
        afterStop.Stop();
    }

    [Fact]
    public void Addresses_include_emulator_address_and_lan_only_when_allowed()
    {
        using var server = MockTicketServer.Start(MockServerSettings.Default with { Port = 0 });

        Assert.Equal(
            [$"http://127.0.0.1:{server.Port}", $"http://10.0.2.2:{server.Port}"],
            server.Addresses.Select(address => address.Url));
        Assert.False(server.AllowsLan);
    }

    // ---------- 채점·옵션 ----------

    [Fact]
    public void Grades_each_request_and_fails_when_nothing_was_received()
    {
        var noRequest = Assert.Single(MockRequestGrader.Grade([]));
        Assert.Equal(MockRequestGrader.NoRequestCheckName, noRequest.CheckName);
        Assert.False(noRequest.IsPassed);

        var results = MockRequestGrader.Grade(
        [
            new MockRequestRecord(2, Now, "POST", "/checks", MockEndpoint.Unknown, "", MockScenario.Auto, 404, "계약 오류 · 404", ["계약에 없는 경로: /checks"]),
            new MockRequestRecord(1, Now, "POST", "/payments", MockEndpoint.Payment, "s-1", MockScenario.AlwaysVerify, 202, "추가 확인 필요 · 202", []),
        ]);

        Assert.Equal(["요청-001", "요청-002"], results.Select(result => result.CaseId));
        Assert.True(results[0].IsPassed);
        Assert.Contains("결제 · POST /payments · 세션 s-1 → 돌려준 응답: 추가 확인 필요 · 202", results[0].Input);
        Assert.False(results[1].IsPassed);
        Assert.Equal("계약에 없는 경로: /checks", results[1].Actual);
    }

    [Fact]
    public void Parses_mock_server_options()
    {
        var options = CommandLineOptions.Parse(["--suite", "mockapi", "--port", "8123", "--scenario", "verify", "--allow-lan", "--duration", "60"]);

        Assert.Equal(TestSuite.MockApi, options.Suite);
        Assert.Equal(CommandLineOptions.NoCasesPath, options.CasesPath);
        Assert.Equal(new MockServerSettings(8123, MockScenario.AlwaysVerify, AllowLan: true, DurationSeconds: 60), options.MockServer);

        Assert.Equal(MockServerSettings.Default, CommandLineOptions.Parse(["--suite", "mockapi"]).MockServer);
        Assert.Null(CommandLineOptions.Parse(["--suite", "verdict"]).MockServer);
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["--suite", "mockapi", "--scenario", "teapot"]));
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["--suite", "mockapi", "--port", "70000"]));
    }

    [Fact]
    public async Task Mock_api_run_is_recorded_in_app_area()
    {
        var reportDirectory = Path.Combine(_tempFolder, "reports");
        var options = CommandLineOptions.Parse(["--suite", "mockapi", "--port", "0", "--duration", "1", "--reports", reportDirectory]);
        var preparedRun = QaRunService.Prepare(options);
        Assert.NotNull(preparedRun.RequestStop);
        Assert.Contains("10.0.2.2", preparedRun.Announcement);

        var testRun = await QaRunService.RunAsync(preparedRun, reportDirectory);

        Assert.Equal("가짜 예매·판정 서버", testRun.SuiteName);
        Assert.Equal("앱", testRun.AreaName);
        Assert.Equal(MockTicketServer.TargetName, testRun.TargetName);
        Assert.False(testRun.UsedFakeTarget);
        Assert.Equal(MockRequestGrader.NoRequestCheckName, Assert.Single(testRun.Results).CheckName);
    }
}
