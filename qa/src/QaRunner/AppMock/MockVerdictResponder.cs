using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QaRunner.AppMock;

/// <summary>
/// 가짜 판정 서버가 돌려줄 응답 하나.
/// </summary>
/// <param name="StatusCode">HTTP 상태 코드. 예: 200, 500</param>
/// <param name="Body">응답 본문 (JSON, 깨진 응답이면 잘린 JSON)</param>
/// <param name="Delay">응답을 보내기 전에 기다릴 시간</param>
/// <param name="Scenario">이 응답을 고른 응답 방식</param>
/// <param name="VerdictCode">판정을 돌려줬으면 그 코드(HIGH_RISK 등), 오류 응답이면 null</param>
public sealed record MockResponse(int StatusCode, string Body, TimeSpan Delay, MockScenario Scenario, string? VerdictCode);

/// <summary>
/// 요청 하나에 어떤 응답을 돌려줄지 정한다. 네트워크와 상관없는 순수한 계산이라 단위 시험으로 바로 확인할 수 있다.
/// 응답 형식은 docs/contract.md 3장(판정 API)을 따른다.
/// </summary>
public static partial class MockVerdictResponder
{
    /// <summary>3초 늦은 응답에서 기다리는 시간</summary>
    public static readonly TimeSpan SlowDelay = TimeSpan.FromSeconds(3);

    /// <summary>12초 늦은 응답에서 기다리는 시간. 계약의 10초 제한보다 길게 잡는다</summary>
    public static readonly TimeSpan TimeoutDelay = TimeSpan.FromSeconds(12);

    /// <summary>계약에 없는 판정 코드 응답에 넣는 값</summary>
    public const string UnknownVerdictCode = "VERY_DANGEROUS";

    // 한글을 \uXXXX로 바꾸지 않아서 앱 개발자가 응답 본문을 그대로 읽을 수 있게 한다
    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 이번 요청에 쓸 응답 방식을 고른다. 문자에 #500 같은 표시가 있으면 그 응답 방식을, 없으면 서버에서 고른 응답 방식을 쓴다.
    /// </summary>
    /// <param name="message">앱이 보낸 문자 내용</param>
    /// <param name="selectedScenario">서버 전체에 골라 둔 응답 방식</param>
    public static MockScenario ChooseScenario(string? message, MockScenario selectedScenario)
    {
        if (message is not null)
        {
            foreach (Match tagMatch in MessageTagPattern().Matches(message))
            {
                if (MockScenarioExtensions.TryParseOptionValue(tagMatch.Groups[1].Value, out var taggedScenario))
                    return taggedScenario;
            }
        }

        return selectedScenario;
    }

    /// <summary>
    /// 응답 방식과 문자 내용으로 응답을 만든다.
    /// </summary>
    /// <param name="scenario">ChooseScenario로 고른 응답 방식</param>
    /// <param name="message">앱이 보낸 문자 내용 (자동 판정에 씀)</param>
    /// <param name="checkedAt">응답에 넣을 판정 시각</param>
    public static MockResponse Respond(MockScenario scenario, string message, DateTimeOffset checkedAt) => scenario switch
    {
        MockScenario.Auto => VerdictResponse(scenario, DecideAutoVerdict(message), checkedAt),
        MockScenario.HighRisk => VerdictResponse(scenario, "HIGH_RISK", checkedAt),
        MockScenario.Caution => VerdictResponse(scenario, "CAUTION", checkedAt),
        MockScenario.NoSignal => VerdictResponse(scenario, "NO_SIGNAL", checkedAt),
        MockScenario.Undecided => VerdictResponse(scenario, "UNDECIDED", checkedAt),
        MockScenario.NoEvidence => new MockResponse(200,
            Serialize(new { scanId = Guid.NewGuid(), verdict = "CAUTION", evidence = Array.Empty<string>(), checkedAt = FormatTime(checkedAt) }),
            TimeSpan.Zero, scenario, "CAUTION"),
        MockScenario.LongText => new MockResponse(200,
            Serialize(new { scanId = Guid.NewGuid(), verdict = "HIGH_RISK", evidence = LongEvidence, guidance = LongGuidance, checkedAt = FormatTime(checkedAt) }),
            TimeSpan.Zero, scenario, "HIGH_RISK"),
        MockScenario.UnknownVerdict => VerdictResponse(scenario, UnknownVerdictCode, checkedAt),
        MockScenario.Slow => VerdictResponse(scenario, DecideAutoVerdict(message), checkedAt) with { Delay = SlowDelay },
        MockScenario.Timeout => VerdictResponse(scenario, DecideAutoVerdict(message), checkedAt) with { Delay = TimeoutDelay },
        MockScenario.BrokenJson => new MockResponse(200,
            $"{{\"scanId\": \"{Guid.NewGuid()}\", \"verdict\": \"HIGH_RISK\", \"evidence\": [\"인증번호",
            TimeSpan.Zero, scenario, null),
        MockScenario.BadRequest => ErrorResponse(scenario, 400),
        MockScenario.ServerError => ErrorResponse(scenario, 500),
        MockScenario.Unavailable => ErrorResponse(scenario, 503),
        _ => ErrorResponse(scenario, 500),
    };

