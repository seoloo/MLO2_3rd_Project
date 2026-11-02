using System.Text.Encodings.Web;
using System.Text.Json;
using QaRunner.Pipeline;
using QaRunner.Verdicts;

namespace QaRunner.AppMock;

/// <summary>
/// 가짜 예매·판정 서버가 돌려줄 응답 하나.
/// </summary>
/// <param name="StatusCode">HTTP 상태 코드. 예: 200, 202, 409</param>
/// <param name="Body">응답 본문 (JSON, 깨진 응답이면 잘린 JSON)</param>
/// <param name="Delay">응답을 보내기 전에 기다릴 시간</param>
/// <param name="Scenario">이 응답을 고른 응답 방식</param>
/// <param name="Summary">받은 요청 목록에 보여 줄 한 줄. 예: "추가 확인 필요", "이벤트 5건"</param>
public sealed record MockResponse(int StatusCode, string Body, TimeSpan Delay, MockScenario Scenario, string Summary);

/// <summary>
/// 가짜 예매·판정 서버의 속마음: 대기열·좌석 점유·결제·추가 확인·받은 이벤트·판정을 메모리에 두고,
/// 계약(docs/contract.md 3~5장)대로 응답을 정한다. 네트워크와 상관없는 계산이라 단위 시험으로 바로 확인할 수 있다.
/// 여러 요청이 동시에 와도 상태가 꼬이지 않게 한 번에 하나씩 처리한다.
/// </summary>
public sealed class MockTicketBackend
{
    /// <summary>3초 늦은 응답에서 기다리는 시간</summary>
    public static readonly TimeSpan SlowDelay = TimeSpan.FromSeconds(3);

    /// <summary>12초 늦은 응답에서 기다리는 시간. 계약의 10초 제한보다 길게 잡는다</summary>
    public static readonly TimeSpan TimeoutDelay = TimeSpan.FromSeconds(12);

    /// <summary>판정 지연에서 판정이 나오지 않는 시간</summary>
    public static readonly TimeSpan VerdictDelay = TimeSpan.FromSeconds(12);

    /// <summary>결제 때 판정이 없으면 기다리는 시간 (계약 3장 초안)</summary>
    public static readonly TimeSpan PaymentVerdictWait = TimeSpan.FromSeconds(2);

    /// <summary>좌석 점유 시간 (계약 3장)</summary>
    public static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(5);

    /// <summary>자동 판정에서 이보다 짧은 좌석 클릭 간격이 있으면 추가 확인으로 판정한다</summary>
    public static readonly TimeSpan TooFastClickInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>긴 대기열의 처음 순번과 1초에 줄어드는 사람 수</summary>
    public const int LongQueueStart = 1234;
    public const int LongQueuePeoplePerSecond = 150;

    // 한글을 \uXXXX로 바꾸지 않아서 앱 개발자가 응답 본문을 그대로 읽을 수 있게 한다
    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed record QueueEntry(string SessionHash, int StartPosition, DateTimeOffset EnteredAt);
    private sealed record SeatHold(string HoldId, string SeatKey, string SessionHash, DateTimeOffset ExpiresAt);
    private sealed record Verification(string VerifyId, string HoldId, string SessionHash);

    // 세션마다 받은 이벤트: 종류별 발생 시각, 받은 시각
    private sealed class SessionActivity
    {
        public List<DateTimeOffset> SeatClickTimes { get; } = [];
        public int ClientEventCount { get; set; }
        public int BookingRequestCount { get; set; }
        public DateTimeOffset? FirstSeenAt { get; set; }
    }

    private readonly Lock _stateLock = new();
    private readonly Dictionary<string, QueueEntry> _queueByToken = [];
    private readonly Dictionary<string, SeatHold> _holdsById = [];
    private readonly Dictionary<string, SeatHold> _holdsBySeat = [];
    private readonly HashSet<string> _soldSeats = [];
    private readonly Dictionary<string, Verification> _verifications = [];
    private readonly Dictionary<string, SessionActivity> _activityBySession = [];

