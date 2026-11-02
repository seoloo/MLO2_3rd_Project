using System.Text.Json;
using QaRunner.Models;
using QaRunner.Pipeline;
using QaRunner.Verdicts;

namespace QaRunner.Tests;

/// <summary>
/// 실제 API·Kafka·MongoDB·Docker에 연결하는 코드 중, 실제 서버 없이 확인할 수 있는 부분을 시험한다.
/// (형식 변환, 옵션 읽기, 연결 실패 처리). 실제 연결은 인프라가 생긴 뒤 pipeline/README.md의 명령으로 확인한다.
/// </summary>
public class RealConnectionTests
{
    [Theory]
    [InlineData("NORMAL", "정상")]
    [InlineData("watch", "관찰")]
    [InlineData("VERIFY", "추가 확인")]
    [InlineData("HOLD", "보류")]
    [InlineData("추가 확인", "추가 확인")]
    public void Verdict_codes_are_converted_to_korean(string verdict, string expectedKorean)
    {
        Assert.Equal(expectedKorean, VerdictCodes.ToKorean(verdict));
    }

    [Fact]
    public void Korean_verdict_is_converted_to_code_and_unknown_value_is_kept()
    {
        Assert.Equal("VERIFY", VerdictCodes.ToCode("추가 확인"));
        Assert.Equal("NORMAL", VerdictCodes.ToCode("정상"));
        Assert.Equal("알 수 없음", VerdictCodes.ToCode("알 수 없음"));
    }

    [Theory]
    [InlineData("정상", VerdictCodes.PassThroughGroup)]
    [InlineData("WATCH", VerdictCodes.PassThroughGroup)]
    [InlineData("추가 확인", VerdictCodes.CheckGroup)]
    [InlineData("HOLD", VerdictCodes.CheckGroup)]
    [InlineData("VERY_DANGEROUS", null)]
    public void Verdicts_are_grouped_for_detection_rates(string verdict, string? expectedGroup)
    {
        Assert.Equal(expectedGroup, VerdictCodes.ToGroup(verdict));
    }

    [Fact]
    public void Qa_event_follows_contract_format()
    {
        var pipelineEvent = new PipelineEvent(
            RunId: "run-1",
            CaseId: "DUP-001",
            EventId: "run-1-dup-001-e1",
            SessionHash: "run-1-dup-001-s1",
            EventTime: new DateTimeOffset(2026, 11, 2, 18, 0, 10, TimeSpan.FromHours(9)),
            EventType: "seat_hold");

        using var contractEvent = JsonDocument.Parse(ContractEventFormat.ToJson(pipelineEvent));
        var root = contractEvent.RootElement;

        // docs/contract.md 4-1장의 필드
        Assert.Equal("run-1-dup-001-e1", root.GetProperty("eventId").GetString());
        Assert.Equal("run-1-dup-001-s1", root.GetProperty("sessionHash").GetString());
        Assert.Equal("seat_hold", root.GetProperty("type").GetString());
        Assert.Equal("server", root.GetProperty("origin").GetString());
        Assert.Equal("seat", root.GetProperty("screen").GetString());
        Assert.Equal("qa-device-DUP-001", root.GetProperty("deviceHash").GetString());
        Assert.Equal("qa-show", root.GetProperty("showId").GetString());
        Assert.Equal("2026-11-02T09:00:10.000Z", root.GetProperty("occurredAt").GetString());
        Assert.Equal("qa", root.GetProperty("source").GetString());
        Assert.Equal("run-1", root.GetProperty("runId").GetString());
        Assert.Equal("DUP-001", root.GetProperty("caseId").GetString());
    }

    [Fact]
    public void Reads_pipeline_connection_options_with_contract_defaults()
    {
        var options = CommandLineOptions.Parse(
            ["--suite", "pipeline", "--kafka", "localhost:9092", "--mongo", "mongodb://localhost:27017"]);

        var connection = Assert.IsType<PipelineConnectionSettings>(options.PipelineConnection);
        Assert.Equal("localhost:9092", connection.KafkaBootstrapServers);
        Assert.Equal("mongodb://localhost:27017", connection.MongoConnectionString);
        Assert.Equal("ticket-events", connection.KafkaTopic);
        Assert.Equal("ticketguard", connection.MongoDatabase);
        Assert.Equal("spark", connection.SparkContainerName);
    }

    [Fact]
    public void Uses_fake_pipeline_when_no_connection_options_are_given()
    {
        Assert.Null(CommandLineOptions.Parse(["--suite", "pipeline"]).PipelineConnection);
    }

    [Theory]
    [InlineData("--kafka", "localhost:9092")]
    [InlineData("--mongo", "mongodb://localhost:27017")]
    public void Rejects_only_one_of_kafka_and_mongo(string optionName, string optionValue)
    {
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["--suite", "pipeline", optionName, optionValue]));
    }

    [Fact]
    public void Prepares_real_pipeline_target_when_connection_is_given()
    {
        // 준비 단계에서는 실제로 연결하지 않으므로 서버 없이도 확인할 수 있다
        var options = new CommandLineOptions(
            TestSuite.Recovery, CommandLineOptions.NoCasesPath, "reports", ApiBaseUrl: null,
            PipelineConnection: new PipelineConnectionSettings("localhost:9092", "mongodb://localhost:27017"));

        var preparedRun = QaRunService.Prepare(options);
        preparedRun.ResourceToDispose?.Dispose();

        Assert.Equal(QaRunService.RealPipelineTargetName, preparedRun.TargetName);
        Assert.NotNull(preparedRun.ResourceToDispose);
    }

    [Fact]
    public async Task Docker_control_reports_failure_for_missing_container_or_docker()
    {
        // docker가 없는 PC든, 컨테이너가 없는 PC든 ProcessingControlException으로 알려야 한다
        var dockerControl = new DockerProcessingControl($"qa-test-missing-{Guid.NewGuid():N}");

        await Assert.ThrowsAsync<ProcessingControlException>(() => dockerControl.StopProcessingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Mongo_reader_reports_read_failure_when_mongodb_is_unreachable()
    {
        // 아무것도 듣고 있지 않은 포트. 연결 시도는 5초 안에 포기한다
        var mongoReader = new MongoPipelineReader("mongodb://127.0.0.1:1", "ticketguard");

        await Assert.ThrowsAsync<PipelineReadException>(
            () => mongoReader.ReadCaseCountsAsync("run-1", "DUP-001", CancellationToken.None));
    }
}
