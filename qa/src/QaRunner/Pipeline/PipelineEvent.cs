namespace QaRunner.Pipeline;

/// <summary>
/// QA 러너가 파이프라인(Kafka)에 넣는 행동 이벤트 한 건. Kafka로 보낼 때는 ContractEventFormat이
/// docs/contract.md 4-1장의 전체 필드(origin, deviceHash, showId 등)를 채운다.
/// </summary>
/// <param name="RunId">어느 QA 실행에서 보낸 이벤트인지. 결과를 읽을 때 이번 실행 것만 골라내는 데 쓴다</param>
/// <param name="CaseId">어느 시나리오에서 보낸 이벤트인지</param>
/// <param name="EventId">이벤트 고유 번호. Silver는 이 값으로 중복을 없앤다</param>
/// <param name="SessionHash">세션 해시. Kafka 메시지 키로 쓴다</param>
/// <param name="EventTime">이벤트가 실제로 일어난 시각(occurredAt). Gold는 이 시각 기준 60초 구간으로 집계한다</param>
/// <param name="EventType">이벤트 종류. 예: seat_click (docs/contract.md 4-2장)</param>
public sealed record PipelineEvent(
    string RunId,
    string CaseId,
    string EventId,
    string SessionHash,
    DateTimeOffset EventTime,
    string EventType);