    /// <summary>
    /// 계약과 맞는 요청 하나에 응답을 정한다. 응답 방식에 따라 오류·지연을 섞고, 그 밖에는 예매 상태를 바꾼다.
    /// </summary>
    /// <param name="inspection">MockRequestInspector가 확인한 요청 (계약과 맞아야 한다)</param>
    /// <param name="scenario">이 요청에 쓸 응답 방식</param>
    /// <param name="now">지금 시각 (단위 시험에서 시간을 정해 넣을 수 있게 받는다)</param>
    public MockResponse Respond(MockRequestInspection inspection, MockScenario scenario, DateTimeOffset now)
    {
        switch (scenario)
        {
            case MockScenario.BadRequest: return Error(scenario, 400);
            case MockScenario.ServerError: return Error(scenario, 500);
            case MockScenario.Unavailable: return Error(scenario, 503);
            case MockScenario.BrokenJson:
                return new MockResponse(200, "{\"status\": \"PAID\", \"verifyId\": \"v-", TimeSpan.Zero, scenario, "깨진 응답");
        }

        MockResponse response;
        lock (_stateLock)
        {
            response = inspection.Endpoint switch
            {
                MockEndpoint.QueueEnter => EnterQueue(inspection, scenario, now),
                MockEndpoint.QueueStatus => ReadQueue(inspection, scenario, now),
                MockEndpoint.SeatHold => HoldSeat(inspection, scenario, now),
                MockEndpoint.Payment => Pay(inspection, scenario, now),
                MockEndpoint.Verification => Verify(inspection, scenario),
                MockEndpoint.Events => CollectEvents(inspection, scenario, now),
                MockEndpoint.VerdictLookup => LookUpVerdict(inspection, scenario, now),
                _ => ContractError(404, "계약에 없는 경로예요."),
            };
        }

        var extraDelay = scenario switch
        {
            MockScenario.Slow => SlowDelay,
            MockScenario.Timeout => TimeoutDelay,
            _ => TimeSpan.Zero,
        };
        return response with { Delay = response.Delay + extraDelay, Scenario = scenario };
    }

    /// <summary>
    /// 계약에 맞지 않는 요청에 돌려줄 오류 응답 (경로 404, 방식 405, 본문 400).
    /// </summary>
    public static MockResponse ContractError(int statusCode, string reason) =>
        new(statusCode, Serialize(new { error = reason }), TimeSpan.Zero, MockScenario.Auto, $"계약 오류 · {statusCode}");

    // ---------- 예매 API (계약 3장) ----------

    private MockResponse EnterQueue(MockRequestInspection inspection, MockScenario scenario, DateTimeOffset now)
    {
        var sessionHash = inspection.SessionHash!;
        Touch(sessionHash, now).BookingRequestCount++;
        var startPosition = scenario == MockScenario.LongQueue ? LongQueueStart + Random.Shared.Next(0, 50) : 0;
        var queueToken = "q-" + Guid.NewGuid().ToString("N")[..10];
        _queueByToken[queueToken] = new QueueEntry(sessionHash, startPosition, now);
        return Ok(scenario, new { queueToken, position = startPosition }, startPosition > 0 ? $"대기 {startPosition}번" : "바로 입장");
    }

    private MockResponse ReadQueue(MockRequestInspection inspection, MockScenario scenario, DateTimeOffset now)
    {
        if (!_queueByToken.TryGetValue(inspection.RouteValue!, out var entry))
            return NotFound(scenario, "대기열 번호를 찾지 못했어요. 대기열에 다시 들어와 주세요.");

        var waitedSeconds = (now - entry.EnteredAt).TotalSeconds;
        var position = Math.Max(0, entry.StartPosition - (int)(waitedSeconds * LongQueuePeoplePerSecond));
        var admitted = position == 0;
        return Ok(scenario, new { position, admitted }, admitted ? "입장" : $"대기 {position}번");
    }

    private MockResponse HoldSeat(MockRequestInspection inspection, MockScenario scenario, DateTimeOffset now)
    {
        var body = inspection.Body!.Value;
        var sessionHash = inspection.SessionHash!;
        Touch(sessionHash, now).BookingRequestCount++;
        var seatKey = $"{body.GetProperty("showId").GetString()}:{body.GetProperty("seatId").GetString()}";

        if (scenario == MockScenario.SoldOut || _soldSeats.Contains(seatKey) && scenario != MockScenario.SeatTaken)
            return Fail(scenario, 410, "매진됐어요.", "매진 · 410");

        var takenByOther = _holdsBySeat.TryGetValue(seatKey, out var existing) && existing.ExpiresAt > now && existing.SessionHash != sessionHash;
        if (scenario == MockScenario.SeatTaken || takenByOther)
            return Fail(scenario, 409, "이미 다른 분이 점유한 좌석이에요. 다른 좌석을 골라 주세요.", "이미 점유됨 · 409");

        var hold = new SeatHold("h-" + Guid.NewGuid().ToString("N")[..10], seatKey, sessionHash, now + HoldDuration);
        _holdsById[hold.HoldId] = hold;
        _holdsBySeat[seatKey] = hold;
        return Ok(scenario, new { holdId = hold.HoldId, expiresAt = FormatTime(hold.ExpiresAt) }, "점유 5분");
    }

