using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace QaRunner.AppMock;

/// <summary>
/// B의 판정 API 대신 앱(A)이 붙어 볼 수 있는 가짜 판정 서버.
/// POST /checks에 정해 둔 응답(정상 판정, 오류, 지연, 깨진 응답)을 돌려주고, 받은 요청이 계약 3장과 같은지 기록한다.
///
/// 별도 웹 서버 런타임 없이 돌도록 TcpListener로 HTTP/1.1의 필요한 부분만 처리한다.
/// (HttpListener는 Host 헤더가 localhost가 아니면 거절해서 Android 에뮬레이터의 10.0.2.2 요청을 받지 못한다)
/// 응답마다 연결을 닫고, 웹 브라우저로 띄운 앱(Expo 웹)도 부를 수 있게 CORS 헤더를 붙인다.
/// </summary>
public sealed class MockVerdictServer : IDisposable
{
    /// <summary>보고서에 남기는 시험 대상 이름. 시험하는 것은 앱이고, 서버는 앱이 붙는 상대다</summary>
    public const string TargetName = "앱 (가짜 판정 서버에 연결)";

    // 요청 헤더·본문 크기 제한. 판정 요청은 문자 한 통이라 이보다 훨씬 작다
    private const int MaxHeaderBytes = 32 * 1024;
    private const int MaxBodyBytes = 1024 * 1024;

    // 요청을 다 받을 때까지 기다리는 시간. 연결만 열고 아무것도 보내지 않는 경우를 정리한다
    private static readonly TimeSpan RequestReadTimeout = TimeSpan.FromSeconds(10);

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<MockRequestRecord> _records = [];
    private readonly Lock _recordsLock = new();
    private int _scenario;

