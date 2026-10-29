namespace QaRunner.AppMock;

/// <summary>
/// 가짜 판정 서버가 앱에 돌려줄 응답 방식. 앱이 정상 판정뿐 아니라 오류·지연·이상한 응답에도
/// 화면이 제대로 버티는지 확인하려고 일부러 여러 가지를 준비해 둔다.
/// 서버 전체의 응답 방식을 하나 고르고, 문자 안에 #500처럼 표시를 넣으면 그 요청만 다른 응답을 받는다.
/// </summary>
public enum MockScenario
{
    /// <summary>자동 판정: 문자 내용의 단어를 보고 네 가지 판정 중 하나를 돌려준다</summary>
    Auto,

    /// <summary>항상 고위험</summary>
    HighRisk,

    /// <summary>항상 주의</summary>
    Caution,

    /// <summary>항상 신호 미발견</summary>
    NoSignal,

    /// <summary>항상 판단 보류</summary>
    Undecided,

    /// <summary>근거가 빈 배열이고 대응 안내가 없는 판정</summary>
    NoEvidence,

    /// <summary>근거 5개와 아주 긴 대응 안내 (화면이 잘리거나 넘치지 않는지)</summary>
    LongText,

    /// <summary>계약에 없는 판정 코드 (앱이 모르는 값을 받았을 때)</summary>
    UnknownVerdict,

    /// <summary>3초 늦게 오는 정상 판정 (로딩 표시와 중복 요청 확인)</summary>
    Slow,

    /// <summary>12초 늦게 오는 응답 (10초 시간 초과 처리 확인)</summary>
    Timeout,

    /// <summary>중간에 잘린 JSON (응답을 읽지 못할 때)</summary>
    BrokenJson,

    /// <summary>400 잘못된 요청</summary>
    BadRequest,

    /// <summary>500 서버 내부 오류</summary>
    ServerError,

    /// <summary>503 일시 중단</summary>
    Unavailable,
}

public static class MockScenarioExtensions
{
    /// <summary>화면·보고서에 보여 줄 한글 이름</summary>
    public static string ToKoreanName(this MockScenario scenario) => scenario switch
    {
        MockScenario.Auto => "자동 판정",
        MockScenario.HighRisk => "항상 고위험",
        MockScenario.Caution => "항상 주의",
        MockScenario.NoSignal => "항상 신호 미발견",
        MockScenario.Undecided => "항상 판단 보류",
        MockScenario.NoEvidence => "근거 없는 판정",
        MockScenario.LongText => "아주 긴 근거·안내",
        MockScenario.UnknownVerdict => "모르는 판정 코드",
        MockScenario.Slow => "3초 늦은 응답",
        MockScenario.Timeout => "12초 늦은 응답",
        MockScenario.BrokenJson => "깨진 응답",
        MockScenario.BadRequest => "잘못된 요청 400",
        MockScenario.ServerError => "서버 오류 500",
        MockScenario.Unavailable => "일시 중단 503",
        _ => scenario.ToString(),
    };

    /// <summary>
    /// --scenario 옵션 값이자 문자에 넣는 표시(# 뒤)에 쓰는 짧은 이름. 예: 500 → 문자에 "#500"
    /// </summary>
    public static string ToOptionValue(this MockScenario scenario) => scenario switch
    {
        MockScenario.Auto => "auto",
        MockScenario.HighRisk => "high",
        MockScenario.Caution => "caution",
        MockScenario.NoSignal => "nosignal",
        MockScenario.Undecided => "undecided",
        MockScenario.NoEvidence => "noevidence",
        MockScenario.LongText => "long",
        MockScenario.UnknownVerdict => "unknown",
        MockScenario.Slow => "slow",
        MockScenario.Timeout => "timeout",
        MockScenario.BrokenJson => "broken",
        MockScenario.BadRequest => "400",
        MockScenario.ServerError => "500",
        MockScenario.Unavailable => "503",
        _ => scenario.ToString().ToLowerInvariant(),
    };

    /// <summary>문자 안에 넣으면 그 요청만 이 응답을 받는 표시. 예: #500</summary>
    public static string ToMessageTag(this MockScenario scenario) => "#" + scenario.ToOptionValue();

