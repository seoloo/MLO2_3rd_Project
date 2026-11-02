namespace QaRunner.AppMock;

/// <summary>
/// 가짜 예매·판정 서버가 앱에 돌려줄 응답 방식. 앱이 정상 예매뿐 아니라 추가 확인·보류·대기열·좌석 경쟁·
/// 오류·지연에도 화면이 제대로 버티는지 확인하려고 일부러 여러 가지를 준비해 둔다.
/// 서버 전체의 응답 방식을 하나 고르고, 요청 헤더 X-Mock-Scenario나 주소 끝 ?mock=값을 붙이면 그 요청만 다른 응답을 받는다.
/// </summary>
public enum MockScenario
{
    /// <summary>자동 판정: 앱이 보낸 행동 이벤트를 보고 판정한다 (클릭 간격이 지나치게 짧으면 추가 확인)</summary>
    Auto,

    /// <summary>항상 정상: 결제가 바로 끝난다</summary>
    AlwaysNormal,

    /// <summary>항상 관찰: 결제는 바로 끝나고 운영자 화면에만 관찰로 보인다</summary>
    AlwaysWatch,

    /// <summary>항상 추가 확인: 결제 직전 본인 확인을 요구한다 (확인 코드 123456)</summary>
    AlwaysVerify,

    /// <summary>항상 보류: 결제가 운영자 검토 대기로 멈춘다</summary>
    AlwaysHold,

    /// <summary>긴 대기열: 대기 순번이 1,200번대에서 시작해 조회할 때마다 줄어든다</summary>
    LongQueue,

    /// <summary>이미 점유된 좌석: 좌석 점유 요청에 409</summary>
    SeatTaken,

    /// <summary>매진: 좌석 점유 요청에 410</summary>
    SoldOut,

    /// <summary>점유 시간 끝남: 결제 요청에 404 (좌석을 다시 골라야 함)</summary>
    HoldExpired,

    /// <summary>판정 지연: 판정이 12초 동안 나오지 않는다. 결제는 계약대로 2초 기다린 뒤 추가 확인으로 처리</summary>
    VerdictDelayed,

    /// <summary>3초 늦은 응답 (로딩 표시와 중복 요청 확인)</summary>
    Slow,

    /// <summary>12초 늦은 응답 (시간 초과 처리 확인)</summary>
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
    /// <summary>추가 확인 단계에서 통과하는 확인 코드</summary>
    public const string VerificationCode = "123456";

    /// <summary>화면·보고서에 보여 줄 한글 이름</summary>
    public static string ToKoreanName(this MockScenario scenario) => scenario switch
    {
        MockScenario.Auto => "자동 판정",
        MockScenario.AlwaysNormal => "항상 정상",
        MockScenario.AlwaysWatch => "항상 관찰",
        MockScenario.AlwaysVerify => "항상 추가 확인",
        MockScenario.AlwaysHold => "항상 보류",
        MockScenario.LongQueue => "긴 대기열",
        MockScenario.SeatTaken => "이미 점유된 좌석",
        MockScenario.SoldOut => "매진",
        MockScenario.HoldExpired => "점유 시간 끝남",
        MockScenario.VerdictDelayed => "판정 지연",
        MockScenario.Slow => "3초 늦은 응답",
        MockScenario.Timeout => "12초 늦은 응답",
        MockScenario.BrokenJson => "깨진 응답",
        MockScenario.BadRequest => "잘못된 요청 400",
        MockScenario.ServerError => "서버 오류 500",
        MockScenario.Unavailable => "일시 중단 503",
        _ => scenario.ToString(),
    };

    /// <summary>
    /// --scenario 옵션 값이자, 요청 하나만 바꿀 때 쓰는 짧은 이름. 예: verify → 헤더 X-Mock-Scenario: verify 또는 주소 끝 ?mock=verify
    /// </summary>
    public static string ToOptionValue(this MockScenario scenario) => scenario switch
    {
        MockScenario.Auto => "auto",
        MockScenario.AlwaysNormal => "normal",
        MockScenario.AlwaysWatch => "watch",
        MockScenario.AlwaysVerify => "verify",
        MockScenario.AlwaysHold => "hold",
        MockScenario.LongQueue => "queue",
        MockScenario.SeatTaken => "taken",
        MockScenario.SoldOut => "soldout",
        MockScenario.HoldExpired => "expired",
        MockScenario.VerdictDelayed => "delayed",
        MockScenario.Slow => "slow",
        MockScenario.Timeout => "timeout",
        MockScenario.BrokenJson => "broken",
        MockScenario.BadRequest => "400",
        MockScenario.ServerError => "500",
        MockScenario.Unavailable => "503",
        _ => scenario.ToString().ToLowerInvariant(),
    };

    /// <summary>요청 하나만 바꿀 때 붙이는 표시. 예: ?mock=verify</summary>
    public static string ToQueryTag(this MockScenario scenario) => "?mock=" + scenario.ToOptionValue();