    /// <summary>
    /// 계약에 맞지 않는 요청에 돌려줄 오류 응답 (경로 404, 방식 405, 본문 400).
    /// </summary>
    /// <param name="statusCode">상태 코드</param>
    /// <param name="reason">무엇이 틀렸는지 (본문 error에 넣음)</param>
    public static MockResponse ContractErrorResponse(int statusCode, string reason) =>
        new(statusCode, Serialize(new { error = reason }), TimeSpan.Zero, MockScenario.Auto, null);

    /// <summary>
    /// 자동 판정: 실제 판정 규칙이 아니라 앱 화면을 확인하기 위한 단순한 단어 규칙이다.
    /// 인증번호·앱 설치 → 고위험, 링크·택배·송금 → 주의, 6글자 미만 → 판단 보류, 나머지 → 신호 미발견.
    /// </summary>
    /// <param name="message">앱이 보낸 문자 내용</param>
    public static string DecideAutoVerdict(string message)
    {
        var text = message.Trim();
        if (text.Length < 6)
            return "UNDECIDED";
        if (HighRiskWords.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)))
            return "HIGH_RISK";
        if (CautionWords.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)))
            return "CAUTION";
        return "NO_SIGNAL";
    }

    private static readonly string[] HighRiskWords = ["인증번호", "앱 설치", "앱을 설치", "apk", "원격"];
    private static readonly string[] CautionWords = ["http", "링크", "클릭", "택배", "송금", "계좌", "대출"];

    // 판정 코드마다 앱 결과 화면에 보여 줄 근거와 대응 안내
    private static (string[] Evidence, string Guidance) ExplanationFor(string verdictCode) => verdictCode switch
    {
        "HIGH_RISK" => (["인증번호를 다른 사람에게 알려 달라는 요청이 있습니다"],
            "전달을 멈추고, 직접 찾은 공식 앱이나 고객센터에서 확인하세요."),
        "CAUTION" => (["모르는 링크로 접속을 유도합니다"],
            "링크를 누르기 전에 보낸 곳을 공식 번호로 확인하세요."),
        "NO_SIGNAL" => ([],
            "위험 신호를 찾지 못했어요. 그래도 개인정보·인증번호 요청에는 응하지 마세요."),
        "UNDECIDED" => (["문자 내용이 너무 짧아 판단할 근거가 부족합니다"],
            "문자 전체를 붙여 넣으면 다시 확인할 수 있어요."),
        _ => (["가짜 판정 서버가 일부러 보낸 모르는 판정 코드입니다"],
            "앱이 모르는 판정 코드를 받았을 때 화면을 확인하세요."),
    };

    private static readonly string[] LongEvidence =
    [
        "상담원이라고 밝힌 사람이 휴대폰으로 받은 인증번호 여섯 자리를 문자로 다시 보내 달라고 요청합니다. 금융기관과 공공기관은 인증번호를 묻지 않습니다.",
        "공식 앱 마켓이 아닌 주소에서 보안 앱을 내려받아 설치하라고 안내합니다. 이런 앱은 문자와 통화 기록을 빼 가는 경우가 많습니다.",
        "오늘 안에 처리하지 않으면 계좌가 정지된다며 서두르게 만듭니다. 급하게 결정하게 만드는 표현은 사기 문자의 대표적인 특징입니다.",
        "보낸 번호가 기관의 대표 번호가 아니고 개인 휴대폰 번호입니다.",
        "링크 주소가 기관 이름과 비슷하지만 철자가 한두 글자 다른 가짜 주소입니다.",
    ];

    private const string LongGuidance =
        "지금 바로 답장과 링크 접속을 멈추세요. 이미 인증번호를 알려 줬다면 해당 금융기관 고객센터에 전화해 거래를 막고, " +
        "설치한 앱이 있다면 비행기 모드로 바꾼 뒤 삭제하세요. 그다음 경찰청(112)이나 금융감독원(1332)에 신고하고, " +
        "가족이나 지인에게도 같은 문자가 오지 않았는지 알려 주세요. 의심스러운 문자는 이 앱에 다시 붙여 넣어 확인할 수 있어요.";

    private static MockResponse VerdictResponse(MockScenario scenario, string verdictCode, DateTimeOffset checkedAt)
    {
        var (evidence, guidance) = ExplanationFor(verdictCode);
        var body = Serialize(new { scanId = Guid.NewGuid(), verdict = verdictCode, evidence, guidance, checkedAt = FormatTime(checkedAt) });
        return new MockResponse(200, body, TimeSpan.Zero, scenario, verdictCode);
    }

    private static MockResponse ErrorResponse(MockScenario scenario, int statusCode) => new(statusCode,
        Serialize(new { error = $"가짜 판정 서버가 일부러 돌려준 {statusCode} 응답이에요 ({scenario.ToMessageTag()})." }),
        TimeSpan.Zero, scenario, null);

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, ResponseJsonOptions);

    // 계약 예시처럼 UTC 초 단위로 적는다. 예: 2026-11-02T09:00:10Z
    private static string FormatTime(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    // 문자 안의 #500, #slow 같은 표시
    [GeneratedRegex(@"#([A-Za-z0-9]+)")]
    private static partial Regex MessageTagPattern();
}
