using System.Net;
using System.Text;
using QaRunner.Verdicts;

namespace QaRunner.Tests;

/// <summary>
/// 실제 서버 없이, 미리 정한 응답을 돌려주는 가짜 HTTP 처리기로 ApiVerdictService를 확인한다.
/// </summary>
public class ApiVerdictServiceTests
{
    /// <summary>
    /// 어떤 요청이 왔는지 기록해 두고, 정해 둔 응답을 돌려주는 시험용 HTTP 처리기.
    /// </summary>
    private sealed class RecordingHttpHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static ApiVerdictService CreateService(RecordingHttpHandler httpHandler) =>
        new(new HttpClient(httpHandler) { BaseAddress = new Uri("http://qa-test.local/api/") });

    [Fact]
    public async Task Posts_message_to_checks_endpoint_and_returns_verdict()
    {
        var httpHandler = new RecordingHttpHandler(HttpStatusCode.OK, """{ "verdict": "고위험", "scanId": "s-1" }""");
        var verdictService = CreateService(httpHandler);

        var verdict = await verdictService.GetVerdictAsync("인증번호를 알려주세요");

        Assert.Equal("고위험", verdict);
        Assert.Equal(HttpMethod.Post, httpHandler.LastRequest!.Method);
        Assert.Equal("http://qa-test.local/api/checks", httpHandler.LastRequest.RequestUri!.ToString());
        Assert.Contains("\"message\"", httpHandler.LastRequestBody);
    }

    [Fact]
    public async Task Converts_contract_verdict_code_to_korean()
    {
        // 계약대로 영문 코드가 오면 입력값 파일의 정답(한글)과 비교할 수 있게 한글로 바꾼다
        var verdictService = CreateService(new RecordingHttpHandler(HttpStatusCode.OK, """{ "verdict": "UNDECIDED" }"""));

        Assert.Equal("판단 보류", await verdictService.GetVerdictAsync("본인 확인이 필요합니다"));
    }

    [Fact]
    public async Task Throws_when_api_returns_error_status()
    {
        var verdictService = CreateService(new RecordingHttpHandler(HttpStatusCode.InternalServerError, "{}"));

        await Assert.ThrowsAsync<HttpRequestException>(() => verdictService.GetVerdictAsync("문자"));
    }

    [Fact]
    public async Task Throws_when_response_has_no_verdict()
    {
        var verdictService = CreateService(new RecordingHttpHandler(HttpStatusCode.OK, """{ "result": "ok" }"""));

        await Assert.ThrowsAsync<VerdictServiceException>(() => verdictService.GetVerdictAsync("문자"));
    }

    [Theory]
    [InlineData("http://localhost:8000", "http://localhost:8000/")]
    [InlineData("http://localhost:8000/api", "http://localhost:8000/api/")]
    [InlineData("https://staging.example.test/", "https://staging.example.test/")]
    public void Adds_trailing_slash_to_api_address(string apiBaseUrl, string expectedBaseAddress)
    {
        using var httpClient = ApiVerdictService.CreateHttpClient(apiBaseUrl);

        Assert.Equal(expectedBaseAddress, httpClient.BaseAddress!.ToString());
    }

    [Theory]
    [InlineData("localhost:8000")]
    [InlineData("ftp://localhost")]
    public void Rejects_address_without_http_scheme(string apiBaseUrl)
    {
        Assert.Throws<ArgumentException>(() => ApiVerdictService.CreateHttpClient(apiBaseUrl));
    }
}