    /// <summary>서버가 실제로 무엇을 돌려주는지</summary>
    public static string DescribeResponse(this MockScenario scenario) => scenario switch
    {
        MockScenario.Auto => "앱이 보낸 행동 이벤트를 보고 판정해요. 좌석 클릭 간격이 0.15초보다 짧으면 추가 확인, 화면 기록 없이 예매 API만 부르면 관찰, 나머지는 정상이에요.",
        MockScenario.AlwaysNormal => "모든 세션을 정상으로 판정해요. 결제는 200 { \"status\": \"PAID\" }예요.",
        MockScenario.AlwaysWatch => "모든 세션을 관찰로 판정해요. 결제는 바로 끝나고(PAID) 판정 조회에만 WATCH가 나와요.",
        MockScenario.AlwaysVerify => $"모든 세션을 추가 확인으로 판정해요. 결제는 202 VERIFY_REQUIRED이고, 확인 코드 {VerificationCode}만 통과해요.",
        MockScenario.AlwaysHold => "모든 세션을 보류로 판정해요. 결제는 202 { \"status\": \"ON_HOLD\" }예요.",
        MockScenario.LongQueue => "대기열 진입 때 순번 1,2xx번을 주고, 1초에 150명씩 줄어들어 약 8초 뒤 입장(admitted: true)시켜요.",
        MockScenario.SeatTaken => "좌석 점유 요청에 409 { \"error\": \"...\" }를 보내요.",
        MockScenario.SoldOut => "좌석 점유 요청에 410 { \"error\": \"...\" }를 보내요.",
        MockScenario.HoldExpired => "좌석 점유는 받아 주지만 결제 요청에는 점유 시간이 끝났다며 404를 보내요.",
        MockScenario.VerdictDelayed => "첫 이벤트 뒤 12초 동안 판정 조회에 404(판정 전)를 보내요. 결제는 2초 기다린 뒤 VERIFY_REQUIRED로 처리해요 (계약 3장).",
        MockScenario.Slow => "모든 요청에 3초 기다렸다가 자동 판정과 같은 응답을 보내요.",
        MockScenario.Timeout => "모든 요청에 12초 기다렸다가 응답해요.",
        MockScenario.BrokenJson => "200 상태로, 중간에 잘린 JSON을 보내요.",
        MockScenario.BadRequest => "400 상태와 { \"error\": \"...\" }를 보내요.",
        MockScenario.ServerError => "500 상태와 { \"error\": \"...\" }를 보내요.",
        MockScenario.Unavailable => "503 상태와 { \"error\": \"...\" }를 보내요.",
        _ => "",
    };

    /// <summary>이 응답을 받았을 때 앱 화면에서 확인할 것</summary>
    public static string DescribeAppExpectation(this MockScenario scenario) => scenario switch
    {
        MockScenario.Auto => "예매 흐름을 끝까지 진행해 보세요. 행동 기록기가 1초마다 이벤트를 보내면 오른쪽 목록에 ‘이벤트 수집’이 쌓이고, 결제는 바로 끝나야 해요.",
        MockScenario.AlwaysNormal => "결제 직후 예매 완료 화면이 나와야 해요. 추가 확인 화면이 나오면 안 돼요.",
        MockScenario.AlwaysWatch => "사용자 화면은 정상과 똑같아야 해요. 운영자 상황판에서만 ‘관찰’로 보이는지 확인해요.",
        MockScenario.AlwaysVerify => $"결제 버튼을 누르면 좌석을 유지한 채 본인 확인 화면이 나와야 해요. 코드 {VerificationCode}를 넣으면 결제가 끝나고, 틀린 코드에는 다시 입력하라는 안내가 나와야 해요.",
        MockScenario.AlwaysHold => "결제가 실패한 것처럼 보이지 않고 ‘운영자 검토 중’ 안내가 나와야 해요. 사용자를 매크로로 단정하는 표현은 쓰지 마세요.",
        MockScenario.LongQueue => "대기 순번이 줄어드는 게 보이고, 입장되면 좌석 화면으로 자동으로 넘어가야 해요. 새로고침을 연타하게 만들지 않는지도 봐 주세요.",
        MockScenario.SeatTaken => "‘이미 다른 분이 고른 좌석이에요’ 같은 안내와 함께 다른 좌석을 고를 수 있어야 해요.",
        MockScenario.SoldOut => "매진 안내가 나오고, 좌석 화면에서 더 누를 수 없어야 해요.",
        MockScenario.HoldExpired => "점유 시간이 끝났다는 안내와 함께 좌석 선택으로 돌아가야 해요. 남은 점유 시간 표시도 확인해 주세요.",
        MockScenario.VerdictDelayed => "판정이 늦어도 결제가 그냥 통과되지 않고 본인 확인 화면으로 가야 해요. 운영자 상황판은 ‘판정 전’을 오류와 구분해 보여 줘야 해요.",
        MockScenario.Slow => "기다리는 동안 로딩 표시가 보이고, 버튼을 여러 번 눌러도 요청이 한 번만 가야 해요. 오른쪽 받은 요청 목록에서 건수를 확인해 보세요.",
        MockScenario.Timeout => "10초가 지나면 기다림을 멈추고 오류 안내와 다시 시도 방법이 보여야 해요.",
        MockScenario.BrokenJson => "응답을 읽지 못해도 앱이 멈추지 않고 오류 안내가 보여야 해요.",
        MockScenario.BadRequest => "입력을 다시 확인하라는 안내가 보여야 해요.",
        MockScenario.ServerError or MockScenario.Unavailable =>
            "예매 결과처럼 보이지 않는 오류 안내가 나오고, 고른 좌석을 잃지 않은 채 다시 시도할 수 있어야 해요.",
        _ => "",
    };

    /// <summary>
    /// --scenario 옵션 값이나 요청 표시(?mock=) 값을 응답 방식으로 바꾼다. 값이 없으면 자동 판정.
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

    /// <summary>옵션 값이나 요청 표시 값에 맞는 응답 방식을 찾는다. 없으면 false</summary>
    public static bool TryParseOptionValue(string optionValue, out MockScenario scenario)
    {
        foreach (var candidate in Enum.GetValues<MockScenario>())
        {
            if (string.Equals(candidate.ToOptionValue(), optionValue.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                scenario = candidate;
                return true;
            }
        }

        scenario = MockScenario.Auto;
        return false;
    }
}
