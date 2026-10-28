using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using QaRunner.Measurement;
using QaRunner.Models;
using QaRunner.Pipeline;
using QaRunner.Reports;

namespace QaRunner.Desktop;

/// <summary>
/// 공통 탐색·시험 실행·상세 비교·로컬 이력을 제공하는 Fluent Desktop 작업 화면.
/// 실제 실행 절차는 콘솔 프로그램과 같은 QaRunService를 쓴다.
/// </summary>
public partial class MainWindow : Window
{
    // 마지막으로 저장한 HTML 보고서 경로. "HTML 보고서 열기" 버튼에서 쓴다
    private string? _lastHtmlReportPath;
    private bool _isRunning;
    private bool _isReady;
    private bool _applyingSuiteDefaults;
    private string _verdictCasesPath = CommandLineOptions.DefaultCasesPathFor(TestSuite.Verdict);
    private readonly AdaptiveWindowLayout _adaptiveLayout;

    public MainWindow(bool fitToScreen = true)
    {
        InitializeComponent();
        DesktopBackgrounds.ApplyRandom(HomeHero);
        _adaptiveLayout = new AdaptiveWindowLayout(this, WorkspaceViewport, WorkspaceRoot, ZoomStatusText, fitToScreen);
        UsageHelp.Enable(this);
        ApplySuiteDefaults();
        _isReady = true;
        CollaborationPage.NavigationRequested += SelectPage;
        CollaborationPage.WorkflowCompleted += RefreshHistory;
        CollaborationPage.DatasetChanged += RefreshHeader;
        CollaborationPage.BusyChanged += _ => UpdateInteractionState();
        RefreshHeader();
        RefreshHistory();
        SelectPage("overview");
        RefreshHomeRecommendation();

        // 예전에 만든 대시보드가 있으면 실행 전에도 바로 열 수 있게 한다
        OpenDashboardButton.IsEnabled = File.Exists(DashboardPath);
    }

    // 보고서 폴더 칸 기준의 대시보드 경로
    private string DashboardPath
    {
        get
        {
            var reportDirectory = ReportDirectoryBox.Text.Trim();
            return Path.GetFullPath(Path.Combine(
                reportDirectory.Length > 0 ? reportDirectory : CommandLineOptions.DefaultReportDirectory,
                DashboardWriter.FileName));
        }
    }

    /// <summary>
    /// 시험 종류를 코드에서 고른다 (화면 캡처 모드에서 사용).
    /// </summary>
    public void SelectSuite(TestSuite testSuite)
    {
        VerdictSuiteRadio.IsChecked = testSuite == TestSuite.Verdict;
        PipelineSuiteRadio.IsChecked = testSuite == TestSuite.Pipeline;
        LoadSuiteRadio.IsChecked = testSuite == TestSuite.Load;
        RecoverySuiteRadio.IsChecked = testSuite == TestSuite.Recovery;
    }

    /// <summary>
    /// 지금 화면에 고른 설정으로 시험을 돌리고 결과를 보여 준다.
    /// 준비 단계 오류(파일 없음, 잘못된 숫자·주소 등)는 창을 닫지 않고 상태 칸에 알린다.
    /// </summary>
    public async Task RunSelectedSuiteAsync()
    {
        if (_isRunning || CollaborationPage.IsBusy) return;
        try
        {
            var options = ReadOptionsFromScreen();
            SelectPage("run");
            ExecutionScroll.ScrollToTop();
            SetRunning(true, options);
            ResultsGrid.ItemsSource = null;
            ResultsSection.Visibility = Visibility.Collapsed;
            ExecutionError.Visibility = Visibility.Collapsed;
            _displayedRun = null;
            DetailCaseText.Text = "시험이 끝나면 결과를 확인할 수 있어요.";
            DetailInputText.Text = DetailExpectedText.Text = DetailActualText.Text = "—";
            TotalCountText.Text = PassedCountText.Text = FailedCountText.Text = PassRateText.Text = "—";
            OpenReportButton.IsEnabled = false;
            _lastHtmlReportPath = null;
            FakeTargetNotice.Visibility = Visibility.Collapsed;
            RunInfoText.Text = $"{options.Suite.ToKoreanName()} · 시험 실행 중";

            var preparedRun = QaRunService.Prepare(options);
            var testRun = await QaRunService.RunAsync(preparedRun, options.ReportDirectory);
            var savedReports = QaRunService.SaveReports(testRun, options.ReportDirectory);

            ShowTestRun(testRun);
            _lastHtmlReportPath = Path.GetFullPath(savedReports.HtmlReportPath);
            OpenDashboardButton.IsEnabled = true;
            RefreshHistory();
            ShowStatus("시험이 끝났어요. 결과를 골라 자세히 확인해 보세요. 보고서도 저장했어요.", isError: false);
        }
        catch (Exception exception) when (QaRunService.IsPreparationError(exception))
        {
            ShowExecutionError($"아직 시험을 시작하지 못했어요. {exception.Message}");
        }
        catch (PipelineReadException exception)
        {
            ShowExecutionError($"시험 대상에 연결하지 못했어요. 주소와 서버 상태를 확인해 주세요.\n{exception.Message}");
        }
        finally
        {
            SetRunning(false);
        }
    }

