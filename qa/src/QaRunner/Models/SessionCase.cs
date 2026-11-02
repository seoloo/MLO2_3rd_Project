namespace QaRunner.Models;

/// <summary>
/// 판정 시험 사례 한 건: 사람인지 어떤 봇인지 정답을 아는 예매 세션 하나. 사례 파일(cases/sessions.json)의 한 항목이다.
/// 판정 대상에는 행동(Steps)만 보내고, 행위자와 정답은 QA 안에서만 쓴다 (만드는 쪽과 판정하는 쪽 분리).
/// </summary>
/// <param name="CaseId">사례 번호. 예: S-004</param>
/// <param name="Actor">행위자. 사람, 봇 A·B·C·D, 새 유형 봇 중 하나로 시작한다. 탐지율·오탐률을 나눠 셀 때 쓴다</param>
/// <param name="Description">어떤 행동인지 한 줄 설명. 보고서의 입력 칸에 나온다</param>
/// <param name="ExpectedVerdict">정답 판정 (정상·관찰·추가 확인·보류). 채점은 묶음(정상 처리 / 추가 확인 이상)으로 비교한다</param>
/// <param name="Steps">예매 오픈 시각부터 순서대로 일어난 행동</param>
public sealed record SessionCase(
    string CaseId,
    string Actor,
    string Description,
    string ExpectedVerdict,
    IReadOnlyList<SessionStep> Steps)
{
    /// <summary>
    /// 같은 기기에서 같은 행동을 동시에 하는 세션 수. 봇 D(한 기기 다중 세션)처럼 2 이상이면
    /// 같은 deviceHash로 세션을 여러 개 만들어 보내고, 첫 세션의 판정을 채점한다.
    /// </summary>
    public int ParallelSessions { get; init; } = 1;

    /// <summary>사람이 한 행동인지 (오탐률 계산용)</summary>
    public bool IsHuman => Actor.StartsWith("사람", StringComparison.Ordinal);

    /// <summary>
    /// 행동 흐름을 한 줄로. 예: "0.03초 대기열 진입 → 0.13초 좌석 클릭 → 0.33초 좌석 점유"
    /// </summary>
    public string DescribeSteps() => string.Join(" → ", Steps.OrderBy(step => step.AtMs)
        .Select(step => $"{step.AtMs / 1000.0:0.00}초 {QaRunner.Pipeline.EventTypes.ToKoreanName(step.Type)}"));
}

/// <summary>
/// 세션 안의 행동 하나. 이벤트 종류와 화면 이름은 docs/contract.md 4-2장을 따른다.
/// </summary>
/// <param name="Type">이벤트 종류. 예: page_view, queue_enter, seat_click, seat_hold, payment_request</param>
/// <param name="AtMs">예매 오픈 시각부터 몇 밀리초 뒤인지</param>
/// <param name="Screen">이벤트가 난 화면. 비우면 종류에 맞는 기본 화면을 쓴다</param>
/// <param name="X">클릭 위치 (클릭 이벤트만)</param>
/// <param name="Y">클릭 위치 (클릭 이벤트만)</param>
public sealed record SessionStep(string Type, int AtMs, string? Screen = null, int? X = null, int? Y = null);
