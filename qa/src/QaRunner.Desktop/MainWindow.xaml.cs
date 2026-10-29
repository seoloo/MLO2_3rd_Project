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
        // 메인 창에서는 위쪽 '기록·관리' 제목과 탭이 화면 이름을 보여 주므로 안쪽 큰 제목은 숨긴다
        CollaborationPage.PageTitle.Visibility = Visibility.Collapsed;
        HomeHeroContent.SizeChanged += ClipHomeHero;
        // 인사말과 왼쪽 아래 사용자 표시는 Windows 사용자 이름을 쓴다
        var userName = Environment.UserName;
        UserNameText.Text = userName;
        UserInitialText.Text = userName.Length > 0 ? userName[..1].ToUpperInvariant() : "?";
        HomeGreetingText.Text = $"안녕하세요, {userName}님";
        InitializeChecklist();
        SelectHomeArea(TestArea.Service);
        RefreshHeader();
        RefreshHistory();
        // 이미 시험해 본 기록이 있으면 여정의 앞 두 단계(고르기·실행하기)는 끝낸 것으로 본다
        if (HistoryGrid.Items.Count > 0)
        {
            CompleteStep(ChecklistStepKind.PickTest);
            CompleteStep(ChecklistStepKind.RunTest);
        }
        SelectPage("home");

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
        SelfCheckSuiteRadio.IsChecked = testSuite == TestSuite.SelfCheck;
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
            CompleteStep(ChecklistStepKind.PickTest);
            CloseSheet();
            SelectPage("run");
            ExecutionScroll.ScrollToTop();
            SetRunning(true, options);
            ResultsGrid.ItemsSource = null;
            ResultsSection.Visibility = Visibility.Collapsed;
            ExecutionError.IsOpen = false;
            HideCoach();
            _displayedRun = null;
            DetailCaseText.Text = "시험이 끝나면 결과를 확인할 수 있어요.";
            DetailInputText.Text = DetailExpectedText.Text = DetailActualText.Text = "—";
            TotalCountText.Text = PassedCountText.Text = FailedCountText.Text = PassRateText.Text = "—";
            PassRateBar.Value = 0;
            OpenReportButton.IsEnabled = false;
            _lastHtmlReportPath = null;
            FakeTargetNotice.IsOpen = false;
            RunInfoText.Text = $"{options.Suite.ToKoreanName()} · 시험 실행 중";

            var preparedRun = QaRunService.Prepare(options);
            var testRun = await QaRunService.RunAsync(preparedRun, options.ReportDirectory);
            var savedReports = QaRunService.SaveReports(testRun, options.ReportDirectory);

            ShowTestRun(testRun);
            _lastHtmlReportPath = Path.GetFullPath(savedReports.HtmlReportPath);
            OpenDashboardButton.IsEnabled = true;
            RefreshHistory();
            CompleteStep(ChecklistStepKind.RunTest);
            ShowCompletion(testRun);
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
        if (_lastHtmlReportPath is null) return;
        CompleteStep(ChecklistStepKind.OpenReport);
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
        : SelfCheckSuiteRadio.IsChecked == true ? TestSuite.SelfCheck
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

        // 입력: 판정·부하는 입력값 파일, 파이프라인은 시나리오 폴더, 복구·QA 도구 점검은 쓰지 않음
        CasesPathLabel.Text = suite == TestSuite.Pipeline ? "시험 시나리오가 있는 폴더" : "시험 문자와 예상 정답";
        CasesPathField.Visibility = suite.UsesNoCasesPath() ? Visibility.Collapsed : Visibility.Visible;
        StopSecondsField.Visibility = suite == TestSuite.Recovery ? Visibility.Visible : Visibility.Collapsed;
        _applyingSuiteDefaults = true;
        CasesPathBox.Text = suite == TestSuite.Verdict ? _verdictCasesPath : CommandLineOptions.DefaultCasesPathFor(suite);
        _applyingSuiteDefaults = false;
        CasesPathBox.IsEnabled = !suite.UsesNoCasesPath();

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
                TestSuite.SelfCheck => "QA 도구 점검",
                _ => "문자 판정 시험",
            };
            SuiteDescriptionText.Text = suite switch
            {
                TestSuite.Pipeline => "중복·순서 변경·지연 조건에서 저장·정제·집계 건수를 비교해요.",
                TestSuite.Load => "입력을 계속 보내면서 응답 속도와 오류, 밀린 작업이 있는지 확인해요.",
                TestSuite.Recovery => "처리를 잠깐 멈췄다가 다시 켜고, 얼마나 빨리 따라잡는지 확인해요.",
                TestSuite.SelfCheck => "답을 이미 아는 입력으로 이 QA 도구의 채점기·정답 계산·보고서가 맞게 동작하는지 확인해요.",
                _ => "문자를 보내고 돌아온 판정이 미리 정한 정답과 같은지 비교해요.",
            };
        }
        RefreshHeader();
        UpdateSheetHeader();
        if (_isReady)
        {
            ResultsGrid.ItemsSource = null;
            TotalCountText.Text = PassedCountText.Text = FailedCountText.Text = PassRateText.Text = "—";
            PassRateBar.Value = 0;
            DetailCaseText.Text = "위에서 항목을 골라 주세요.";
            ResultsSection.Visibility = Visibility.Collapsed;
            ExecutionError.IsOpen = false;
            HideCoach();
            _displayedRun = null;
            DetailInputText.Text = DetailExpectedText.Text = DetailActualText.Text = "—";
            RunInfoText.Text = "조건을 확인했다면 시험을 시작해 주세요.";
            FakeTargetNotice.IsOpen = false;
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
        if (usesApi && ApiTargetRadio.IsChecked == true && apiUrl.Length == 0)
            throw new ArgumentException("판정 API 주소를 넣어 주세요. 예: http://localhost:8000. 서버 없이 해 보려면 ‘연습용 대상’을 골라 주세요.");
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
            CasesPath: suite.UsesNoCasesPath() ? CommandLineOptions.NoCasesPath : CasesPathBox.Text.Trim(),
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
        // 연습용 대상이면 완료 카드에 작은 표시를 붙이고, API와 연습용 파이프라인이 섞였을 때만 노란 안내를 따로 띄운다
        var mixedTarget = testRun.TargetName.Contains("ApiVerdictService", StringComparison.Ordinal);
        ResultTargetChip.Visibility = testRun.UsedFakeTarget ? Visibility.Visible : Visibility.Collapsed;
        ResultTargetChipText.Text = mixedTarget ? "API + 연습용 파이프라인" : "연습용 대상";
        ResultTargetChip.ToolTip = "연습용 대상에는 일부러 틀리는 규칙이 들어 있어서 실제 서비스의 품질과는 다를 수 있어요.";
        FakeTargetNotice.IsOpen = testRun.UsedFakeTarget && mixedTarget;
        FakeTargetNotice.Message = "판정 API에는 실제로 요청했지만, 데이터 처리는 연습용 파이프라인을 사용했어요. 각 항목의 시험 대상을 구분해 읽어 주세요.";
        // 기록에서 다시 연 결과는 ‘지난 실행’으로, 방금 끝난 결과는 ShowCompletion이 ‘시험이 끝났어요’로 바꾼다
        ResultHeroTitle.Text = "지난 실행 결과예요";
        ResultHeroIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.History24;

        TotalCountText.Text = testRun.TotalCount.ToString();
        PassedCountText.Text = testRun.PassedCount.ToString();
        FailedCountText.Text = testRun.FailedCount.ToString();
        PassRateText.Text = $"{testRun.PassRate:0.0}%";
        PassRateBar.Value = testRun.PassRate;

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
                // 완료 카드 위에 숨 쉴 여백을 조금 남긴다
                ExecutionScroll.ScrollToVerticalOffset(Math.Max(0, top - 16));
            }
        }));
    }

    /// <summary>화면이 속한 메뉴. 시험(run) · 기록(reports·team) · 관리(cases·ai·review·settings)</summary>
    private static string SectionOf(string page) => page switch
    {
        "home" => "home",
        "reports" or "team" => "history",
        "cases" or "ai" or "review" or "settings" => "manage",
        _ => "test",
    };

    // 코드로 메뉴·탭 선택을 바꾸는 동안 Checked 이벤트가 다시 화면을 바꾸지 않게 막는다
    private bool _selectingPage;

    /// <summary>왼쪽 메뉴·안쪽 탭 선택과 실제 보이는 화면을 함께 바꾼다.</summary>
    public void SelectPage(string page)
    {
        if (!_isReady) return;
        if (page == "overview") page = "home";
        var section = SectionOf(page);
        _selectingPage = true;
        HomeNav.IsChecked = section == "home";
        TestNav.IsChecked = section == "test";
        HistoryNav.IsChecked = section == "history";
        ManageNav.IsChecked = section == "manage";
        foreach (var tab in SubTabBar.Children.OfType<RadioButton>())
        {
            var tag = (string)tab.Tag;
            tab.Visibility = SectionOf(tag) == section ? Visibility.Visible : Visibility.Collapsed;
            tab.IsChecked = tag == page || (page == "review" && tag == "cases");
        }
        _selectingPage = false;
        SectionHeader.Visibility = section is "test" or "home" ? Visibility.Collapsed : Visibility.Visible;
        SectionTitleText.Text = section == "history" ? "기록" : "관리";

        HomePage.Visibility = page == "home" ? Visibility.Visible : Visibility.Collapsed;
        ExecutionScroll.Visibility = page == "run" ? Visibility.Visible : Visibility.Collapsed;
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
        if (page is "home" or "run" or "reports") RefreshHistory();
        RefreshHeader();
        if (page != _currentPage)
        {
            _currentPage = page;
            TitleBar.Section = PageNameFor(page);
            // 시험 실행 중이 아니면 이전 화면의 안내가 남지 않도록 지금 화면에 맞는 안내로 바꾼다
            if (!_isRunning) ShowStatus(PageHintFor(page), false);
        }
    }

    // 지금 보이는 화면. 같은 화면으로 다시 이동할 때는 상태 안내를 덮어쓰지 않는다
    private string? _currentPage;

    /// <summary>제목 표시줄 "정보지킴 QA / 화면 이름"에 쓸 화면 이름.</summary>
    private static string PageNameFor(string page) => page switch
    {
        "home" => "홈",
        "run" => "시험 결과",
        "cases" => "관리 / 시험 문자",
        "ai" => "관리 / AI 문자",
        "review" => "관리 / 예상 정답 검토",
        "team" => "기록 / 팀 공유",
        "reports" => "기록 / 내 기록",
        "settings" => "관리 / 설정",
        _ => "QA 작업 공간",
    };

    /// <summary>화면을 옮겼을 때 아래 상태 표시줄에 보여 줄 한 줄 안내.</summary>
    private string PageHintFor(string page) => page switch
    {
        "home" => "‘다음 할 일’을 따라가거나 시험 카드를 골라 주세요.",
        "run" => ResultsSection.Visibility == Visibility.Visible
            ? "결과를 골라 예상과 실제 값을 비교해 보세요." : "‘시험 고르기’를 누르면 오른쪽에서 조건을 확인할 수 있어요.",
        "cases" => "시험에 쓸 문자와 예상 정답을 확인할 수 있어요.",
        "ai" => "AI 연결은 준비 중이에요. 예제 문자로 먼저 연습할 수 있어요.",
        "review" => "문자를 고르면 예상 정답과 판정 근거를 볼 수 있어요.",
        "team" => "공유된 기록을 고르면 오른쪽에 사례별 결과가 보여요.",
        "reports" => "이 PC에 저장한 기록이에요. 하나를 고르면 그때 결과를 다시 열어요.",
        "settings" => "바꾼 값은 지금 켜 둔 앱에서만 적용돼요.",
        _ => "",
    };

    private void Navigation_Checked(object sender, RoutedEventArgs e)
    {
        if (_selectingPage || sender is not RadioButton { Tag: string page }) return;
        SelectPage(page);
    }

    private void NewRun_Click(object sender, RoutedEventArgs e) => SelectPage("run");
    private void OpenHistory_Click(object sender, RoutedEventArgs e) => SelectPage("reports");

    public bool NavigateFromGuide(string page)
    {
        if (_isRunning || CollaborationPage.IsBusy) { ShowStatus("시험이 끝나면 이동할 수 있어요.", false); return false; }
        if (page == "verdict") { SelectPage("home"); OpenConditionSheet(TestSuite.Verdict); }
        else SelectPage(page);
        return true;
    }
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
        if (ReferenceEquals(sender, ApiUrlBox) && ApiUrlBox.Text.Trim().Length > 0) ApiTargetRadio.IsChecked = true;
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
        HomeSuiteCards.IsEnabled = NextStepButton.IsEnabled = idle;
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
            HistoryGrid.ItemsSource = rows;
            RecentRunsGrid.ItemsSource = rows.Take(3).ToArray();
            HistoryEmptyText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowHistorySummary(runs, rows);
            ShowHomeSummary(runs);
            OpenDashboardButton.IsEnabled = File.Exists(DashboardPath);
        }
        catch (Exception exception) when (QaRunService.IsPreparationError(exception))
        { ShowStatus($"저장한 기록을 읽지 못했어요. {exception.Message}", true); }
    }

    private void RecentRun_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (((DataGrid)sender).SelectedItem is not HistoryRow row || _isRunning || CollaborationPage.IsBusy) return;
        CompleteStep(ChecklistStepKind.ReopenHistory);
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

    /// <summary>실행 기록 화면의 요약 숫자와 시작하기 카드의 "최근 결과" 한 줄을 채운다.</summary>
    private void ShowHistorySummary(IReadOnlyList<TestRun> runs, HistoryRow[] rows)
    {
        HistoryCountText.Text = runs.Count.ToString();
        var scored = runs.Where(run => run.TotalCount > 0).ToArray();
        HistoryAverageText.Text = scored.Length == 0 ? "—" : $"{scored.Average(run => run.PassRate):0}%";
        HistoryLatestText.Text = rows.Length == 0 ? "—" : $"{rows[0].SuiteName} {rows[0].CountText.Replace(" ", "")}";
        // 시험 종류마다 가장 최근 기록을 카드 아래에 보여 준다
        foreach (var (suite, label) in new[] { (TestSuite.Verdict, PresetVerdictLast), (TestSuite.Pipeline, PresetPipelineLast),
                     (TestSuite.Load, PresetLoadLast), (TestSuite.Recovery, PresetRecoveryLast), (TestSuite.SelfCheck, PresetSelfCheckLast) })
        {
            var last = rows.FirstOrDefault(row => row.SuiteName == suite.ToKoreanName());
            label.Text = last is null ? "아직 실행 전" : $"최근 {last.CountText.Replace(" ", "")} 통과";
            label.Foreground = (Brush)FindResource(last is null ? "MutedBrush"
                : last.Run.FailedCount == 0 ? "PassBrush" : "NoticeBrush");
        }
    }

    private sealed record HistoryRow(TestRun Run)
    {
        public string RunId => Run.RunId;
        public string SuiteName => Run.SuiteName;
        public string AreaName => Run.AreaName;
        public string StartedAtText => Run.StartedAt.ToLocalTime().ToString("MM-dd HH:mm:ss");
        public string CountText => $"{Run.PassedCount} / {Run.TotalCount}";
        public string TargetText => DescribeTarget(Run);
        public string LabelText => Run.HasProvisionalLabels ? "정답 검토 전" : "사례 파일 기준";
        /// <summary>홈 최근 실행의 점 색: 모두 통과하면 초록, 아니면 주황</summary>
        public bool IsAllPassed => Run.FailedCount == 0;
    }

    /// <summary>상태 표시줄의 색. 평소에는 바탕 없이 회색 글자, 시험이 끝나거나 오류가 나면 색 띠로 바뀐다.</summary>
    private enum StatusTone { Normal, Pass, Notice, Fail }

    private void ShowStatus(string message, bool isError) => ShowStatus(message, isError ? StatusTone.Fail : StatusTone.Normal);

    /// <summary>
    /// 상태 표시줄에 안내를 쓰고 색을 바꾼다.
    /// </summary>
    /// <param name="message">보여 줄 안내 문장</param>
    /// <param name="tone">띠 색: 통과(초록) · 다시 볼 항목 있음(주황) · 오류(빨강)</param>
    /// <param name="showReportActions">보고서 열기·폴더 열기 버튼을 함께 보여 줄지</param>
    private void ShowStatus(string message, StatusTone tone, bool showReportActions = false)
    {
        StatusText.Text = message;
        var (inkKey, backgroundKey, symbol) = tone switch
        {
            StatusTone.Pass => ("PassBrush", "PassBackgroundBrush", Wpf.Ui.Controls.SymbolRegular.Checkmark24),
            StatusTone.Notice => ("NoticeBrush", "NoticeBackgroundBrush", Wpf.Ui.Controls.SymbolRegular.Checkmark24),
            StatusTone.Fail => ("FailBrush", "FailBackgroundBrush", Wpf.Ui.Controls.SymbolRegular.ErrorCircle24),
            _ => ("MutedBrush", "", Wpf.Ui.Controls.SymbolRegular.Checkmark24),
        };
        var ink = (Brush)FindResource(inkKey);
        var colored = tone != StatusTone.Normal;
        StatusText.Foreground = ink;
        StatusText.FontWeight = colored ? FontWeights.SemiBold : FontWeights.Normal;
        StatusText.FontSize = colored ? 13 : 12;
        StatusBarBorder.Background = colored ? (Brush)FindResource(backgroundKey) : Brushes.Transparent;
        StatusBarBorder.BorderBrush = colored ? ink : Brushes.Transparent;
        StatusIconBadge.Visibility = colored ? Visibility.Visible : Visibility.Collapsed;
        StatusIconBadge.Background = ink;
        StatusIcon.Symbol = symbol;
        StatusActions.Visibility = showReportActions ? Visibility.Visible : Visibility.Collapsed;
    }

    // 방금 저장한 보고서가 있는 폴더를 탐색기로 연다
    private void OpenReportFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = _lastHtmlReportPath is not null ? Path.GetDirectoryName(_lastHtmlReportPath) : Path.GetDirectoryName(DashboardPath);
        if (folder is not null && Directory.Exists(folder)) OpenLocalPath(folder);
        else ShowStatus("아직 보고서 폴더가 없어요. 시험을 한 번 실행하면 만들어져요.", isError: true);
    }
}
