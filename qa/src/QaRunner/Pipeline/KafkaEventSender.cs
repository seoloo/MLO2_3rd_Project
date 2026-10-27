using Confluent.Kafka;

namespace QaRunner.Pipeline;

/// <summary>
/// 실제 Kafka에 QA 이벤트를 넣는다 (docs/contract.md 4장: 토픽 scan-events, 키 scanId).
/// 아직 실제 Kafka로 확인하지 못한 코드다. 인프라가 생기면 pipeline/README.md의 명령으로 확인한다.
/// </summary>
public sealed class KafkaEventSender : IEventSender, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;

    /// <param name="bootstrapServers">Kafka 주소. 예: localhost:9092</param>
    /// <param name="topic">이벤트를 넣을 토픽</param>
    public KafkaEventSender(string bootstrapServers, string topic)
    {
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,

            // 모든 복제본이 받았다고 확인해 줄 때까지 기다린다 (실행안의 "발행 확인 후 전송 표기")
            Acks = Acks.All,

            // Kafka 쪽에서 재시도로 생기는 중복을 막는다. QA가 일부러 넣는 중복은 eventId가 같아 그대로 간다
            EnableIdempotence = true,

            // 이 시간 안에 확인을 못 받으면 보내기 실패로 본다
            MessageTimeoutMs = 10_000,
        };

        _producer = new ProducerBuilder<string, string>(producerConfig).Build();
        _topic = topic;
    }

    public async Task SendAsync(PipelineEvent pipelineEvent, CancellationToken cancellationToken)
    {
        var message = new Message<string, string>
        {
            Key = pipelineEvent.ScanId,
            Value = ContractEventFormat.ToJson(pipelineEvent),
        };

        try
        {
            // 확인(ack)을 받을 때까지 기다린다
            await _producer.ProduceAsync(_topic, message, cancellationToken);
        }
        catch (KafkaException exception)
        {
            throw new EventSendException($"Kafka에 보내지 못했습니다: {exception.Error.Reason}", exception);
        }
    }

    public void Dispose() => _producer.Dispose();
}
