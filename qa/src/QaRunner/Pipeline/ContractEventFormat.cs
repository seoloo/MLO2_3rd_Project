using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace QaRunner.Pipeline;

/// <summary>
/// 계약 형식의 행동 이벤트 한 건 (docs/contract.md 4-1장). 수집 API 본문과 Kafka 메시지 값이 같은 모양이다.
/// </summary>
/// <param name="EventId">이벤트 고유 번호</param>
/// <param name="Type">이벤트 종류 (4-2장)</param>
/// <param name="SessionHash">세션 해시 (Kafka 메시지 키)</param>
/// <param name="DeviceHash">기기 해시. 같은 기기의 세션 수를 셀 때 쓴다</param>
/// <param name="ShowId">공연 회차</param>
/// <param name="OccurredAt">이벤트가 일어난 시각</param>
/// <param name="RunId">QA 실행 번호</param>
/// <param name="CaseId">QA 사례 번호</param>
/// <param name="Screen">화면 이름. 비우면 종류에 맞는 기본 화면</param>
/// <param name="X">클릭 위치</param>
/// <param name="Y">클릭 위치</param>
public sealed record ContractEvent(
    string EventId,
    string Type,
    string SessionHash,
    string DeviceHash,
    string ShowId,
    DateTimeOffset OccurredAt,
    string RunId,
    string CaseId,
    string? Screen = null,
    int? X = null,
    int? Y = null);

/// <summary>
/// QA 이벤트를 계약 형식(JSON)으로 바꾼다. 계약의 이벤트 필드가 바뀌면 이 파일만 고친다.
/// </summary>
public static class ContractEventFormat
{
    /// <summary>QA 러너가 넣은 시험 이벤트라는 표시. 파이프라인은 실제 이벤트와 똑같이 처리한다</summary>
    public const string QaSource = "qa";

    /// <summary>QA 이벤트에 붙이는 공연 회차. 실제 공연과 섞이지 않게 qa로 시작한다</summary>
    public const string QaShowId = "qa-show";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // 사람이 Kafka 메시지를 열어 봤을 때 한글 등이 \uXXXX로 바뀌지 않게 한다
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>파이프라인 시험 이벤트를 계약 이벤트로 채운다. 기기는 시나리오마다 하나로 본다</summary>
    public static ContractEvent FromPipelineEvent(PipelineEvent pipelineEvent) => new(
        EventId: pipelineEvent.EventId,
        Type: pipelineEvent.EventType,
        SessionHash: pipelineEvent.SessionHash,
        DeviceHash: $"qa-device-{pipelineEvent.CaseId}",
        ShowId: QaShowId,
        OccurredAt: pipelineEvent.EventTime,
        RunId: pipelineEvent.RunId,
        CaseId: pipelineEvent.CaseId);

    /// <summary>이벤트 하나를 계약 필드 이름의 사전으로 만든다 (수집 API 본문의 events 배열 항목)</summary>
    public static Dictionary<string, object?> ToContractObject(ContractEvent contractEvent) => new()
    {
        ["eventId"] = contractEvent.EventId,
        ["type"] = contractEvent.Type,
        ["origin"] = EventTypes.OriginOf(contractEvent.Type),
        ["sessionHash"] = contractEvent.SessionHash,
        ["deviceHash"] = contractEvent.DeviceHash,
        ["showId"] = contractEvent.ShowId,
        ["screen"] = contractEvent.Screen ?? EventTypes.DefaultScreenOf(contractEvent.Type),
        ["x"] = contractEvent.X,
        ["y"] = contractEvent.Y,
        ["occurredAt"] = FormatTime(contractEvent.OccurredAt),
        ["source"] = QaSource,
        ["runId"] = contractEvent.RunId,
        ["caseId"] = contractEvent.CaseId,
    };

    /// <summary>이벤트 하나를 계약 형식의 JSON 문자열로 만든다 (Kafka 메시지 값)</summary>
    public static string ToJson(ContractEvent contractEvent) =>
        JsonSerializer.Serialize(ToContractObject(contractEvent), JsonOptions);

    /// <summary>파이프라인 시험 이벤트를 계약 형식의 JSON 문자열로 만든다</summary>
    public static string ToJson(PipelineEvent pipelineEvent) => ToJson(FromPipelineEvent(pipelineEvent));

    /// <summary>계약의 시각 형식: UTC, 밀리초까지. 예: 2026-11-20T11:02:01.204Z</summary>
    public static string FormatTime(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
