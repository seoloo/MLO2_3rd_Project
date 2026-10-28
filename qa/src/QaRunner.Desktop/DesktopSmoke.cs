using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Shell;
using System.Windows.Input;
using System.Windows.Automation;
using QaRunner.Models;

namespace QaRunner.Desktop;

public partial class MainWindow
{
    /// <summary>
    /// 실제 WPF 컨트롤로 탐색·검증·이력·공유를 점검하고 화면을 저장한다.
    /// 가짜 대상과 앱 내부 공유만 사용하며 사용자 보고서는 별도 폴더로 격리한다.
    /// </summary>
    public async Task RunUiSmokeAsync(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        ReportDirectoryBox.Text = Path.Combine(outputDirectory, "reports");
        var checks = new List<string>();
        void Require(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException($"UI 점검 실패: {name}");
            checks.Add(name);
        }
        async Task Capture(string name)
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            SaveScreenshot(Path.Combine(outputDirectory, $"{name}.png"));
        }

        // 서버에 연결하지 않고 배포본에 필요한 Kafka 네이티브 DLL이 로드되는지 확인한다.
        Confluent.Kafka.Library.Load();
        Require(Confluent.Kafka.Library.IsLoaded && Confluent.Kafka.Library.Version > 0,
            "Windows용 Kafka 네이티브 라이브러리 로드·버전 조회");

        await Capture("start");
        Require(((System.Windows.Media.SolidColorBrush)FindResource("AccentBrush")).Color == System.Windows.Media.Color.FromRgb(79, 70, 229)
            && ((System.Windows.Media.SolidColorBrush)FindResource("AccentBackgroundBrush")).Color == System.Windows.Media.Color.FromRgb(238, 242, 255),
            "밝은 Slate·Indigo 테마와 남보라 선택 강조 적용");
        Require(NavigationBar.TranslatePoint(new Point(), WorkspaceRoot).X < HomeQuestions.TranslatePoint(new Point(), WorkspaceRoot).X
            && HomeHero.IsVisible && HomeSuiteCards.Columns == 2, "좁은 탐색 레일·배너·2×2 시험 카드 배치");
        Require(OverviewPage.IsVisible && HomeVerdictChoice.IsChecked == true && ResultsSection.Visibility == Visibility.Collapsed,
            "시험 선택 화면에서 결과를 미리 채우지 않고 문자 시험을 기본 안내");
        HomeLoadChoice.IsChecked = true;
        PrepareHomeRunButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(RunNav.IsChecked == true && LoadSuiteRadio.IsChecked == true && MeasurementPanel.Visibility == Visibility.Visible
            && StopSecondsField.Visibility == Visibility.Collapsed, "부하 시험 선택에서 부하 조건으로 이동·불필요한 중단 입력 숨김");
        SelectPage("overview");
        HomeRecoveryChoice.IsChecked = true;
        PrepareHomeRunButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(RecoverySuiteRadio.IsChecked == true && CasesPathField.Visibility == Visibility.Collapsed
            && StopSecondsField.Visibility == Visibility.Visible && HomeApiTarget.Visibility == Visibility.Collapsed,
            "복구 시험 선택에서 복구 조건으로 이동·사용하지 않는 파일과 API 선택 숨김");
        SelectPage("overview");
        HomeVerdictChoice.IsChecked = true;
        HomeApiTarget.IsChecked = true;
        HomeApiBox.Text = "잘못된 주소";
        var historyBeforePreparation = ReportHistoryLoaderCount();
        PrepareHomeRunButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(OverviewPage.IsVisible && HomeErrorText.IsVisible && HomeErrorText.Text.Contains("http://"),
            "주소를 잘못 입력하면 시작 화면에서 수정 방법 안내");
        await Capture("api-address-error");
        HomeApiBox.Text = "http://localhost:8000";
        PrepareHomeRunButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(RunNav.IsChecked == true && ApiUrlBox.Text == HomeApiBox.Text && ExecutionApiField.Visibility == Visibility.Visible,
            "내 API 선택과 주소를 실행 조건에 전달");
        await Capture("api-conditions");
        Require(ReportHistoryLoaderCount() == historyBeforePreparation && ResultsSection.Visibility == Visibility.Collapsed,
            "조건 확인 단계에서 API 호출·시험·보고서 생성을 시작하지 않음");
        SelectPage("overview");
        HomeDemoTarget.IsChecked = true;
        PrepareHomeRunButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(ApiUrlBox.Text.Length == 0 && ExecutionApiField.Visibility == Visibility.Collapsed,
            "연습 모드로 바꾸면 이전 API를 시험 대상에서 제외");
        await Capture("ready");
        var failedResponse = ResultRow.From(TestResult.SystemFailure("UI-ERR", "응답", "문자", "고위험", "연결 실패"));
        Require(failedResponse.IsSystemFailure && failedResponse.ResultLabel == "응답 없음",
            "응답을 못 받은 경우와 판정 불일치를 다른 말로 구분");

