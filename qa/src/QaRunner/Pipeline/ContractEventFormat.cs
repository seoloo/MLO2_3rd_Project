using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using QaRunner.Verdicts;

namespace QaRunner.Pipeline;

/// <summary>
/// QA 이벤트를 Kafka 메시지 본문(JSON)으로 바꾼다. 형식은 docs/contract.md 4장을 따른다.
/// 계약의 이벤트 필드가 바뀌면 이 파일만 고친다.
/// </summary>
public static class ContractEventFormat
{
    /// <summary>계약상 이벤트 종류. 지금은 검사 1건 = 이벤트 1건이라 하나뿐이다</summary>
    public const string CheckCompletedEventType = "check_completed";

    /// <summary>QA 러너가 넣은 시험 이벤트라는 표시. 파이프라인은 실제 이벤트와 똑같이 처리한다</summary>
    public const string QaSource = "qa";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // 사람이 Kafka 메시지를 열어 봤을 때 한글 등이 \uXXXX로 바뀌지 않게 한다
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 이벤트 하나를 계약 형식의 JSON 문자열로 만든다.
    /// </summary>
    public static string ToJson(PipelineEvent pipelineEvent)
    {
        var contractEvent = new Dictionary<string, object?>
        {
            ["eventId"] = pipelineEvent.EventId,
            ["scanId"] = pipelineEvent.ScanId,
            ["eventType"] = CheckCompletedEventType,
            ["verdict"] = VerdictCodes.ToCode(pipelineEvent.Verdict),
            ["occurredAt"] = pipelineEvent.EventTime.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            ["source"] = QaSource,
            ["runId"] = pipelineEvent.RunId,
            ["caseId"] = pipelineEvent.CaseId,
        };

        return JsonSerializer.Serialize(contractEvent, JsonOptions);
    }
}
