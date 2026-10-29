using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using QaRunner.AppMock;
using QaRunner.Models;
using QaRunner.Verdicts;

namespace QaRunner.Tests;

/// <summary>
/// 앱 영역의 가짜 판정 서버를 확인한다: 응답 방식 고르기, 응답 내용, 요청 계약 검사, 실제 HTTP 왕복, 보고서 채점.
/// </summary>
public class MockVerdictServerTests : IDisposable
{
    private static readonly DateTimeOffset CheckedAt = new(2026, 11, 2, 18, 0, 10, TimeSpan.FromHours(9));
    private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), $"qa-mock-test-{Guid.NewGuid():N}");

    public MockVerdictServerTests() => Directory.CreateDirectory(_tempFolder);

    public void Dispose() => Directory.Delete(_tempFolder, recursive: true);

    [Theory]
    [InlineData("인증번호 알려주세요", MockScenario.Auto, MockScenario.Auto)]
    [InlineData("인증번호 알려주세요 #500", MockScenario.Auto, MockScenario.ServerError)]
    [InlineData("#SLOW 택배 조회", MockScenario.HighRisk, MockScenario.Slow)]
    [InlineData("해시태그 #여행 은 표시가 아님", MockScenario.Caution, MockScenario.Caution)]
    [InlineData(null, MockScenario.Timeout, MockScenario.Timeout)]
    public void Message_tag_overrides_the_selected_scenario_for_that_request(string? message, MockScenario selected, MockScenario expected)
    {
        Assert.Equal(expected, MockVerdictResponder.ChooseScenario(message, selected));
    }

    [Theory]
    [InlineData("[택배] 상담원에게 인증번호를 알려주세요", "HIGH_RISK")]
    [InlineData("보안 강화를 위해 앱을 설치해 주세요", "HIGH_RISK")]
    [InlineData("택배 주소가 틀렸습니다 https://example.test 확인", "CAUTION")]
    [InlineData("오늘 저녁에 같이 밥 먹을래요?", "NO_SIGNAL")]
    [InlineData(" 안녕 ", "UNDECIDED")]
    public void Auto_verdict_follows_simple_word_rules(string message, string expectedCode)
    {
        Assert.Equal(expectedCode, MockVerdictResponder.DecideAutoVerdict(message));
    }

    [Fact]
    public void Verdict_responses_follow_contract_chapter_3()
    {
        var response = MockVerdictResponder.Respond(MockScenario.HighRisk, "아무 문자", CheckedAt);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(TimeSpan.Zero, response.Delay);
        using var body = JsonDocument.Parse(response.Body);
        var root = body.RootElement;
        Assert.True(Guid.TryParse(root.GetProperty("scanId").GetString(), out _));
        Assert.Equal("HIGH_RISK", root.GetProperty("verdict").GetString());
        Assert.True(root.GetProperty("evidence").GetArrayLength() > 0);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("guidance").GetString()));
        Assert.Equal("2026-11-02T09:00:10Z", root.GetProperty("checkedAt").GetString());
        // 앱 개발자가 응답 본문을 그대로 읽을 수 있게 한글을 \uXXXX로 바꾸지 않는다
        Assert.Contains("인증번호", response.Body);
    }

    [Fact]
    public void Special_scenarios_return_edge_case_responses()
    {
        using (var noEvidence = JsonDocument.Parse(MockVerdictResponder.Respond(MockScenario.NoEvidence, "x", CheckedAt).Body))
        {
            Assert.Equal(0, noEvidence.RootElement.GetProperty("evidence").GetArrayLength());
            Assert.False(noEvidence.RootElement.TryGetProperty("guidance", out _));
        }

        using (var longText = JsonDocument.Parse(MockVerdictResponder.Respond(MockScenario.LongText, "x", CheckedAt).Body))
        {
            Assert.Equal(5, longText.RootElement.GetProperty("evidence").GetArrayLength());
            Assert.True(longText.RootElement.GetProperty("guidance").GetString()!.Length > 150);
        }

        var unknown = MockVerdictResponder.Respond(MockScenario.UnknownVerdict, "x", CheckedAt);
        Assert.Equal(MockVerdictResponder.UnknownVerdictCode, unknown.VerdictCode);

        var broken = MockVerdictResponder.Respond(MockScenario.BrokenJson, "x", CheckedAt);
        Assert.Equal(200, broken.StatusCode);
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(broken.Body));

        Assert.Equal(MockVerdictResponder.SlowDelay, MockVerdictResponder.Respond(MockScenario.Slow, "x", CheckedAt).Delay);
        // 늦은 응답은 계약(QA 러너)의 10초 제한을 넘어야 시간 초과 화면을 확인할 수 있다
        Assert.True(MockVerdictResponder.Respond(MockScenario.Timeout, "x", CheckedAt).Delay > ApiVerdictService.RequestTimeout);

        foreach (var (scenario, statusCode) in new[] { (MockScenario.BadRequest, 400), (MockScenario.ServerError, 500), (MockScenario.Unavailable, 503) })
        {
            var error = MockVerdictResponder.Respond(scenario, "x", CheckedAt);
            Assert.Equal(statusCode, error.StatusCode);
            Assert.Null(error.VerdictCode);
            using var errorBody = JsonDocument.Parse(error.Body);
            Assert.Contains(scenario.ToMessageTag(), errorBody.RootElement.GetProperty("error").GetString());
        }
    }

    [Fact]
    public void Every_scenario_has_korean_name_description_and_app_expectation()
    {
        Assert.All(Enum.GetValues<MockScenario>(), scenario =>
        {
            Assert.NotEqual(scenario.ToString(), scenario.ToKoreanName());
            Assert.NotEmpty(scenario.DescribeResponse());
            Assert.NotEmpty(scenario.DescribeAppExpectation());
            Assert.Equal(scenario, MockScenarioExtensions.ParseOptionValue(scenario.ToOptionValue()));
        });
    }

    [Fact]
    public void Contract_request_passes_inspection()
    {
        var inspection = MockRequestInspector.Inspect(
            new MockHttpRequest("POST", "/checks", "application/json; charset=utf-8", """{ "message": "택배 문자" }"""));

        Assert.True(inspection.MatchesContract);
        Assert.Equal("택배 문자", inspection.Message);
        Assert.Null(inspection.ErrorStatusCode);
    }

    [Theory]
    [InlineData("POST", "/check", "application/json", """{ "message": "x" }""", 404, "경로가 /checks가 아니라 /check")]
    [InlineData("GET", "/checks", "application/json", """{ "message": "x" }""", 405, "POST가 아니라 GET로 보냄")]
    [InlineData("POST", "/checks", null, """{ "message": "x" }""", 400, "Content-Type 헤더 없음")]
    [InlineData("POST", "/checks", "text/plain", """{ "message": "x" }""", 400, "Content-Type이 application/json이 아니라 text/plain")]
    [InlineData("POST", "/checks", "application/json", "", 400, "본문이 비어 있음")]
    [InlineData("POST", "/checks", "application/json", "message=x", 400, "본문이 JSON 형식이 아님")]
    [InlineData("POST", "/checks", "application/json", """["x"]""", 400, "본문이 JSON 객체({ })가 아님")]
    [InlineData("POST", "/checks", "application/json", """{ "text": "x" }""", 400, "본문에 message 필드 없음")]
    [InlineData("POST", "/checks", "application/json", """{ "message": 3 }""", 400, "message가 문자열이 아님")]
    [InlineData("POST", "/checks", "application/json", """{ "message": "  " }""", 400, "message가 비어 있음")]
    public void Request_that_breaks_contract_is_reported(string method, string path, string? contentType, string body, int statusCode, string problem)
    {
        var inspection = MockRequestInspector.Inspect(new MockHttpRequest(method, path, contentType, body));

        Assert.False(inspection.MatchesContract);
        Assert.Equal(statusCode, inspection.ErrorStatusCode);
        Assert.Contains(problem, inspection.Problems);
    }

    [Fact]
    public async Task Real_http_round_trip_works_with_the_qa_api_client()
    {
        using var server = MockVerdictServer.Start(MockServerSettings.Default with { Port = 0 });
        var received = new List<MockRequestRecord>();
        server.RequestReceived += record => { lock (received) received.Add(record); };
        var apiVerdictService = new ApiVerdictService(ApiVerdictService.CreateHttpClient($"http://127.0.0.1:{server.Port}"));

        // QA 러너의 실제 판정 API 클라이언트로 부르면, 계약대로 보낸 요청이라 통과로 기록된다
        Assert.Equal("고위험", await apiVerdictService.GetVerdictAsync("[택배] 상담원에게 인증번호를 알려주세요"));
        await Assert.ThrowsAsync<HttpRequestException>(() => apiVerdictService.GetVerdictAsync("오늘 저녁 #503"));

        // 켜 둔 채로 응답 방식을 바꿀 수 있다
        server.Scenario = MockScenario.Caution;
        Assert.Equal("주의", await apiVerdictService.GetVerdictAsync("오늘 저녁에 같이 밥 먹을래요?"));

        // 경로와 형식이 틀린 요청은 404로 답하고 문제를 기록한다
        using var httpClient = new HttpClient();
        using var wrongResponse = await httpClient.PostAsync($"http://127.0.0.1:{server.Port}/check",
            new StringContent("hello", Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.NotFound, wrongResponse.StatusCode);

        // 브라우저 사전 확인과 주소 확인은 기록하지 않는다
        using var preflight = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Options, $"http://127.0.0.1:{server.Port}/checks"));
        Assert.Equal(HttpStatusCode.NoContent, preflight.StatusCode);
        Assert.Equal("*", preflight.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("가짜 판정 서버", await httpClient.GetStringAsync($"http://127.0.0.1:{server.Port}/"));

        var records = server.Records;
        Assert.Equal(4, records.Count);
        Assert.Equal(4, received.Count);
        Assert.Equal([1, 2, 3, 4], records.Select(record => record.Number));
        Assert.Equal("고위험 · 200", records[0].ResponseSummary);
        Assert.Equal("[택배] 상담원에게 인증번호를 알려주세요", records[0].MessagePreview);
        Assert.Equal("일시 중단 503", records[1].ResponseSummary);
        Assert.Equal(MockScenario.Caution, records[2].Scenario);
        Assert.Equal("계약 오류 · 404", records[3].ResponseSummary);
        Assert.True(records.Take(3).All(record => record.MatchesContract));
        Assert.False(records[3].MatchesContract);

        server.Stop();
        await server.WaitUntilStoppedAsync(null);
        Assert.True(server.IsStopped);
    }

    [Fact]
    public async Task Stopping_cancels_a_slow_response_and_the_server_stops_after_duration()
    {
        using var server = MockVerdictServer.Start(MockServerSettings.Default with { Port = 0, Scenario = MockScenario.Timeout });
        using var httpClient = new HttpClient();
        var pendingRequest = httpClient.PostAsync($"http://127.0.0.1:{server.Port}/checks",
            new StringContent("""{ "message": "택배 문자" }""", Encoding.UTF8, "application/json"));

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
        using var server = MockVerdictServer.Start(MockServerSettings.Default with { Port = 0 });
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };

        Assert.Contains("가짜 판정 서버", await httpClient.GetStringAsync($"http://[::1]:{server.Port}/"));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await httpClient.GetStringAsync($"http://localhost:{server.Port}/");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"localhost 요청이 {stopwatch.Elapsed.TotalSeconds:0.0}초 걸렸어요");
    }

    [Fact]
    public void Port_in_use_is_a_preparation_error()
    {
        using var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();
        var port = ((IPEndPoint)occupied.LocalEndpoint).Port;

        var exception = Assert.Throws<MockServerStartException>(() => MockVerdictServer.Start(MockServerSettings.Default with { Port = port }));

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

        Assert.Throws<MockServerStartException>(() => MockVerdictServer.Start(MockServerSettings.Default with { Port = port }));
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

        using var server = MockVerdictServer.Start(MockServerSettings.Default with { Port = port });

        // 나중에 켠 판정 API는 127.0.0.1로든 0.0.0.0으로든 같은 포트를 열지 못한다
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
        using var server = MockVerdictServer.Start(MockServerSettings.Default with { Port = 0 });

        Assert.Equal(
            [$"http://127.0.0.1:{server.Port}", $"http://10.0.2.2:{server.Port}"],
            server.Addresses.Select(address => address.Url));
        Assert.False(server.AllowsLan);
    }

    [Fact]
    public void Grades_each_request_and_fails_when_nothing_was_received()
    {
        var noRequest = Assert.Single(MockRequestGrader.Grade([]));
        Assert.Equal(MockRequestGrader.NoRequestCheckName, noRequest.CheckName);
        Assert.False(noRequest.IsPassed);

        var results = MockRequestGrader.Grade(
        [
            new MockRequestRecord(2, CheckedAt, "POST", "/check", "", MockScenario.Auto, 404, null, ["경로가 /checks가 아니라 /check"]),
            new MockRequestRecord(1, CheckedAt, "POST", "/checks", "택배 문자", MockScenario.Slow, 200, "CAUTION", []),
        ]);

        Assert.Equal(["요청-001", "요청-002"], results.Select(result => result.CaseId));
        Assert.True(results[0].IsPassed);
        Assert.Contains("3초 늦은 응답 · 주의 · 200", results[0].Input);
        Assert.False(results[1].IsPassed);
        Assert.Equal("경로가 /checks가 아니라 /check", results[1].Actual);
    }

    [Fact]
    public void Parses_mock_server_options()
    {
        var options = CommandLineOptions.Parse(["--suite", "mockapi", "--port", "8123", "--scenario", "500", "--allow-lan", "--duration", "60"]);

        Assert.Equal(TestSuite.MockApi, options.Suite);
        Assert.Equal(CommandLineOptions.NoCasesPath, options.CasesPath);
        Assert.Equal(new MockServerSettings(8123, MockScenario.ServerError, AllowLan: true, DurationSeconds: 60), options.MockServer);

        var defaults = CommandLineOptions.Parse(["--suite", "mockapi"]);
        Assert.Equal(MockServerSettings.Default, defaults.MockServer);
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

        Assert.Equal("가짜 판정 서버", testRun.SuiteName);
        Assert.Equal("앱", testRun.AreaName);
        Assert.Equal(MockVerdictServer.TargetName, testRun.TargetName);
        Assert.False(testRun.UsedFakeTarget);
        Assert.Equal(MockRequestGrader.NoRequestCheckName, Assert.Single(testRun.Results).CheckName);
    }
}