    private MockResponse Pay(MockRequestInspection inspection, MockScenario scenario, DateTimeOffset now)
    {
        var body = inspection.Body!.Value;
        var sessionHash = inspection.SessionHash!;
        Touch(sessionHash, now).BookingRequestCount++;
        var holdId = body.GetProperty("holdId").GetString()!;

        if (scenario == MockScenario.HoldExpired || !_holdsById.TryGetValue(holdId, out var hold) || hold.ExpiresAt <= now)
            return Fail(scenario, 404, "좌석 점유 시간이 끝났어요. 좌석을 다시 골라 주세요.", "점유 끝남 · 404");
        if (hold.SessionHash != sessionHash)
            return Fail(scenario, 409, "다른 세션이 점유한 좌석이에요.", "다른 세션의 점유 · 409");

        // 판정이 아직 없으면 계약대로 2초 기다린 뒤 추가 확인으로 처리한다 (스트림이 멈춰도 그냥 통과시키지 않음)
        var verdict = DecideVerdict(sessionHash, scenario, now);
        if (verdict is null)
            return Accepted(scenario, StartVerification(hold, sessionHash), "판정 없음 → 추가 확인 · 202") with { Delay = PaymentVerdictWait };

        return verdict.Code switch
        {
            "VERIFY" => Accepted(scenario, StartVerification(hold, sessionHash), "추가 확인 필요 · 202"),
            "HOLD" => Accepted(scenario, new { status = "ON_HOLD" }, "보류 · 202"),
            _ => CompletePayment(hold, scenario, verdict.Code == "WATCH" ? "결제 완료 (관찰)" : "결제 완료"),
        };
    }

    private MockResponse Verify(MockRequestInspection inspection, MockScenario scenario)
    {
        if (!_verifications.TryGetValue(inspection.RouteValue!, out var verification) || !_holdsById.TryGetValue(verification.HoldId, out var hold))
            return NotFound(scenario, "추가 확인 요청을 찾지 못했어요. 결제를 다시 시도해 주세요.");

        var code = inspection.Body!.Value.GetProperty("code").GetString();
        if (code != MockScenarioExtensions.VerificationCode)
            return Fail(scenario, 400, "확인 코드가 맞지 않아요. 다시 입력해 주세요.", "코드 틀림 · 400");

        _verifications.Remove(verification.VerifyId);
        return CompletePayment(hold, scenario, "확인 후 결제 완료");
    }

    private object StartVerification(SeatHold hold, string sessionHash)
    {
        var verifyId = "v-" + Guid.NewGuid().ToString("N")[..10];
        _verifications[verifyId] = new Verification(verifyId, hold.HoldId, sessionHash);
        return new { status = "VERIFY_REQUIRED", verifyId };
    }

    private MockResponse CompletePayment(SeatHold hold, MockScenario scenario, string summary)
    {
        _holdsById.Remove(hold.HoldId);
        _holdsBySeat.Remove(hold.SeatKey);
        _soldSeats.Add(hold.SeatKey);
        return Ok(scenario, new { status = "PAID" }, summary);
    }

    // ---------- 수집 API와 판정 조회 (계약 4·5장) ----------

    private MockResponse CollectEvents(MockRequestInspection inspection, MockScenario scenario, DateTimeOffset now)
    {
        var events = inspection.Body!.Value.GetProperty("events").EnumerateArray().ToList();
        foreach (var contractEvent in events)
        {
            var activity = Touch(contractEvent.GetProperty("sessionHash").GetString()!, now);
            if (contractEvent.GetProperty("origin").GetString() == EventTypes.ClientOrigin)
                activity.ClientEventCount++;
            if (contractEvent.GetProperty("type").GetString() == EventTypes.SeatClick
                && DateTimeOffset.TryParse(contractEvent.GetProperty("occurredAt").GetString(), out var occurredAt))
                activity.SeatClickTimes.Add(occurredAt);
        }

        return new MockResponse(202, Serialize(new { accepted = events.Count }), TimeSpan.Zero, scenario, $"이벤트 {events.Count}건 · 202");
    }