    private MockVerdictServer(TcpListener listener, MockServerSettings settings)
    {
        _listener = listener;
        _scenario = (int)settings.Scenario;
        AllowsLan = settings.AllowLan;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>실제로 연 포트 (설정에서 0을 줬으면 운영체제가 고른 포트)</summary>
    public int Port { get; }

    /// <summary>같은 Wi-Fi의 다른 기기에서도 접속할 수 있게 열었는지</summary>
    public bool AllowsLan { get; }

    /// <summary>지금 서버 전체에 고른 응답 방식. 켜 둔 채로 바꿀 수 있다</summary>
    public MockScenario Scenario
    {
        get => (MockScenario)Volatile.Read(ref _scenario);
        set => Volatile.Write(ref _scenario, (int)value);
    }

    /// <summary>
    /// 판정 요청을 받아 응답을 정하면 바로 알린다 (느린 응답은 기다리기 전에 알린다).
    /// 서버의 작업 스레드에서 불리므로 화면에서는 Dispatcher로 넘겨 써야 한다.
    /// </summary>
    public event Action<MockRequestRecord>? RequestReceived;

    /// <summary>지금까지 받은 판정 요청 기록 (받은 순서)</summary>
    public IReadOnlyList<MockRequestRecord> Records
    {
        get
        {
            lock (_recordsLock)
                return _records.ToArray();
        }
    }

    /// <summary>서버가 꺼졌는지</summary>
    public bool IsStopped => _stopped.Task.IsCompleted;

    /// <summary>
    /// 서버를 켠다. 포트를 이미 다른 프로그램이 쓰고 있으면 MockServerStartException을 던진다.
    /// </summary>
    /// <param name="settings">포트, 처음 응답 방식, 같은 Wi-Fi 허용 여부</param>
    public static MockVerdictServer Start(MockServerSettings settings)
    {
        settings.Validate();

        // Windows는 다른 프로그램이 0.0.0.0(모든 주소)으로 연 포트를 127.0.0.1로 또 열 수 있게 둔다.
        // 그러면 uvicorn --host 0.0.0.0으로 켠 판정 API와 이 서버가 함께 켜져서, 앱 요청이 어느 쪽으로 갈지 헷갈린다.
        // 그래서 켜기 전에 모든 주소로 잠깐 열어 보고, 누군가 이미 쓰고 있으면 켜지 않는다
        if (settings.Port != 0 && !settings.AllowLan && !IsPortFreeOnAllAddresses(settings.Port))
            throw PortInUse(settings.Port, SocketError.AddressAlreadyInUse);

        var listener = new TcpListener(settings.AllowLan ? IPAddress.Any : IPAddress.Loopback, settings.Port);
        // Windows에서는 같은 포트를 다른 프로그램이 함께 열어 요청을 가로채지 못하게 한다 (맥·리눅스는 원래 막혀 있다)
        if (OperatingSystem.IsWindows())
            listener.ExclusiveAddressUse = true;

        try
        {
            listener.Start();
        }
        catch (SocketException exception)
        {
            throw PortInUse(settings.Port, exception.SocketErrorCode);
        }

        var server = new MockVerdictServer(listener, settings);
        if (!settings.AllowLan)
            server._allAddressGuard = ReserveAllAddresses(server.Port);
        _ = server.AcceptLoopAsync();
        return server;
    }

    // 이 서버가 켜져 있는 동안 다른 프로그램이 같은 포트를 0.0.0.0으로 열지 못하게 잡아 두는 소켓.
    // 연결을 받지는 않는다(Listen하지 않음). 같은 Wi-Fi 허용이면 이미 모든 주소로 열었으므로 필요 없다
    private Socket? _allAddressGuard;

    private static MockServerStartException PortInUse(int port, SocketError socketError) => new(
        $"{port}번 포트를 열지 못했어요. 판정 API 같은 다른 프로그램이 이미 쓰고 있을 수 있어요. 다른 포트를 골라 주세요. ({socketError})");

    // 모든 주소(0.0.0.0)로 이 포트를 혼자 열 수 있는지. 127.0.0.1이든 0.0.0.0이든 누가 쓰고 있으면 false
    private static bool IsPortFreeOnAllAddresses(int port)
    {
        using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        if (OperatingSystem.IsWindows())
            probe.ExclusiveAddressUse = true;
        try
        {
            probe.Bind(new IPEndPoint(IPAddress.Any, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    // 0.0.0.0의 같은 포트를 묶어만 둔다. 운영체제가 허락하지 않으면(맥·리눅스) 원래 막혀 있는 것이라 그냥 넘어간다
    private static Socket? ReserveAllAddresses(int port)
    {
        var guard = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            guard.Bind(new IPEndPoint(IPAddress.Any, port));
            return guard;
        }
        catch (SocketException)
        {
            guard.Dispose();
            return null;
        }
    }

    /// <summary>
    /// 앱에 넣을 서버 주소들. 이 PC, Android 에뮬레이터, (허용했으면) 같은 Wi-Fi 휴대폰 순서다.
    /// </summary>
    public IReadOnlyList<MockServerAddress> Addresses
    {
        get
        {
            var addresses = new List<MockServerAddress>
            {
                new("이 PC (웹·iOS 시뮬레이터)", $"http://127.0.0.1:{Port}"),
                new("Android 에뮬레이터", $"http://10.0.2.2:{Port}"),
            };
            if (AllowsLan)
                addresses.AddRange(FindLanAddresses().Select(address => new MockServerAddress("같은 Wi-Fi 휴대폰", $"http://{address}:{Port}")));
            return addresses;
        }
    }

    /// <summary>
    /// 서버가 꺼질 때까지 기다린다. 시간을 주면 그 시간이 지났을 때 스스로 끈다.
    /// </summary>
    /// <param name="duration">켜 둘 시간. null이면 Stop을 부를 때까지</param>
    public async Task WaitUntilStoppedAsync(TimeSpan? duration)
    {
        if (duration is { } runTime)
        {
            await Task.WhenAny(_stopped.Task, Task.Delay(runTime));
            Stop();
        }

        await _stopped.Task;
    }

    /// <summary>서버를 끈다. 기다리던 느린 응답도 함께 멈춘다. 여러 번 불러도 된다</summary>
    public void Stop()
    {
        if (_stopping.IsCancellationRequested)
            return;

        _stopping.Cancel();
        _listener.Stop();
        _allAddressGuard?.Dispose();
        _stopped.TrySetResult();
    }

    public void Dispose() => Stop();

    // 연결을 받을 때마다 따로 처리한다. 느린 응답을 기다리는 동안에도 다른 요청을 받을 수 있다
    private async Task AcceptLoopAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stopping.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                if (_stopping.IsCancellationRequested)
                    break;
                continue;
            }

            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
                readTimeout.CancelAfter(RequestReadTimeout);

                var request = await ReadRequestAsync(stream, readTimeout.Token);
                if (request is null)
                {
                    await WriteResponseAsync(stream, MockVerdictResponder.ContractErrorResponse(413, "요청이 너무 커요."), _stopping.Token);
                    return;
                }

                var response = DecideResponse(request);
                if (response.Delay > TimeSpan.Zero)
                    await Task.Delay(response.Delay, _stopping.Token);
                await WriteResponseAsync(stream, response, _stopping.Token);
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // 앱이 기다리다 연결을 끊었거나 서버를 껐다. 이 연결만 정리하면 된다
            }
        }
    }

