namespace QaRunner.Pipeline;

/// <summary>
/// 파이프라인 시험 시나리오 하나. cases/pipeline 폴더의 JSON 파일 하나에 해당한다.
/// 어떤 이벤트를 어떤 조건(중복·역순·지연)으로 보낼지 적는다. 정답은 적지 않는다(정답 계산기가 계산).
/// </summary>
public sealed record PipelineScenario
{
    /// <summary>시나리오 번호. 예: DUP-001</summary>
    public required string CaseId { get; init; }

    /// <summary>무엇을 확인하는 시나리오인지 한 줄 설명. 보고서의 입력 칸에 나온다</summary>
    public string Description { get; init; } = "";

    /// <summary>보낼 이벤트 목록</summary>
    public List<ScenarioEvent> Events { get; init; } = [];

    /// <summary>보낼 때 넣을 조건</summary>
    public FaultInjection Injection { get; init; } = new();
}

/// <summary>
/// 시나리오 안의 이벤트 한 건.
/// </summary>
public sealed record ScenarioEvent
{
    /// <summary>이벤트 고유 번호. 같은 번호를 다른 내용으로 두 번 적으면 "같은 ID 상충" 시나리오가 된다</summary>
    public required string EventId { get; init; }

    /// <summary>검사 번호</summary>
    public required string ScanId { get; init; }

    /// <summary>시나리오 시작 기준으로 몇 초 뒤에 일어난 이벤트인지</summary>
    public int OffsetSeconds { get; init; }

    /// <summary>판정</summary>
    public string Verdict { get; init; } = "신호 미발견";
}

/// <summary>
/// 이벤트를 보낼 때 일부러 넣는 조건 (실행안의 중복·역순·지연 주입).
/// </summary>
public sealed record FaultInjection
{
    /// <summary>같은 이벤트를 몇 번 보낼지. 1이면 중복 없음</summary>
    public int DuplicateCount { get; init; } = 1;

    /// <summary>true면 나중에 일어난 이벤트부터 거꾸로 보낸다</summary>
    public bool SendInReverseOrder { get; init; }

    /// <summary>이벤트가 일어난 뒤 몇 초 늦게 도착시킬지. 300초(5분)를 넘으면 격리 대상</summary>
    public int DelaySeconds { get; init; }
}