        // 네이티브 창 명령도 실제 버튼의 Click 경로로 확인한다. 종료는 메인 대신 점검용 창에서 실행한다.
        await CheckWindowControlsAsync(Require);
        Require(RunButton.ToolTip is ToolTip
            && AutomationProperties.GetHelpText(RunButton).Contains("보고서"), "실행 버튼 말풍선과 화면 읽기 도구의 사용 안내 연결");
        Require(UsageHelp.GetKey(ApiUrlBox) == "api" && ToolTipService.GetInitialShowDelay(ApiUrlBox) == 350,
            "입력칸에 설명과 예시를 말풍선으로 제공");
        Require(ToolTipService.GetShowOnDisabled(OpenReportButton) && ToolTipService.GetShowsToolTipOnKeyboardFocus(RunButton) == true,
            "비활성 버튼과 키보드 포커스에서도 설명 제공");
        Require(InputBindings.OfType<KeyBinding>().Any(binding => binding.Key == Key.F1)
            && ApplicationCommands.Help.CanExecute(null, this), "F1 사용 안내 명령 연결");
        var tip = (ToolTip)RunButton.ToolTip;
        SelectPage("run");
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        tip.PlacementTarget = RunButton;
        tip.Opacity = 0;
        tip.IsOpen = true;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        Require(tip.IsOpen && tip.ActualWidth > 100, "실제 말풍선 열기·크기 계산");
        tip.IsOpen = false;
        tip.Opacity = 1;
        var guide = new HelpWindow(this, fitToScreen: false)
        {
            Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual,
            ShowActivated = false, ShowInTaskbar = false, Opacity = 0,
        };
        var guideClosed = false;
        guide.Closed += (_, _) => guideClosed = true;
        try
        {
            guide.Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            SaveWindowScreenshot(guide, Path.Combine(outputDirectory, "usage-guide.png"));
            Require(guide.GuideStartButton.ActualWidth > 100, "처음 사용하는 순서 안내 창 표시");
            guide.GuideStartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(guideClosed && RunNav.IsChecked == true && VerdictSuiteRadio.IsChecked == true,
                "시작 가이드에서 실제 판정 시험 화면으로 이동");
        }
        finally { if (!guideClosed) guide.Close(); }
        foreach (var dpiFactor in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var work = new Rect(-1366, 0, 1366, 728);
            var fit = AdaptiveWindowLayout.FitBounds(work, new Size(1120, 740), new DpiScale(dpiFactor, dpiFactor));
            Require(work.Contains(fit), $"1366×768 노트북·DPI {dpiFactor:P0}에서 시작 창이 작업 영역 안에 배치");
        }

