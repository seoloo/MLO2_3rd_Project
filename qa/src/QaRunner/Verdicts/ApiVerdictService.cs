using System.Net;
using System.Net.Http.Json;
using QaRunner.Models;
using QaRunner.Pipeline;

namespace QaRunner.Verdicts;

/// <summary>
/// 백엔드(B)의 FastAPI에 세션의 행동 이벤트를 보내고, 판정 조회 API에서 그 세션의 판정을 받아 오는 실제 판정 대상.
/// 형식은 docs/contract.md 4·5장을 따른다. 계약이 바뀌면 이 파일만 고친다.
///   1. POST {API 주소}/events            본문 { "events": [ ... ] }      → 202
///   2. GET  {API 주소}/verdicts/{세션 해시} → 404(판정 전)면 0.5초 뒤 다시, 200이면 { "verdict": "VERIFY", ... }
/// 앱 → Kafka → Spark 판정 → 분석 DB → 조회 API의 실제 경로를 그대로 지나가므로, 판정까지 걸린 시간도 함께 재진다.
/// </summary>
/// <param name="httpClient">API 주소(BaseAddress)가 설정된 HTTP 클라이언트</param>
/// <param name="verdictWaitLimit">판정을 기다리는 최대 시간. 비우면 계약의 10초 (단위 시험에서만 줄인다)</param>
/// <param name="pollInterval">판정을 다시 조회하는 간격. 비우면 0.5초</param>
public sealed class ApiVerdictService(HttpClient httpClient, TimeSpan? verdictWaitLimit = null, TimeSpan? pollInterval = null) : IVerdictService
{
    /// <summary>행동 이벤트를 보내는 수집 API 경로 (API 주소 뒤에 붙는다)</summary>
    public const string EventsPath = "events";

    /// <summary>판정 조회 API 경로. 뒤에 /{세션 해시}가 붙는다</summary>
    public const string VerdictsPath = "verdicts";

    /// <summary>요청 하나의 응답을 기다리는 시간</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>이벤트를 보낸 뒤 판정이 나오기를 기다리는 최대 시간. 넘으면 시스템 실패로 본다 (계약 5장)</summary>
    public static readonly TimeSpan VerdictWaitLimit = TimeSpan.FromSeconds(10);

    /// <summary>판정이 아직 없을 때 다시 조회하는 간격</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public async Task<string> GetVerdictAsync(SessionCase session, string runId, string sessionKey)
    {
        var sessionEvents = SessionEventBuilder.Build(
            session, runId, sessionKey, SessionEventBuilder.OpenTimeEndingNow(session, DateTimeOffset.UtcNow));

        var requestBody = new { events = sessionEvents.Events.Select(ContractEventFormat.ToContractObject) };
        using (var sendResponse = await httpClient.PostAsJsonAsync(EventsPath, requestBody))
        {
            // 4xx, 5xx 응답이면 HttpRequestException을 던진다 (채점기에서 시스템 실패로 기록)
            sendResponse.EnsureSuccessStatusCode();
        }

        return await WaitForVerdictAsync(sessionEvents.PrimarySessionHash);
    }

    // 판정 조회 API를 판정이 나올 때까지 0.5초마다 부른다. 404는 "아직 판정 전"이다
    private async Task<string> WaitForVerdictAsync(string sessionHash)
    {
        var waitLimit = verdictWaitLimit ?? VerdictWaitLimit;
        var interval = pollInterval ?? PollInterval;
        var deadline = DateTimeOffset.UtcNow + waitLimit;
        while (true)
        {
            using var response = await httpClient.GetAsync($"{VerdictsPath}/{Uri.EscapeDataString(sessionHash)}");
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                response.EnsureSuccessStatusCode();
                var verdictResponse = await response.Content.ReadFromJsonAsync<VerdictResponse>();
                if (string.IsNullOrWhiteSpace(verdictResponse?.Verdict))
                    throw new VerdictServiceException("판정 조회 응답에 verdict 값이 없습니다.");

                // API는 영문 코드(VERIFY)를 주고, 사례 파일의 정답은 한글(추가 확인)이라 한글로 바꿔 비교한다
                return VerdictCodes.ToKorean(verdictResponse.Verdict);
            }

            if (DateTimeOffset.UtcNow + interval > deadline)
                throw new VerdictServiceException($"이벤트를 보낸 뒤 {waitLimit.TotalSeconds:0.#}초 안에 판정이 나오지 않았습니다 (세션 {sessionHash}).");
            await Task.Delay(interval);
        }
    }

    /// <summary>
    /// API 주소로 HTTP 클라이언트를 만든다.
    /// 주소 끝에 /가 없으면 붙인다. 없으면 "http://host/api" + "events"가 "http://host/events"로 바뀌기 때문이다.
    /// </summary>
    /// <param name="apiBaseUrl">API 주소. 예: http://localhost:8000</param>
    public static HttpClient CreateHttpClient(string apiBaseUrl)
    {
        var baseUrlWithSlash = apiBaseUrl.EndsWith('/') ? apiBaseUrl : apiBaseUrl + "/";

        // "localhost:8000"처럼 http://를 빠뜨린 주소는 채점을 시작하기 전에 바로 알려 준다
        var isHttpUrl = Uri.TryCreate(baseUrlWithSlash, UriKind.Absolute, out var baseAddress)
            && (baseAddress.Scheme == Uri.UriSchemeHttp || baseAddress.Scheme == Uri.UriSchemeHttps);
        if (!isHttpUrl)
            throw new ArgumentException($"API 주소는 http:// 또는 https://로 시작해야 합니다: {apiBaseUrl}");

        return new HttpClient { BaseAddress = baseAddress, Timeout = RequestTimeout };
    }

    // 판정 조회 API가 돌려주는 응답 본문 중 판정 값만 읽는다
    private sealed record VerdictResponse(string? Verdict);
}

/// <summary>
/// 판정 대상이 정상적인 판정을 돌려주지 못했을 때 쓰는 예외 (응답 형식이 다르거나 값이 빠졌거나 시간 안에 안 나온 경우).
/// </summary>
public sealed class VerdictServiceException(string message) : Exception(message);
