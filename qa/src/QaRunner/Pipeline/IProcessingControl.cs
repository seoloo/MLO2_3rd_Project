namespace QaRunner.Pipeline;

/// <summary>
/// 파이프라인의 처리 단계(Spark)를 멈추고 다시 켜는 쪽. 복구 시험에서 장애를 일부러 만드는 데 쓴다.
/// 지금은 FakeStreamingPipeline을 쓰고, 인프라가 생기면 docker stop/start로 Spark 컨테이너를 제어하는 구현으로 바꾼다.
/// </summary>
public interface IProcessingControl
{
    /// <summary>처리를 멈춘다. 이벤트는 계속 받지만(Kafka에 쌓임) Gold에 반영되지 않는다.</summary>
    Task StopProcessingAsync(CancellationToken cancellationToken);

    /// <summary>처리를 다시 시작한다. 멈춰 있던 동안 쌓인 이벤트부터 따라잡는다.</summary>
    Task StartProcessingAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 처리를 멈추거나 다시 켜지 못했을 때 쓰는 예외 (컨테이너 제어 실패 등).
/// 복구 시험은 이 예외가 나면 결과를 시스템 실패로 기록한다.
/// </summary>
public sealed class ProcessingControlException(string message, Exception? innerException = null)
    : Exception(message, innerException);