        SelectSuite(TestSuite.Load);
        RateBox.Text = "0";
        await RunSelectedSuiteAsync();
        Require(StatusText.Text.Contains("0보다"), "잘못된 측정 조건을 알리고 재실행 가능");
        Require(RunButton.IsEnabled, "입력 오류 후 실행 버튼 복구");
        SelectPage("run");
        await Capture("invalid-input");

        foreach (var suite in Enum.GetValues<TestSuite>())
        {
            SelectSuite(suite);
            if (suite.IsMeasurementSuite())
            {
                DurationBox.Text = suite == TestSuite.Load ? "4" : "8";
                RateBox.Text = "5";
                StopSecondsBox.Text = suite == TestSuite.Recovery ? "2" : "0";
            }
            var beforeCount = ReportHistoryLoaderCount();
            var pending = RunSelectedSuiteAsync();
            if (suite == TestSuite.Load)
            {
                Require(!RunButton.IsEnabled && !SuitePanel.IsEnabled && !SettingsFields.IsEnabled,
                    "실행 중 다른 화면의 설정과 중복 실행 차단");
                await RunSelectedSuiteAsync();
            }
            await pending;
            Require(ResultsGrid.Items.Count > 0 && _lastHtmlReportPath is not null && File.Exists(_lastHtmlReportPath),
                $"{suite} 실제 채점·결과 바인딩·HTML 저장");
            Require(ReportHistoryLoaderCount() == beforeCount + 1, $"{suite} 실행 기록 한 건 저장");
            Require(DetailExpectedText.Text != "—" && DetailActualText.Text != "—", $"{suite} 선택 결과 비교");
            Require(ResultsSection.Visibility == Visibility.Visible && ResultSummaryTitle.Text.Length > 0
                && DetailExplanationText.Text.Length > 0, $"{suite} 결과 요약과 다음 확인 사항 안내");
            Require(RunButton.IsEnabled && !RunProgress.IsVisible, $"{suite} 실행 종료 후 조작 복구");
            await Capture(suite.ToString().ToLowerInvariant());
        }

        SelectPage("ai");
        await Capture("ai");
        await CollaborationPage.LoadDemoCasesAsync();
        Require(CasesNav.IsChecked == true && CollaborationPage.CasesGrid.Items.Count == 4,
            "예제 생성 후 사례 관리로 이동·4건 표시");
        Require(HeaderCasesText.Text.Contains(CollaborationPage.DatasetDisplay), "선택한 사례 집합 표시 동기화");
        await Capture("cases");
        SelectPage("review");
        Require(CollaborationPage.ReviewMessage.Text.Length > 0, "검수 화면의 선택 문자·근거 연결");
        await Capture("review");
        await CollaborationPage.RunDemoAsync();
        Require(TeamNav.IsChecked == true && CollaborationPage.TeamRunsGrid.Items.Count > 0, "공유 실행 후 팀 기록 이동");
        CollaborationPage.TeamRunsGrid.SelectedIndex = 0;
        Require(CollaborationPage.SharedResultsGrid.Items.Count == 4, "공유 스냅샷의 상세 결과 표시");
        await Capture("team");
        SelectPage("reports");
        Require(HistoryGrid.Items.Count >= 5, "로컬 이력에 일반 시험·탐색 시험 모두 표시");
        await Capture("reports");
        HistoryGrid.SelectedIndex = 0;
        Require(RunNav.IsChecked == true && ResultsGrid.Items.Count > 0, "저장 이력에서 결과 다시 열기");
        SelectPage("settings");
        await Capture("settings");
        SelectPage("overview");
        Require(RecentRunsGrid.Items.Count > 0 && OverviewContextText.Text.Contains("가장 최근"), "시작 화면의 이전 실행은 실제 보고서에서 조회");
        await Capture("overview");
        SelectSuite(TestSuite.Load);
        SelectPage("run");
        Width = 840;
        Height = 520;
        await Capture("minimum-window");
        Require(RunButton.ActualHeight > 20 && ExecutionOptions.ActualHeight > 70,
            "최소 창 크기에서 실행 조건과 시작 버튼 유지·본문 스크롤 제공");
        var compactScale = _adaptiveLayout.CurrentScale;
        var closePoint = TitleBar.CloseButton.TranslatePoint(new Point(0, 0), (UIElement)Content);
        Require(closePoint.X >= 0 && closePoint.Y >= 0
            && closePoint.X + TitleBar.CloseButton.ActualWidth <= ((FrameworkElement)Content).ActualWidth + 1,
            "작은 창에서도 닫기·최소화 버튼이 본문 배율과 별개로 창 안에 유지");
        Width = 1280;
        Height = 840;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        Require(compactScale >= 0.75 && _adaptiveLayout.CurrentScale > compactScale && _adaptiveLayout.CurrentScale <= 1,
            "창을 늘리거나 줄이면 본문 배율이 자동 조정");