    // 브라우저의 사전 확인(OPTIONS)과 주소 확인용 GET /은 기록하지 않고 바로 답한다. 나머지는 모두 판정 요청으로 기록한다
    private MockResponse DecideResponse(MockHttpRequest request)
    {
        if (request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            return new MockResponse(204, "", TimeSpan.Zero, Scenario, null);

        if (request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) && request.Path is "/" or "/favicon.ico")
        {
            return request.Path == "/"
                ? new MockResponse(200, $"{{\"server\": \"정보지킴 QA 가짜 판정 서버\", \"scenario\": \"{Scenario.ToOptionValue()}\", \"checkPath\": \"{MockRequestInspector.CheckPath}\"}}",
                    TimeSpan.Zero, Scenario, null)
                : new MockResponse(404, "", TimeSpan.Zero, Scenario, null);
        }

        var inspection = MockRequestInspector.Inspect(request);
        var scenario = MockVerdictResponder.ChooseScenario(inspection.Message, Scenario);
        var response = inspection.ErrorStatusCode is { } errorStatusCode
            ? MockVerdictResponder.ContractErrorResponse(errorStatusCode, "계약 3장과 다른 요청이에요: " + string.Join(" · ", inspection.Problems)) with { Scenario = scenario }
            : MockVerdictResponder.Respond(scenario, inspection.Message!, DateTimeOffset.Now);

        MockRequestRecord record;
        lock (_recordsLock)
        {
            record = new MockRequestRecord(
                Number: _records.Count + 1,
                ReceivedAt: DateTimeOffset.Now,
                Method: request.Method,
                Path: request.Path,
                MessagePreview: MockRequestRecord.ToPreview(inspection.Message),
                Scenario: scenario,
                StatusCode: response.StatusCode,
                VerdictCode: response.VerdictCode,
                Problems: inspection.Problems);
            _records.Add(record);
        }

        RequestReceived?.Invoke(record);
        return response;
    }

    /// <summary>
    /// 요청 줄·헤더·본문을 읽는다. 헤더나 본문이 제한보다 크면 null.
    /// 본문은 Content-Length만큼 읽거나, 길이를 미리 알리지 않는 chunked 방식이면 조각을 이어 붙인다.
    /// (.NET HttpClient의 PostAsJsonAsync처럼 chunked로 보내는 클라이언트가 많다)
    /// </summary>
    private static async Task<MockHttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var reader = new RequestByteReader(stream, cancellationToken);
        var headerEnd = await reader.FindAsync("\r\n\r\n"u8.ToArray(), from: 0, MaxHeaderBytes);
        if (headerEnd < 0)
            return null;