    /// <summary>
    /// 창 내용을 PNG 파일로 저장한다. 글자가 선명하도록 2배 크기로 그린다.
    /// </summary>
    /// <param name="pngFilePath">저장할 PNG 파일 경로</param>
    public void SaveScreenshot(string pngFilePath) => SaveWindowScreenshot(this, pngFilePath);

    internal static void SaveWindowScreenshot(Window window, string pngFilePath)
    {
        window.UpdateLayout();
        var windowContent = (FrameworkElement)window.Content;

        // 창 내용의 바깥 여백(Margin)까지 포함한 크기로 찍는다
        var margin = windowContent.Margin;
        var imageBounds = new Rect(0, 0,
            windowContent.ActualWidth + margin.Left + margin.Right,
            windowContent.ActualHeight + margin.Top + margin.Bottom);
        var contentBounds = new Rect(margin.Left, margin.Top, windowContent.ActualWidth, windowContent.ActualHeight);

        const double scale = 2;
        var bitmap = new RenderTargetBitmap(
            (int)(imageBounds.Width * scale), (int)(imageBounds.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);

        // 투명한 영역도 실제 창 테마와 같은 배경으로 저장한다.
        var drawing = new DrawingVisual();
        using (var drawingContext = drawing.RenderOpen())
        {
            drawingContext.DrawRectangle(window.Background ?? Brushes.Transparent, null, imageBounds);
            // 크기를 줄인 직후 이전 자손의 렌더링 경계가 남아 있어도 현재 창 영역만 캡처한다.
            var contentBrush = new VisualBrush(windowContent)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, windowContent.ActualWidth, windowContent.ActualHeight),
                Stretch = Stretch.Fill,
            };
            drawingContext.DrawRectangle(contentBrush, null, contentBounds);
        }
        bitmap.Render(drawing);

