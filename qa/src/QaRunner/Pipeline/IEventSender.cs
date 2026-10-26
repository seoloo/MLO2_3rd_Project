namespace QaRunner.Pipeline;

/// <summary>
/// 이벤트를 파이프라인에 넣는 쪽. 지금은 FakePipeline을 쓰고,
/// C의 파이프라인이 준비되면 Kafka에 발행하는 구현을 새로 만들어 바꿔 끼운다.
/// </summary>
public interface IEventSender
{
    /// <summary>
    /// 이벤트 하나를 보낸다. 보내지 못하면 EventSendException을 던진다.
    /// </summary>
    Task SendAsync(PipelineEvent pipelineEvent, CancellationToken cancellationToken);
}

/// <summary>
/// 이벤트를 파이프라인에 넣지 못했을 때 쓰는 예외 (Kafka 연결 실패 등).
/// 채점기는 이 예외가 난 시나리오를 시스템 실패로 기록한다.
/// </summary>
public sealed class EventSendException(string message, Exception? innerException = null)
    : Exception(message, innerException);
