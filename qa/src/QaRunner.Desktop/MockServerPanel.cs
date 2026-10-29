using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QaRunner.AppMock;
using QaRunner.Models;

namespace QaRunner.Desktop;

/// <summary>
/// 홈 › 앱 영역의 가짜 판정 서버 화면. 서버를 켜고 끄고, 앱이 보낸 요청을 받는 즉시 목록에 보여 준다.
/// 끄면 그때까지 받은 요청을 계약과 비교한 결과를 다른 시험처럼 보고서로 저장하고 결과 화면에 띄운다.
/// </summary>
public partial class MainWindow
{
    // 켜 둔 가짜 판정 서버와, 서버가 꺼지면 채점 결과를 돌려주는 실행. 꺼져 있으면 둘 다 null
    private MockVerdictServer? _mockServer;
    private Task<TestRun>? _mockRunTask;
    private string _mockReportDirectory = CommandLineOptions.DefaultReportDirectory;

    // 받은 요청 목록 (최근 요청이 위)
    private readonly ObservableCollection<MockRequestRow> _mockRequestRows = [];

    /// <summary>가짜 판정 서버가 켜져 있는지</summary>
    public bool IsMockServerRunning => _mockServer is not null;

    /// <summary>응답 방식 목록을 채우고 꺼진 상태로 보여 준다. 창을 닫으면 켜 둔 서버도 끈다</summary>
    private void InitializeMockServerPanel()
    {
        MockScenarioBox.ItemsSource = Enum.GetValues<MockScenario>().Select(scenario => new MockScenarioItem(scenario)).ToArray();
        MockScenarioBox.SelectedIndex = 0;
        MockRequestsGrid.ItemsSource = _mockRequestRows;
        Closed += (_, _) => _mockServer?.Stop();
        ShowMockServerState();
    }

    private MockScenario SelectedMockScenario =>
        MockScenarioBox.SelectedItem is MockScenarioItem item ? item.Scenario : MockScenario.Auto;

    // 응답 방식을 바꾸면 앱에서 확인할 것을 바꾸고, 켜 둔 서버에도 바로 적용한다
    private void MockScenario_Changed(object sender, SelectionChangedEventArgs e)
    {
        var scenario = SelectedMockScenario;
        MockExpectationText.Text = scenario.DescribeAppExpectation();
        MockResponseText.Text = $"서버가 하는 일: {scenario.DescribeResponse()}";
        if (_mockServer is null) return;
        _mockServer.Scenario = scenario;
        ShowStatus($"가짜 판정 서버의 응답 방식을 ‘{scenario.ToKoreanName()}’으로 바꿨어요. 앱에서 다시 요청해 보세요.", StatusTone.Running);
    }

    /// <summary>
    /// 화면의 포트·응답 방식으로 가짜 판정 서버를 켠다. 포트가 막혀 있으면 카드 안에 이유를 보여 준다.
    /// </summary>
    public void StartMockServer()
    {
        if (_mockServer is not null) return;
        MockError.IsOpen = false;
        try
        {
            var portText = MockPortBox.Text.Trim();
            if (!int.TryParse(portText, out var port) || port is < 1 or > 65535)
                throw new ArgumentException("포트 칸에 1~65535 사이 숫자를 넣어 주세요. 보통 8000을 써요.");

            var settings = new MockServerSettings(port, SelectedMockScenario, MockAllowLanBox.IsChecked == true, DurationSeconds: null);
            var server = MockVerdictServer.Start(settings);
            _mockServer = server;
            _mockRequestRows.Clear();
            server.RequestReceived += record => Dispatcher.BeginInvoke(() => AddMockRequest(record));

            var reportDirectory = ReportDirectoryBox.Text.Trim();
            _mockReportDirectory = reportDirectory.Length > 0 ? reportDirectory : CommandLineOptions.DefaultReportDirectory;
            _mockRunTask = QaRunService.RunAsync(QaRunService.CreateMockApiRun(server, duration: null), _mockReportDirectory);

            ShowMockServerState();
            CompleteStep(ChecklistStepKind.PickTest);
            ShowStatus($"가짜 판정 서버가 켜져 있어요 · {server.Addresses[0].Url}. 앱의 API 주소를 바꾸고 판정을 요청해 보세요.", StatusTone.Running);
        }
        catch (Exception exception) when (QaRunService.IsPreparationError(exception))
        {
            MockError.Title = "서버를 켜지 못했어요";
            MockError.Message = exception.Message;
            MockError.IsOpen = true;
        }
    }

    /// <summary>
    /// 가짜 판정 서버를 끄고, 받은 요청이 있으면 보고서를 저장해 결과 화면에 보여 준다.
    /// </summary>
    public async Task StopMockServerAsync()
    {
        if (_mockServer is not { } server || _mockRunTask is not { } runTask) return;
        server.Stop();
        var testRun = await runTask;
        _mockServer = null;
        _mockRunTask = null;
        ShowMockServerState();

        if (server.Records.Count == 0)
        {
            ShowStatus("서버를 껐어요. 받은 요청이 없어서 보고서는 만들지 않았어요. 앱의 API 주소와 포트를 확인해 주세요.", StatusTone.Notice);
            return;
        }

        var savedReports = QaRunService.SaveReports(testRun, _mockReportDirectory);
        SelectPage("run");
        ExecutionScroll.ScrollToTop();
        HideCoach();
        ShowTestRun(testRun);
        _lastHtmlReportPath = Path.GetFullPath(savedReports.HtmlReportPath);
        OpenDashboardButton.IsEnabled = true;
        RefreshHistory();
        CompleteStep(ChecklistStepKind.RunTest);
        ShowCompletion(testRun);
    }