    /// <summary>서버가 실제로 무엇을 돌려주는지</summary>
    public static string DescribeResponse(this MockScenario scenario) => scenario switch
    {
        MockScenario.Auto => "문자에 인증번호·앱 설치가 있으면 고위험, 링크·택배·송금이 있으면 주의, 너무 짧으면 판단 보류, 나머지는 신호 미발견으로 200 응답을 보내요.",
        MockScenario.HighRisk => "verdict가 HIGH_RISK인 200 응답을 보내요.",
        MockScenario.Caution => "verdict가 CAUTION인 200 응답을 보내요.",
        MockScenario.NoSignal => "verdict가 NO_SIGNAL인 200 응답을 보내요.",
        MockScenario.Undecided => "verdict가 UNDECIDED인 200 응답을 보내요.",
        MockScenario.NoEvidence => "verdict는 CAUTION이지만 evidence가 빈 배열이고 guidance가 없는 200 응답을 보내요.",
        MockScenario.LongText => "근거 5개와 아주 긴 대응 안내가 든 고위험 200 응답을 보내요.",
        MockScenario.UnknownVerdict => "계약에 없는 판정 코드 VERY_DANGEROUS를 담은 200 응답을 보내요.",
        MockScenario.Slow => "3초 기다렸다가 자동 판정으로 200 응답을 보내요.",
        MockScenario.Timeout => "12초 기다렸다가 응답해요. 계약의 10초 제한을 넘기는 응답이에요.",
        MockScenario.BrokenJson => "200 상태로, 중간에 잘린 JSON을 보내요.",
        MockScenario.BadRequest => "400 상태와 { \"error\": \"...\" }를 보내요.",
        MockScenario.ServerError => "500 상태와 { \"error\": \"...\" }를 보내요.",
        MockScenario.Unavailable => "503 상태와 { \"error\": \"...\" }를 보내요.",
        _ => "",
    };

    /// <summary>이 응답을 받았을 때 앱 화면에서 확인할 것</summary>
    public static string DescribeAppExpectation(this MockScenario scenario) => scenario switch
    {
        MockScenario.Auto => "문자를 바꿔 가며 보내 보세요. 결과 화면에 한글 판정(고위험·주의·신호 미발견·판단 보류)과 근거·대응 안내가 함께 보여야 해요.",
        MockScenario.HighRisk => "‘고위험’이 한글로 가장 눈에 띄게 보이고, 근거와 대응 안내가 함께 나와야 해요.",
        MockScenario.Caution => "‘주의’가 한글로 보이고 고위험과 다른 색·표현으로 구분되어야 해요.",
        MockScenario.NoSignal => "‘신호 미발견’이 ‘안전’처럼 읽히지 않아야 해요. 위험 신호를 못 찾았다는 뜻으로 보여 주세요.",
        MockScenario.Undecided => "‘판단 보류’가 오류처럼 보이지 않고, 정보가 부족하다는 안내가 나와야 해요.",
        MockScenario.NoEvidence => "근거·안내가 없어도 화면이 깨지거나 빈 칸만 남지 않고 판정은 그대로 보여야 해요.",
        MockScenario.LongText => "긴 근거와 안내가 잘리지 않고 스크롤로 끝까지 읽혀야 해요. 버튼이 화면 밖으로 밀려나지 않는지도 봐 주세요.",
        MockScenario.UnknownVerdict => "영문 코드를 그대로 보여 주거나 앱이 멈추면 안 돼요. 어떻게 보여 줄지는 A·B가 정해 계약 문서에 적어 주세요.",
        MockScenario.Slow => "기다리는 동안 로딩 표시가 보이고, 버튼을 여러 번 눌러도 요청이 한 번만 가야 해요. 오른쪽 받은 요청 목록에서 건수를 확인해 보세요.",
        MockScenario.Timeout => "10초가 지나면 기다림을 멈추고 ‘시스템 실패’ 안내와 다시 시도 방법이 보여야 해요.",
        MockScenario.BrokenJson => "응답을 읽지 못해도 앱이 멈추지 않고 ‘시스템 실패’로 보여야 해요.",
        MockScenario.BadRequest => "입력을 다시 확인하라는 안내가 보여야 해요. 판정 결과처럼 보이면 안 돼요.",
        MockScenario.ServerError or MockScenario.Unavailable =>
            "판정 대신 ‘시스템 실패’ 안내가 판정 결과와 분명히 구분되어 보이고, 다시 시도할 수 있어야 해요.",
        _ => "",
    };

    /// <summary>
    /// --scenario 옵션 값이나 문자 표시(# 뒤)를 응답 방식으로 바꾼다. 값이 없으면 자동 판정.
    /// 알 수 없는 값이면 ArgumentException을 던진다.
    /// </summary>
    public static MockScenario ParseOptionValue(string? optionValue)
    {
        if (optionValue is null)
            return MockScenario.Auto;

        return TryParseOptionValue(optionValue, out var scenario)
            ? scenario
            : throw new ArgumentException(
                $"--scenario 값은 {string.Join(", ", Enum.GetValues<MockScenario>().Select(item => item.ToOptionValue()))} 중 하나여야 합니다: {optionValue}");
    }

    /// <summary>옵션 값이나 문자 표시(# 뒤)에 맞는 응답 방식을 찾는다. 없으면 false</summary>
    public static bool TryParseOptionValue(string optionValue, out MockScenario scenario)
    {
        foreach (var candidate in Enum.GetValues<MockScenario>())
        {
            if (string.Equals(candidate.ToOptionValue(), optionValue, StringComparison.OrdinalIgnoreCase))
            {
                scenario = candidate;
                return true;
            }
        }

        scenario = MockScenario.Auto;
        return false;
    }
}
