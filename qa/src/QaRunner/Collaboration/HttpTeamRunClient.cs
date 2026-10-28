using System.Net;
using System.Net.Http.Json;
using QaRunner.Contracts;

namespace QaRunner.Collaboration;

/// <summary>공유 API 계약을 로컬 서버로 확인하는 클라이언트. EC2 연결은 인증·HTTPS 구현 후 확장한다.</summary>
public sealed class HttpTeamRunClient : ITeamRunClient, IDisposable
{
    private readonly HttpClient _httpClient;

    public HttpTeamRunClient(Uri baseAddress)
    {
        if (!baseAddress.IsAbsoluteUri || !baseAddress.IsLoopback
            || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("스켈레톤의 공유 API는 localhost 주소만 지원합니다. EC2 연결은 후속 구현입니다.");

        // TODO: EC2 연결 시 HTTPS·인증·실행자 식별·요청 제한을 함께 구현한다.
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseAddress.AbsoluteUri.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(5),
        };
    }

    public async Task PublishAsync(SharedQaRun run, CancellationToken cancellationToken = default)
    {
        run.Validate();
        using var response = await _httpClient.PutAsJsonAsync($"qa/runs/{run.RunId:D}", run, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<SharedQaRun>> ListAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<List<SharedQaRun>>("qa/runs", cancellationToken) ?? [];

    public async Task<SharedQaRun?> FindAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"qa/runs/{runId:D}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SharedQaRun>(cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();
}
