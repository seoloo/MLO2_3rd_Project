namespace QaRunner.Pipeline;

/// <summary>
/// 실제 파이프라인(Kafka·MongoDB·Spark)에 연결할 때 필요한 주소와 이름.
/// 이 설정이 있으면 파이프라인·부하·복구 시험이 가짜 대신 실제 파이프라인을 대상으로 돈다.
/// 기본값은 docs/contract.md 4~6장과 같다.
/// </summary>
/// <param name="KafkaBootstrapServers">Kafka 주소. 예: localhost:9092</param>
/// <param name="MongoConnectionString">MongoDB 연결 문자열. 예: mongodb://localhost:27017</param>
/// <param name="KafkaTopic">이벤트를 넣을 토픽</param>
/// <param name="MongoDatabase">메달리온 컬렉션이 있는 데이터베이스</param>
/// <param name="SparkContainerName">복구 시험에서 멈췄다 켤 Spark 컨테이너 이름</param>
public sealed record PipelineConnectionSettings(
    string KafkaBootstrapServers,
    string MongoConnectionString,
    string KafkaTopic = PipelineConnectionSettings.DefaultKafkaTopic,
    string MongoDatabase = PipelineConnectionSettings.DefaultMongoDatabase,
    string SparkContainerName = PipelineConnectionSettings.DefaultSparkContainerName)
{
    public const string DefaultKafkaTopic = "scan-events";
    public const string DefaultMongoDatabase = "jeongbo";
    public const string DefaultSparkContainerName = "spark";
}