        var headerText = Encoding.UTF8.GetString(reader.Data, 0, headerEnd);
        var lines = headerText.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2)
            throw new IOException("HTTP 요청 줄을 읽지 못했어요.");

        var headers = lines.Skip(1)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0].Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last()[1].Trim(), StringComparer.OrdinalIgnoreCase);

        // curl처럼 큰 본문 전에 허락을 묻는 클라이언트에는 계속 보내라고 답한다
        if (headers.TryGetValue("Expect", out var expect) && expect.Equals("100-continue", StringComparison.OrdinalIgnoreCase))
            await stream.WriteAsync("HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray(), cancellationToken);

        var bodyStart = headerEnd + 4;
        var isChunked = headers.TryGetValue("Transfer-Encoding", out var transferEncoding)
            && transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase);
        var bodyBytes = isChunked
            ? await ReadChunkedBodyAsync(reader, bodyStart)
            : await ReadFixedLengthBodyAsync(reader, bodyStart, headers);
        if (bodyBytes is null)
            return null;

        var path = requestLine[1].Split('?', 2)[0];
        return new MockHttpRequest(requestLine[0], path, headers.GetValueOrDefault("Content-Type"), Encoding.UTF8.GetString(bodyBytes));
    }

    // Content-Length만큼 읽는다. 헤더가 없으면 본문 없음, 너무 크면 null
    private static async Task<byte[]?> ReadFixedLengthBodyAsync(RequestByteReader reader, int bodyStart, Dictionary<string, string> headers)
    {
        var contentLength = headers.TryGetValue("Content-Length", out var lengthText) && int.TryParse(lengthText, out var length) ? length : 0;
        if (contentLength is < 0 or > MaxBodyBytes)
            return null;

        await reader.EnsureAsync(bodyStart + contentLength);
        return reader.Data.AsSpan(bodyStart, contentLength).ToArray();
    }

    // "크기(16진수)\r\n내용\r\n" 조각을 크기 0이 나올 때까지 이어 붙인다. 합친 크기가 제한을 넘으면 null
    private static async Task<byte[]?> ReadChunkedBodyAsync(RequestByteReader reader, int bodyStart)
    {
        using var body = new MemoryStream();
        var position = bodyStart;
        while (true)
        {
            var sizeLineEnd = await reader.FindAsync("\r\n"u8.ToArray(), position, position + 1024);
            if (sizeLineEnd < 0)
                return null;

            var sizeText = Encoding.ASCII.GetString(reader.Data, position, sizeLineEnd - position).Split(';')[0].Trim();
            if (!int.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber, null, out var chunkSize) || chunkSize < 0)
                throw new IOException($"chunked 본문의 조각 크기를 읽지 못했어요: {sizeText}");

            position = sizeLineEnd + 2;
            if (chunkSize == 0)
                return body.ToArray();
            if (body.Length + chunkSize > MaxBodyBytes)
                return null;

            await reader.EnsureAsync(position + chunkSize + 2);
            body.Write(reader.Data, position, chunkSize);
            position += chunkSize + 2;
        }
    }

    /// <summary>
    /// 연결에서 받은 바이트를 쌓아 두고, 필요한 만큼 더 읽어 오는 도우미.
    /// </summary>
    private sealed class RequestByteReader(NetworkStream stream, CancellationToken cancellationToken)
    {
        private readonly byte[] _readBuffer = new byte[8 * 1024];
        private readonly MemoryStream _received = new();

        /// <summary>지금까지 받은 바이트 (앞부분만 유효하고 길이는 Length)</summary>
        public byte[] Data => _received.GetBuffer();

        /// <summary>받은 바이트 수</summary>
        public int Length => (int)_received.Length;

        /// <summary>적어도 length 바이트를 받을 때까지 읽는다</summary>
        public async Task EnsureAsync(int length)
        {
            while (Length < length)
                await ReadMoreAsync();
        }

        /// <summary>from 위치부터 pattern을 찾는다. limit 바이트를 넘게 받아도 없으면 -1</summary>
        public async Task<int> FindAsync(byte[] pattern, int from, int limit)
        {
            while (true)
            {
                var index = Data.AsSpan(from, Math.Max(0, Length - from)).IndexOf(pattern);
                if (index >= 0)
                    return from + index;
                if (Length > limit)
                    return -1;
                await ReadMoreAsync();
            }
        }

        private async Task ReadMoreAsync()
        {
            var readCount = await stream.ReadAsync(_readBuffer, cancellationToken);
            if (readCount == 0)
                throw new IOException("요청을 다 받기 전에 연결이 끊겼어요.");
            _received.Write(_readBuffer, 0, readCount);
        }
    }

    private static async Task WriteResponseAsync(NetworkStream stream, MockResponse response, CancellationToken cancellationToken)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(response.Body);
        var header = new StringBuilder()
            .Append($"HTTP/1.1 {response.StatusCode} {ReasonPhrase(response.StatusCode)}\r\n")
            .Append("Content-Type: application/json; charset=utf-8\r\n")
            .Append($"Content-Length: {bodyBytes.Length}\r\n")
            .Append("Access-Control-Allow-Origin: *\r\n")
            .Append("Access-Control-Allow-Methods: POST, OPTIONS\r\n")
            .Append("Access-Control-Allow-Headers: Content-Type\r\n")
            .Append("Connection: close\r\n\r\n")
            .ToString();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancellationToken);
        await stream.WriteAsync(bodyBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static string ReasonPhrase(int statusCode) => statusCode switch
    {
        200 => "OK",
        204 => "No Content",
        400 => "Bad Request",
        404 => "Not Found",
        405 => "Method Not Allowed",
        413 => "Payload Too Large",
        500 => "Internal Server Error",
        503 => "Service Unavailable",
        _ => "Status",
    };

    // 켜져 있는 네트워크(Wi-Fi·유선)의 IPv4 주소. 가상 어댑터도 섞일 수 있어서 화면에는 모두 보여 준다
    private static IEnumerable<string> FindLanAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up
                    && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
                .Where(unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(unicast => unicast.Address.ToString())
                .Distinct()
                .ToArray();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }
}

/// <summary>
/// 앱에 넣을 서버 주소 하나.
/// </summary>
/// <param name="Label">어디서 쓰는 주소인지. 예: Android 에뮬레이터</param>
/// <param name="Url">주소. 예: http://10.0.2.2:8000</param>
public sealed record MockServerAddress(string Label, string Url);

/// <summary>
/// 가짜 판정 서버를 켜지 못했을 때 쓰는 예외 (포트 사용 중 등). 실행 준비 오류로 분류되도록 IOException을 잇는다.
/// </summary>
public sealed class MockServerStartException(string message) : IOException(message);