    private MockResponse LookUpVerdict(MockRequestInspection inspection, MockScenario scenario, DateTimeOffset now)
    {
        var sessionHash = inspection.RouteValue!;
        var verdict = _activityBySession.ContainsKey(sessionHash) ? DecideVerdict(sessionHash, scenario, now) : null;
        if (verdict is null)
            return NotFound(scenario, "판정 전", "판정 전 · 404");

        var body = new
        {
            sessionHash,
            verdict = verdict.Code,
            score = verdict.Score,
            rules = verdict.Rules,
            ruleVersion = "mock-rules-1",
            decidedAt = FormatTime(now),
        };
        return Ok(scenario, body, $"{VerdictCodes.ToKorean(verdict.Code)}");
    }

    private sealed record MockVerdict(string Code, double Score, object[] Rules);

    // 세션의 판정. 판정 지연 중이면 null (판정 전)
    private MockVerdict? DecideVerdict(string sessionHash, MockScenario scenario, DateTimeOffset now)
    {
        var activity = Touch(sessionHash, now);
        if (scenario == MockScenario.VerdictDelayed && now - activity.FirstSeenAt!.Value < VerdictDelay)
            return null;

        static object Rule(string ruleId, string measured, string threshold) => new { ruleId, measured, threshold };
        switch (scenario)
        {
            case MockScenario.AlwaysNormal: return new("NORMAL", 0.1, []);
            case MockScenario.AlwaysWatch: return new("WATCH", 0.45, [Rule("MOCK", "가짜 서버 응답 방식", "항상 관찰")]);
            case MockScenario.AlwaysVerify: return new("VERIFY", 0.8, [Rule("MOCK", "가짜 서버 응답 방식", "항상 추가 확인")]);
            case MockScenario.AlwaysHold: return new("HOLD", 0.95, [Rule("MOCK", "가짜 서버 응답 방식", "항상 보류")]);
        }

        // 자동 판정: 실제 판정 규칙이 아니라 앱 화면을 확인하기 위한 단순한 규칙이다
        var shortestClickGap = activity.SeatClickTimes.Order().Zip(activity.SeatClickTimes.Order().Skip(1), (first, second) => second - first)
            .DefaultIfEmpty(TimeSpan.MaxValue).Min();
        if (shortestClickGap < TooFastClickInterval)
            return new("VERIFY", 0.82, [Rule("R1", $"좌석 클릭 간격 최소 {shortestClickGap.TotalSeconds:0.00}초", $"{TooFastClickInterval.TotalSeconds:0.00}초 미만")]);
        if (activity.BookingRequestCount > 0 && activity.ClientEventCount == 0)
            return new("WATCH", 0.4, [Rule("R5", "화면 기록 없이 예매 API만 호출", "화면 이벤트 0건")]);
        return new("NORMAL", 0.1, []);
    }

    private SessionActivity Touch(string sessionHash, DateTimeOffset now)
    {
        if (!_activityBySession.TryGetValue(sessionHash, out var activity))
            _activityBySession[sessionHash] = activity = new SessionActivity();
        activity.FirstSeenAt ??= now;
        return activity;
    }

    // ---------- 응답 만들기 ----------

    private static MockResponse Ok(MockScenario scenario, object body, string summary) =>
        new(200, Serialize(body), TimeSpan.Zero, scenario, $"{summary} · 200");

    private static MockResponse Accepted(MockScenario scenario, object body, string summary) =>
        new(202, Serialize(body), TimeSpan.Zero, scenario, summary);

    private static MockResponse NotFound(MockScenario scenario, string reason, string? summary = null) =>
        Fail(scenario, 404, reason, summary ?? "찾지 못함 · 404");

    private static MockResponse Fail(MockScenario scenario, int statusCode, string reason, string summary) =>
        new(statusCode, Serialize(new { error = reason }), TimeSpan.Zero, scenario, summary);

    private static MockResponse Error(MockScenario scenario, int statusCode) => new(statusCode,
        Serialize(new { error = $"가짜 예매·판정 서버가 일부러 돌려준 {statusCode} 응답이에요 ({scenario.ToQueryTag()})." }),
        TimeSpan.Zero, scenario, $"{scenario.ToKoreanName()}");

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, ResponseJsonOptions);

    // 계약 예시처럼 UTC 밀리초까지 적는다. 예: 2026-11-20T11:02:01.204Z
    private static string FormatTime(DateTimeOffset time) => ContractEventFormat.FormatTime(time);
}
