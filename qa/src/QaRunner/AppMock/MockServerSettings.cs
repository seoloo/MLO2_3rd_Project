namespace QaRunner.AppMock;

/// <summary>
/// 가짜 판정 서버를 켤 때의 조건 (--port, --scenario, --allow-lan, --duration).
/// </summary>
/// <param name="Port">열 포트. 0이면 비어 있는 포트를 운영체제가 골라 준다 (단위 시험용)</param>
/// <param name="Scenario">처음에 고를 응답 방식</param>
/// <param name="AllowLan">
/// 같은 Wi-Fi의 휴대폰에서도 접속할 수 있게 모든 네트워크에 열지.
/// false면 이 PC(127.0.0.1)에서만 열린다. Android 에뮬레이터는 10.0.2.2로 이 PC에 붙으므로 false여도 된다.
/// </param>
/// <param name="DurationSeconds">이 시간이 지나면 저절로 끈다. null이면 직접 끌 때까지 켜 둔다</param>
public sealed record MockServerSettings(int Port, MockScenario Scenario, bool AllowLan, int? DurationSeconds)
{
    /// <summary>기본 포트. FastAPI(uvicorn) 기본 포트와 같아서 앱 설정을 바꾸지 않고 판정 API 대신 쓸 수 있다</summary>
    public const int DefaultPort = 8000;

    /// <summary>기본 조건: 8000번 포트, 자동 판정, 이 PC에서만, 직접 끌 때까지</summary>
    public static MockServerSettings Default { get; } = new(DefaultPort, MockScenario.Auto, AllowLan: false, DurationSeconds: null);

    /// <summary>
    /// 값이 쓸 수 있는 범위인지 확인한다. 틀리면 ArgumentException을 던진다.
    /// </summary>
    public void Validate()
    {
        if (Port is < 0 or > 65535)
            throw new ArgumentException($"포트는 0~65535 사이여야 합니다: {Port}");
        if (DurationSeconds is <= 0)
            throw new ArgumentException($"--duration은 1초 이상이어야 합니다: {DurationSeconds}");
    }
}
