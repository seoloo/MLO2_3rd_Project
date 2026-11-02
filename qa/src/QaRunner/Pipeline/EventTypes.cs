namespace QaRunner.Pipeline;

/// <summary>
/// 행동 이벤트 종류와, 그 이벤트를 누가 만드는지(origin)·어느 화면에서 나는지 (docs/contract.md 4-2장).
/// 계약에 이벤트 종류가 늘면 이 표에 한 줄 추가한다.
/// </summary>
public static class EventTypes
{
    /// <summary>기기(앱)가 보내는 이벤트. 봇이 꾸며 보낼 수 있다</summary>
    public const string ClientOrigin = "client";

    /// <summary>예매 API가 요청을 받고 직접 만드는 이벤트</summary>
    public const string ServerOrigin = "server";

    /// <summary>좌석 클릭. 파이프라인 시험과 스트림 부하의 기본 이벤트 종류</summary>
    public const string SeatClick = "seat_click";

    // 종류 → (만드는 곳, 기본 화면)
    private static readonly Dictionary<string, (string Origin, string Screen)> KnownTypes = new(StringComparer.Ordinal)
    {
        ["page_view"] = (ClientOrigin, "show"),
        ["show_select"] = (ClientOrigin, "show"),
        ["queue_enter"] = (ServerOrigin, "queue"),
        [SeatClick] = (ClientOrigin, "seat"),
        ["seat_hold"] = (ServerOrigin, "seat"),
        ["payment_request"] = (ServerOrigin, "payment"),
        ["verify_submit"] = (ServerOrigin, "verify"),
        ["back_navigation"] = (ClientOrigin, "seat"),
    };

    // 화면·보고서에 보여 줄 한글 이름
    private static readonly Dictionary<string, string> KoreanNames = new(StringComparer.Ordinal)
    {
        ["page_view"] = "화면 보기",
        ["show_select"] = "공연 선택",
        ["queue_enter"] = "대기열 진입",
        [SeatClick] = "좌석 클릭",
        ["seat_hold"] = "좌석 점유",
        ["payment_request"] = "결제 요청",
        ["verify_submit"] = "추가 확인 제출",
        ["back_navigation"] = "뒤로 가기",
    };

    /// <summary>이벤트 종류의 한글 이름. 모르는 종류는 그대로 돌려준다</summary>
    public static string ToKoreanName(string type) => KoreanNames.TryGetValue(type, out var name) ? name : type;

    /// <summary>계약에 있는 이벤트 종류 전체</summary>
    public static IReadOnlyCollection<string> All => KnownTypes.Keys;

    /// <summary>계약에 있는 이벤트 종류인지</summary>
    public static bool IsKnown(string? type) => type is not null && KnownTypes.ContainsKey(type);

    /// <summary>이벤트를 누가 만드는지 (client / server). 모르는 종류는 client로 본다</summary>
    public static string OriginOf(string type) => KnownTypes.TryGetValue(type, out var info) ? info.Origin : ClientOrigin;

    /// <summary>종류에 맞는 기본 화면 이름</summary>
    public static string DefaultScreenOf(string type) => KnownTypes.TryGetValue(type, out var info) ? info.Screen : "seat";
}