    private void MockStart_Click(object sender, RoutedEventArgs e) => StartMockServer();

    // 결과 화면의 다시 실행: 가짜 판정 서버 결과는 앱이 요청을 보내야 하므로 홈 › 앱 영역으로 돌아가고, 나머지는 같은 조건으로 바로 다시 돌린다
    private async void RerunDisplayed_Click(object sender, RoutedEventArgs e)
    {
        if (_displayedRun?.SuiteName != TestSuite.MockApi.ToKoreanName())
        {
            await RunSelectedSuiteAsync();
            return;
        }

        SelectPage("home");
        SelectHomeArea(TestArea.App);
        ShowStatus("‘서버 켜기’를 누른 뒤 앱에서 다시 판정을 요청해 주세요.", StatusTone.Running);
    }

    private async void MockStop_Click(object sender, RoutedEventArgs e) => await StopMockServerAsync();

    // 받은 요청을 목록 맨 위에 넣고, 상태 표시줄에 마지막 요청을 알린다
    private void AddMockRequest(MockRequestRecord record)
    {
        if (_mockServer is null) return;
        _mockRequestRows.Insert(0, new MockRequestRow(record));
        ShowMockServerState();
        ShowStatus(record.MatchesContract
                ? $"{record.Number}번째 요청을 받았어요 · {record.ResponseSummary}"
                : $"{record.Number}번째 요청이 계약과 달라요 · {string.Join(" · ", record.Problems)}",
            record.MatchesContract ? StatusTone.Running : StatusTone.Notice);
    }

    // 목록에서 요청을 고르면 계약과 다른 점을 상태 표시줄에 자세히 보여 준다
    private void MockRequest_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (MockRequestsGrid.SelectedItem is not MockRequestRow row) return;
        ShowStatus($"{row.Record.Number}번째 요청 · {row.ResponseText} · {row.ProblemText}",
            row.MatchesContract ? StatusTone.Pass : StatusTone.Notice);
    }

    private void CopyMockAddress_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(MockAddressText.Text);
            ShowStatus("앱에 넣을 주소를 복사했어요. 에뮬레이터는 10.0.2.2 주소를 써 주세요.", StatusTone.Running);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            ShowStatus("클립보드를 다른 프로그램이 쓰고 있어서 복사하지 못했어요. 주소를 직접 선택해 복사해 주세요.", true);
        }
    }

    // 켜짐·꺼짐에 맞춰 표시·버튼·주소·건수를 바꾼다
    private void ShowMockServerState()
    {
        var running = _mockServer is not null;
        MockStateText.Text = running ? $"켜져 있어요 · {_mockServer!.Port}" : "꺼져 있어요";
        MockStateText.Foreground = (Brush)FindResource(running ? "PassBrush" : "MutedBrush");
        MockStateBadge.Background = (Brush)FindResource(running ? "PassBackgroundBrush" : "HoverBrush");
        MockStartButton.IsEnabled = !running;
        MockStopButton.IsEnabled = running;
        MockPortBox.IsEnabled = MockAllowLanBox.IsEnabled = !running;
        MockAddressPanel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        if (running)
            MockAddressText.Text = string.Join(Environment.NewLine, _mockServer!.Addresses.Select(address => $"{address.Url}   ({address.Label})"));

        var problemCount = _mockRequestRows.Count(row => !row.MatchesContract);
        MockRequestCountText.Text = _mockRequestRows.Count == 0 ? ""
            : $"{_mockRequestRows.Count}건{(problemCount > 0 ? $" · 계약과 다른 요청 {problemCount}건" : "")}";
        MockEmptyText.Visibility = _mockRequestRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>응답 방식 목록의 한 줄. 예: "서버 오류 500   #500"</summary>
    private sealed record MockScenarioItem(MockScenario Scenario)
    {
        public string Label => $"{Scenario.ToKoreanName()}   {Scenario.ToMessageTag()}";
    }

    /// <summary>받은 요청 목록의 한 줄</summary>
    private sealed record MockRequestRow(MockRequestRecord Record)
    {
        public string TimeText => Record.ReceivedAt.ToLocalTime().ToString("HH:mm:ss");
        public string MessageText => Record.MessagePreview.Length > 0 ? Record.MessagePreview : $"(문자 없음) {Record.Method} {Record.Path}";
        public string ResponseText => Record.ResponseSummary;
        public bool MatchesContract => Record.MatchesContract;
        public string FormatText => MatchesContract ? MockRequestGrader.MatchesContractValue : $"다른 점 {Record.Problems.Count}개";
        public string ProblemText => MatchesContract ? "계약 3장과 같은 요청이에요." : string.Join(" · ", Record.Problems);
    }
}
