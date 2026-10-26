using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QaRunner.Measurement;
using QaRunner.Models;
using QaRunner.Reports;

namespace QaRunner.Desktop;

/// <summary>
/// QA 러너 메인 창. 왼쪽에서 시험 종류와 경로를 고르고 실행하면,
/// 오른쪽에 요약 숫자와 결과 표를 보여 주고 보고서를 저장한다.
/// 실제 실행 절차는 콘솔 프로그램과 같은 QaRunService를 쓴다.
/// </summary>
public partial class MainWindow : Window
{
    // 마지막으로 저장한 HTML 보고서 경로. "HTML 보고서 열기" 버튼에서 쓴다
    private string? _lastHtmlReportPath;

    public MainWindow()
    {
        InitializeComponent();
        ApplySuiteDefaults();

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
        try
        {
            var options = ReadOptionsFromScreen();
            SetRunning(true, options);

            var preparedRun = QaRunService.Prepare(options);
            var testRun = await QaRunService.RunAsync(preparedRun, options.ReportDirectory);
            var savedReports = QaRunService.SaveReports(testRun, options.ReportDirectory);

            ShowTestRun(testRun);
            _lastHtmlReportPath = Path.GetFullPath(savedReports.HtmlReportPath);
            OpenDashboardButton.IsEnabled = true;
            ShowStatus($"보고서를 저장하고 대시보드를 새로 만들었습니다.\n{_lastHtmlReportPath}", isError: false);
        }
        catch (Exception exception) when (QaRunService.IsPreparationError(exception))
        {
            ShowStatus($"실행 준비 중 오류: {exception.Message}", isError: true);
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
    public void SaveScreenshot(string pngFilePath)
    {
        UpdateLayout();
        var windowContent = (FrameworkElement)Content;

        // 창 내용의 바깥 여백(Margin)까지 포함한 크기로 찍는다
        var margin = windowContent.Margin;
        var imageBounds = new Rect(0, 0,
            windowContent.ActualWidth + margin.Left + margin.Right,
            windowContent.ActualHeight + margin.Top + margin.Bottom);
        var contentBounds = new Rect(margin.Left, margin.Top, windowContent.ActualWidth, windowContent.ActualHeight);

        const double scale = 2;
        var bitmap = new RenderTargetBitmap(
            (int)(imageBounds.Width * scale), (int)(imageBounds.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);

        // 배경이 투명하게 찍히지 않도록 흰 바탕 위에 창 내용을 그린다
        var drawing = new DrawingVisual();
        using (var drawingContext = drawing.RenderOpen())
        {
            drawingContext.DrawRectangle(Brushes.White, null, imageBounds);
            drawingContext.DrawRectangle(new VisualBrush(windowContent), null, contentBounds);
        }
        bitmap.Render(drawing);

        var pngEncoder = new PngBitmapEncoder();
        pngEncoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var pngFile = File.Create(pngFilePath);
        pngEncoder.Save(pngFile);
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e) => await RunSelectedSuiteAsync();

    // 저장한 HTML 보고서를 기본 브라우저로 연다
    private void OpenReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastHtmlReportPath is not null)
            Process.Start(new ProcessStartInfo(_lastHtmlReportPath) { UseShellExecute = true });
    }

    // 지금까지의 실행 기록을 모은 대시보드를 기본 브라우저로 연다
    private void OpenDashboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(DashboardPath))
            Process.Start(new ProcessStartInfo(DashboardPath) { UseShellExecute = true });
        else
            ShowStatus("대시보드가 아직 없습니다. 시험을 한 번 실행하면 만들어집니다.", isError: true);
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
        CasesPathLabel.Text = suite == TestSuite.Pipeline ? "시나리오 폴더" : "입력값 파일";
        CasesPathBox.Text = CommandLineOptions.DefaultCasesPathFor(suite);
        CasesPathBox.IsEnabled = suite != TestSuite.Recovery;

        // API 주소: 판정 시험과 부하 시험(API 부하)에서만 쓴다
        var usesApi = suite is TestSuite.Verdict or TestSuite.Load;
        ApiUrlBox.IsEnabled = usesApi;
        ApiUrlHint.Text = usesApi
            ? "비워 두면 가짜 판정 대상으로 실행합니다."
            : "이 시험은 지금 가짜 파이프라인으로 실행합니다.";

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
                ? "시연용으로 짧게 잡았습니다. 실행안 기준은 120초 동안 보내며 30초 중단입니다."
                : "시연용으로 짧게 잡았습니다. 실행안 기준은 300초(초당 5건, 1,500건)입니다.";
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

        MeasurementSettings? measurement = null;
        if (suite.IsMeasurementSuite())
        {
            measurement = new MeasurementSettings(
                DurationSeconds: ReadInt(DurationBox, "시간(초)"),
                EventsPerSecond: ReadDouble(RateBox, "초당 건수"),
                StopSeconds: suite == TestSuite.Recovery ? ReadInt(StopSecondsBox, "중단(초)") : 0);
            measurement.Validate(suite);
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
            : throw new ArgumentException($"{fieldName} 칸에는 정수를 넣어 주세요: {textBox.Text}");

    private static double ReadDouble(TextBox textBox, string fieldName) =>
        double.TryParse(textBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new ArgumentException($"{fieldName} 칸에는 숫자를 넣어 주세요: {textBox.Text}");

    // 실행 중에는 버튼을 막아 두 번 눌리지 않게 한다. 부하·복구 시험은 얼마나 걸리는지도 알려 준다
    private void SetRunning(bool isRunning, CommandLineOptions? options = null)
    {
        RunButton.IsEnabled = !isRunning;
        RunButton.Content = isRunning ? "실행 중..." : "실행";
        if (!isRunning)
            return;

        var runningMessage = options?.Measurement is { } measurement
            ? $"시험을 실행하고 있습니다. 약 {measurement.DurationSeconds}초 걸립니다."
            : "시험을 실행하고 있습니다.";
        ShowStatus(runningMessage, isError: false);
    }

    // 실행 기록을 요약 숫자와 결과 표로 보여 준다
    private void ShowTestRun(TestRun testRun)
    {
        RunInfoText.Text = $"{testRun.SuiteName} · 실행 번호 {testRun.RunId} · 시험 대상 {testRun.TargetName} · {testRun.Duration.TotalSeconds:0.00}초";
        FakeTargetNotice.Visibility = testRun.UsedFakeTarget ? Visibility.Visible : Visibility.Collapsed;

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
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)FindResource(isError ? "FailBrush" : "MutedBrush");
    }
}
