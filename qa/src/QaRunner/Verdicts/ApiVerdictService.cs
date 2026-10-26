using System.Net.Http.Json;

namespace QaRunner.Verdicts;

/// <summary>
/// B가 만드는 FastAPI에 문자를 보내 판정을 받아 오는 실제 판정 대상.
/// 요청·응답 형식은 B와 확정하기 전의 임시 약속이다. 바뀌면 이 파일만 고친다.
///   요청: POST {API 주소}/checks   본문 { "message": "문자 내용" }
///   응답: 200 OK                  본문 { "verdict": "고위험" } (다른 필드는 무시)
/// </summary>
/// <param name="httpClient">API 주소(BaseAddress)가 설정된 HTTP 클라이언트</param>
public sealed class ApiVerdictService(HttpClient httpClient) : IVerdictService
{
    /// <summary>판정을 요청할 API 경로 (API 주소 뒤에 붙는다)</summary>
    public const string CheckEndpointPath = "checks";

    /// <summary>응답이 이 시간 안에 오지 않으면 시스템 실패로 본다</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public async Task<string> GetVerdictAsync(string message)
    {
        var response = await httpClient.PostAsJsonAsync(CheckEndpointPath, new VerdictRequest(message));

        // 4xx, 5xx 응답이면 HttpRequestException을 던진다 (채점기에서 시스템 실패로 기록)
        response.EnsureSuccessStatusCode();

        var verdictResponse = await response.Content.ReadFromJsonAsync<VerdictResponse>();
        if (string.IsNullOrWhiteSpace(verdictResponse?.Verdict))
            throw new VerdictServiceException("API 응답에 verdict 값이 없습니다.");

        return verdictResponse.Verdict;
    }

    /// <summary>
    /// API 주소로 HTTP 클라이언트를 만든다.
    /// 주소 끝에 /가 없으면 붙인다. 없으면 "http://host/api" + "checks"가 "http://host/checks"로 바뀌기 때문이다.
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

    // API로 보내는 요청 본문. JSON 키는 camelCase(message)로 바뀐다
    private sealed record VerdictRequest(string Message);

    // API가 돌려주는 응답 본문 중 판정 값만 읽는다
    private sealed record VerdictResponse(string? Verdict);
}

/// <summary>
/// 판정 대상이 정상적인 판정을 돌려주지 못했을 때 쓰는 예외 (응답 형식이 다르거나 값이 빠진 경우).
/// </summary>
public sealed class VerdictServiceException(string message) : Exception(message);