        var pngEncoder = new PngBitmapEncoder();
        pngEncoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var pngFile = File.Create(pngFilePath);
        pngEncoder.Save(pngFile);
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e) => await RunSelectedSuiteAsync();

    private void OpenCollaboration_Click(object sender, RoutedEventArgs e) => SelectPage("team");

    // 저장한 HTML 보고서를 기본 브라우저로 연다
    private void OpenReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastHtmlReportPath is not null)
            OpenLocalPath(_lastHtmlReportPath);
    }

    // 지금까지의 실행 기록을 모은 대시보드를 기본 브라우저로 연다
    private void OpenDashboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(DashboardPath))
            OpenLocalPath(DashboardPath);
        else
            ShowStatus("아직 실행 기록이 없어요. 시험을 한 번 실행하면 전체 추이도 볼 수 있어요.", isError: true);
    }

    // 시험 종류를 바꾸면 입력 경로 기본값과 API 주소 칸 사용 여부를 바꾼다
    private void SuiteRadio_Checked(object sender, RoutedEventArgs e) => ApplySuiteDefaults();

    private TestSuite SelectedSuite =>
        PipelineSuiteRadio.IsChecked == true ? TestSuite.Pipeline
        : LoadSuiteRadio.IsChecked == true ? TestSuite.Load
        : RecoverySuiteRadio.IsChecked == true ? TestSuite.Recovery
        : TestSuite.Verdict;

    // 화면에서 바로 시연할 때 쓰는 측정 시간 기본값. 실행안 기준(부하 300초, 복구 120초·30초 중단)은 기다리기 길어서
    // 짧게 잡았다. 칸에서 숫자를 바꾸면 실행안 기준으로도 돌릴 수 있다.
    private static readonly MeasurementSettings DesktopLoadDefaults = new(DurationSeconds: 30, EventsPerSecond: 5, StopSeconds: 0);
    private static readonly MeasurementSettings DesktopRecoveryDefaults = new(DurationSeconds: 60, EventsPerSecond: 5, StopSeconds: 15);

    // 시험 종류에 맞게 입력 칸 이름·기본값과 보이는 칸을 바꾼다.
    // 창을 만드는 도중(InitializeComponent 안)에도 불리므로 아직 없는 칸이 있으면 건너뛴다
    private void ApplySuiteDefaults()
    {
        if (CasesPathBox is null || ApiUrlBox is null || CasesPathLabel is null || ApiUrlHint is null
            || MeasurementPanel is null || DurationBox is null || RateBox is null || StopSecondsBox is null || MeasurementHint is null)
            return;

        var suite = SelectedSuite;

        // 입력: 판정·부하는 입력값 파일, 파이프라인은 시나리오 폴더, 복구는 쓰지 않음
        CasesPathLabel.Text = suite == TestSuite.Pipeline ? "시험 시나리오가 있는 폴더" : "시험 문자와 예상 정답";
        CasesPathField.Visibility = suite == TestSuite.Recovery ? Visibility.Collapsed : Visibility.Visible;
        StopSecondsField.Visibility = suite == TestSuite.Recovery ? Visibility.Visible : Visibility.Collapsed;
        _applyingSuiteDefaults = true;
        CasesPathBox.Text = suite == TestSuite.Verdict ? _verdictCasesPath : CommandLineOptions.DefaultCasesPathFor(suite);
        _applyingSuiteDefaults = false;
        CasesPathBox.IsEnabled = suite != TestSuite.Recovery;

        // API 주소: 판정 시험과 부하 시험(API 부하)에서만 쓴다
        var usesApi = suite is TestSuite.Verdict or TestSuite.Load;
        ApiUrlBox.IsEnabled = usesApi;
        RefreshExecutionTarget();

        // 측정 설정: 부하·복구 시험에서만 보인다
        MeasurementPanel.Visibility = suite.IsMeasurementSuite() ? Visibility.Visible : Visibility.Collapsed;
        if (suite.IsMeasurementSuite())
        {
            var defaults = suite == TestSuite.Recovery ? DesktopRecoveryDefaults : DesktopLoadDefaults;
            DurationBox.Text = defaults.DurationSeconds.ToString();
            RateBox.Text = defaults.EventsPerSecond.ToString("0.##");
            StopSecondsBox.Text = suite == TestSuite.Recovery ? defaults.StopSeconds.ToString() : "";
            StopSecondsBox.IsEnabled = suite == TestSuite.Recovery;
            MeasurementHint.Text = suite == TestSuite.Recovery
                ? "먼저 짧게 실행해 보세요. 프로젝트 기준은 120초 동안 보내며 30초 멈추는 조건이에요."
                : "먼저 짧게 실행해 보세요. 프로젝트 기준은 300초 동안 초당 5건, 총 1,500건이에요.";
        }
        if (SuiteTitleText is not null)
        {
            SuiteTitleText.Text = suite switch
            {
                TestSuite.Pipeline => "데이터 처리 시험",
                TestSuite.Load => "부하와 응답 속도 시험",
                TestSuite.Recovery => "중단 후 복구 시험",
                _ => "문자 판정 시험",
            };
            SuiteDescriptionText.Text = suite switch
            {
                TestSuite.Pipeline => "중복·순서 변경·지연 조건에서 저장·정제·집계 건수를 비교해요.",
                TestSuite.Load => "입력을 계속 보내면서 응답 속도와 오류, 밀린 작업이 있는지 확인해요.",
                TestSuite.Recovery => "처리를 잠깐 멈췄다가 다시 켜고, 얼마나 빨리 따라잡는지 확인해요.",
                _ => "문자를 보내고 돌아온 판정이 미리 정한 정답과 같은지 비교해요.",
            };
        }
        RefreshHeader();
        if (_isReady)
        {
            ResultsGrid.ItemsSource = null;
            TotalCountText.Text = PassedCountText.Text = FailedCountText.Text = PassRateText.Text = "—";
            DetailCaseText.Text = "왼쪽에서 항목을 골라 주세요.";
            ResultsSection.Visibility = Visibility.Collapsed;
            ExecutionError.Visibility = Visibility.Collapsed;
            _displayedRun = null;
            DetailInputText.Text = DetailExpectedText.Text = DetailActualText.Text = "—";
            RunInfoText.Text = "조건을 확인했다면 시험을 시작해 주세요.";
            FakeTargetNotice.Visibility = Visibility.Collapsed;
            OpenReportButton.IsEnabled = false;
            _lastHtmlReportPath = null;
        }
    }

    // 화면의 입력 칸 값을 실행 옵션으로 모은다. 콘솔의 명령줄 옵션과 같은 모양을 쓴다.
    // 숫자 칸이 비었거나 숫자가 아니면 ArgumentException을 던진다 (실행 준비 오류로 표시됨)
    private CommandLineOptions ReadOptionsFromScreen()
    {
        var suite = SelectedSuite;
        var apiUrl = ApiUrlBox.Text.Trim();
        var reportDirectory = ReportDirectoryBox.Text.Trim();
        var usesApi = suite is TestSuite.Verdict or TestSuite.Load;
        if (usesApi && apiUrl.Length > 0 && !IsHttpAddress(apiUrl))
            throw new ArgumentException("API 주소를 확인해 주세요. http://localhost:8000처럼 http:// 또는 https://로 시작해야 해요.");

        MeasurementSettings? measurement = null;
        if (suite.IsMeasurementSuite())
        {
            measurement = new MeasurementSettings(
                DurationSeconds: ReadInt(DurationBox, "시간(초)"),
                EventsPerSecond: ReadDouble(RateBox, "초당 건수"),
                StopSeconds: suite == TestSuite.Recovery ? ReadInt(StopSecondsBox, "중단(초)") : 0);
            ValidateMeasurement(measurement, suite);
        }

        return new CommandLineOptions(
            Suite: suite,
            CasesPath: suite == TestSuite.Recovery ? CommandLineOptions.NoCasesPath : CasesPathBox.Text.Trim(),
            ReportDirectory: reportDirectory.Length > 0 ? reportDirectory : CommandLineOptions.DefaultReportDirectory,
            ApiBaseUrl: usesApi && apiUrl.Length > 0 ? apiUrl : null,
            Measurement: measurement);
    }

    private static int ReadInt(TextBox textBox, string fieldName) =>
        int.TryParse(textBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new ArgumentException($"{fieldName} 칸에 1, 2처럼 정수를 입력해 주세요.");

    private static double ReadDouble(TextBox textBox, string fieldName) =>
        double.TryParse(textBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new ArgumentException($"{fieldName} 칸에 숫자를 입력해 주세요.");

    // 실행 중에는 버튼을 막아 두 번 눌리지 않게 한다. 부하·복구 시험은 얼마나 걸리는지도 알려 준다
    private void SetRunning(bool isRunning, CommandLineOptions? options = null)
    {
        _isRunning = isRunning;
        UpdateInteractionState();
        RunButton.Content = isRunning ? "시험 중이에요…" : "시험 시작하기";
        RunProgress.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
        if (!isRunning)
            return;

        var runningMessage = options?.Measurement is { } measurement
            ? $"시험 중이에요. 약 {measurement.DurationSeconds}초 동안 입력을 보낸 뒤 결과를 정리해요."
            : "시험 중이에요. 잠시만 기다려 주세요.";
        ShowStatus(runningMessage, isError: false);
    }

    // 실행 기록을 요약 숫자와 결과 표로 보여 준다
    private void ShowTestRun(TestRun testRun)
    {
        ShowResultSummary(testRun);
        var targetDescription = DescribeTarget(testRun);
        RunInfoText.Text = $"{targetDescription} · {testRun.Duration.TotalSeconds:0.00}초 · 실행 {testRun.RunId}"
            + (testRun.HasProvisionalLabels ? " · 정답 검토 전" : "");
        FakeTargetNotice.Visibility = testRun.UsedFakeTarget ? Visibility.Visible : Visibility.Collapsed;
        ((TextBlock)FakeTargetNotice.Child).Text = testRun.TargetName.Contains("ApiVerdictService", StringComparison.Ordinal)
            ? "판정 API에는 실제로 요청했지만, 데이터 처리는 연습용 파이프라인을 사용했어요. 각 항목의 시험 대상을 구분해 읽어 주세요."
            : "연습용 대상의 결과예요. 일부러 틀리는 규칙이 들어 있어서 실제 서비스의 품질과는 다를 수 있어요.";

        TotalCountText.Text = testRun.TotalCount.ToString();
        PassedCountText.Text = testRun.PassedCount.ToString();
        FailedCountText.Text = testRun.FailedCount.ToString();
        PassRateText.Text = $"{testRun.PassRate:0.0}%";

        // 실패한 항목을 위에 먼저 보여 준다 (HTML 보고서와 같은 순서)
        ResultsGrid.ItemsSource = testRun.Results
            .OrderBy(testResult => testResult.IsPassed)
            .Select(ResultRow.From)
            .ToList();

        OpenReportButton.IsEnabled = true;
        ResultsGrid.SelectedIndex = testRun.Results.Count > 0 ? 0 : -1;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (ExecutionPage.IsVisible && ResultsSection.IsVisible)
            {
                UpdateLayout();
                var top = ResultsSection.TranslatePoint(new Point(0, 0), ExecutionScroll).Y + ExecutionScroll.VerticalOffset;
                ExecutionScroll.ScrollToVerticalOffset(top);
            }
        }));
    }

    /// <summary>메인 탐색 선택과 실제 보이는 페이지를 함께 변경한다.</summary>
    public void SelectPage(string page)
    {
        if (!_isReady) return;
        if (page is "ai" or "review") CasesNav.IsChecked = true;
        else AiNav.IsChecked = ReviewNav.IsChecked = false;
        var nav = NavigationBar.Children.OfType<RadioButton>().FirstOrDefault(item => (string)item.Tag == page);
        if (nav is null) return;
        if (nav.IsChecked != true) { nav.IsChecked = true; return; }
        OverviewPage.Visibility = page == "overview" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "overview") RefreshHomeRecommendation();
        ExecutionPage.Visibility = page == "run" ? Visibility.Visible : Visibility.Collapsed;
        ReportsPage.Visibility = page == "reports" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        var isCollaboration = page is "cases" or "ai" or "review" or "team";
        CollaborationPage.Visibility = isCollaboration ? Visibility.Visible : Visibility.Collapsed;
        if (isCollaboration)
        {
            CollaborationPage.ReportDirectory = ReportDirectoryBox.Text.Trim().Length > 0
                ? ReportDirectoryBox.Text.Trim() : CommandLineOptions.DefaultReportDirectory;
            CollaborationPage.ShowPage(page);
        }
        if (page is "overview" or "reports") RefreshHistory();
        RefreshHeader();
    }

    private void Navigation_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string page }) SelectPage(page);
    }

    private void NewRun_Click(object sender, RoutedEventArgs e) => SelectPage("run");

    public bool NavigateFromGuide(string page)
    {
        if (_isRunning || CollaborationPage.IsBusy) { ShowStatus("시험이 끝나면 이동할 수 있어요.", false); return false; }
        if (page == "verdict") { SelectSuite(TestSuite.Verdict); SelectPage("run"); }
        else SelectPage(page);
        return true;
    }
    private void OpenHistory_Click(object sender, RoutedEventArgs e) => SelectPage("reports");
    private void RefreshHistory_Click(object sender, RoutedEventArgs e) => RefreshHistory();
    private void OpenWorkspace_Click(object sender, RoutedEventArgs e) => OpenLocalPath(Directory.GetCurrentDirectory());

    private void ChooseCases_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning || CollaborationPage.IsBusy) return;
        var dialog = new OpenFileDialog { Title = "판정 사례 JSON 선택", Filter = "JSON 사례 파일|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var cases = TestCaseLoader.LoadFromFile(dialog.FileName);
            if (cases.Count == 0 || cases.Any(item => item is null || string.IsNullOrWhiteSpace(item.CaseId)
                || string.IsNullOrWhiteSpace(item.Message) || string.IsNullOrWhiteSpace(item.ExpectedVerdict)))
                throw new ArgumentException("사례 ID·문자·예상 정답이 있는 사례 파일을 선택하세요.");
            _verdictCasesPath = dialog.FileName;
            SelectSuite(TestSuite.Verdict);
            CasesPathBox.Text = dialog.FileName;
            CollaborationPage.LoadRuleCases(dialog.FileName);
            SelectPage("cases");
            RefreshHistory();
        }
        catch (Exception exception) when (QaRunService.IsPreparationError(exception))
        { ShowStatus($"파일을 읽지 못했어요. {exception.Message}", true); }
    }

    private void EnvironmentField_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_isReady) return;
        if (!_applyingSuiteDefaults && SelectedSuite == TestSuite.Verdict) _verdictCasesPath = CasesPathBox.Text.Trim();
        RefreshHeader();
    }

    private void RefreshHeader()
    {
        if (!_isReady) return;
        HeaderCasesText.Text = CollaborationPage.Visibility == Visibility.Visible
            ? CollaborationPage.DatasetDisplay : Path.GetFileName(_verdictCasesPath);
        HeaderTargetText.Text = SelectedSuite is TestSuite.Verdict or TestSuite.Load && ApiUrlBox.Text.Trim().Length > 0
            ? "이 주소의 API를 시험해요: " + ApiUrlBox.Text.Trim() : "주소를 비워 두면 연습용 판정기를 사용해요.";
        RefreshExecutionTarget();
    }

    // 실행 중에 다른 화면의 설정 변경이나 두 번째 QA가 끼어들지 않게 한다.
    private void UpdateInteractionState()
    {
        var idle = !_isRunning && !CollaborationPage.IsBusy;
        RunButton.IsEnabled = SuitePanel.IsEnabled = ExecutionOptions.IsEnabled = SettingsFields.IsEnabled = CasesSelectorButton.IsEnabled = idle;
        CollaborationPage.IsEnabled = !_isRunning;
        HomeQuestions.IsEnabled = idle;
    }

    private void Result_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not ResultRow row) return;
        DetailCaseText.Text = $"{row.CaseId} · {row.CheckName} · {row.ResultLabel}";
        DetailInputText.Text = row.Input;
        DetailExpectedText.Text = row.Expected;
        DetailActualText.Text = row.ActualDisplay;
        ExplainResult(row);
    }

    /// <summary>화면의 요약·최근 기록·보고서 목록은 저장된 실제 보고서로만 채운다.</summary>
    private void RefreshHistory()
    {
        if (!_isReady) return;
        try
        {
            var directory = Path.GetDirectoryName(DashboardPath)!;
            var runs = ReportHistoryLoader.LoadAll(directory);
            var rows = runs.OrderByDescending(run => run.StartedAt).Select(run => new HistoryRow(run)).ToArray();
            RecentRunsGrid.ItemsSource = rows.Take(5).ToArray();
            HistoryGrid.ItemsSource = rows;
            HistoryEmptyText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var latest = runs.LastOrDefault();
            OverviewContextText.Text = latest is null ? "시험을 실행하면 이곳에서 다시 볼 수 있어요."
                : $"가장 최근 시험: {latest.SuiteName} · {latest.PassedCount}/{latest.TotalCount}개 통과";
            OpenDashboardButton.IsEnabled = File.Exists(DashboardPath);
        }
        catch (Exception exception) when (QaRunService.IsPreparationError(exception))
        { ShowStatus($"저장한 기록을 읽지 못했어요. {exception.Message}", true); }
    }

    private void RecentRun_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (((DataGrid)sender).SelectedItem is not HistoryRow row || _isRunning || CollaborationPage.IsBusy) return;
        var suite = Enum.GetValues<TestSuite>().FirstOrDefault(item => item.ToKoreanName() == row.Run.SuiteName);
        SelectSuite(suite);
        ShowTestRun(row.Run);
        var path = Path.Combine(Path.GetDirectoryName(DashboardPath)!, $"{row.Run.RunId}.html");
        _lastHtmlReportPath = File.Exists(path) ? path : null;
        OpenReportButton.IsEnabled = _lastHtmlReportPath is not null;
        SelectPage("run");
    }

    private void OpenLocalPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        { ShowStatus($"파일을 열지 못했어요. {exception.Message}", true); }
    }

    private sealed record HistoryRow(TestRun Run)
    {
        public string RunId => Run.RunId;
        public string SuiteName => Run.SuiteName;
        public string StartedAtText => Run.StartedAt.ToLocalTime().ToString("MM-dd HH:mm:ss");
        public string CountText => $"{Run.PassedCount} / {Run.TotalCount}";
        public string TargetText => DescribeTarget(Run);
        public string LabelText => Run.HasProvisionalLabels ? "정답 검토 전" : "사례 파일 기준";
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)FindResource(isError ? "FailBrush" : "MutedBrush");
    }
}