        File.WriteAllText(Path.Combine(outputDirectory, "ui-smoke.json"), JsonSerializer.Serialize(new
        {
            Passed = true, Checks = checks, Target = "Fake + DemoTeamRunClient",
            ReportDirectory = ReportDirectoryBox.Text,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private int ReportHistoryLoaderCount() => QaRunner.Reports.ReportHistoryLoader
        .LoadAll(Path.GetDirectoryName(DashboardPath)!).Count;

    private async Task CheckWindowControlsAsync(Action<bool, string> require)
    {
        require(TitleBar.MinimizeButton.IsVisible && TitleBar.MaximizeButton.IsVisible && TitleBar.CloseButton.IsVisible,
            "메인 제목 표시줄의 최소화·최대화·닫기 버튼 표시");
        require(ResizeMode == ResizeMode.CanResize && WindowChrome.GetWindowChrome(this).ResizeBorderThickness.Left > 0,
            "창 가장자리와 모서리 크기 조절 영역 구성");
        var probe = new CollaborationWindow(fitToScreen: false)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000,
            ShowActivated = false, ShowInTaskbar = false, Opacity = 0,
        };
        var closed = false;
        probe.Closed += (_, _) => closed = true;
        async Task WaitFor(Func<bool> ready)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!ready() && DateTime.UtcNow < deadline) await Task.Delay(25);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        }
        try
        {
            probe.Show();
            await WaitFor(() => probe.IsLoaded);
            probe.TitleBar.MinimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => probe.WindowState == WindowState.Minimized);
            require(probe.WindowState == WindowState.Minimized, "최소화 버튼의 실제 Windows 상태 전환");
            SystemCommands.RestoreWindow(probe);
            await WaitFor(() => probe.WindowState == WindowState.Normal);
            require(probe.WindowState == WindowState.Normal, "최소화 후 원래 창으로 복원");
            probe.TitleBar.MaximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => probe.WindowState == WindowState.Maximized);
            require(probe.WindowState == WindowState.Maximized && (string)probe.TitleBar.MaximizeButton.ToolTip == "이전 크기로",
                "최대화 버튼과 복원 안내 전환");
            probe.TitleBar.MaximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => probe.WindowState == WindowState.Normal);
            require(probe.WindowState == WindowState.Normal && (string)probe.TitleBar.MaximizeButton.ToolTip == "최대화",
                "최대화 버튼을 다시 눌러 이전 크기로 복원");
            probe.TitleBar.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => closed);
            require(closed, "닫기 버튼의 실제 창 종료");
        }
        finally { if (!closed) probe.Close(); }

        var fitted = new CollaborationWindow { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        try
        {
            fitted.Show();
            await WaitFor(() => fitted.AdaptiveLayout.IsWithinWorkArea());
            require(fitted.AdaptiveLayout.IsWithinWorkArea(), "실제 모니터의 DPI·작업 영역에 시작 창 크기와 위치 맞춤");
        }
        finally { fitted.Close(); }
    }
}
