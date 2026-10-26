using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QaRunner.Models;

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
    }

    /// <summary>
    /// 시험 종류를 코드에서 고른다 (화면 캡처 모드에서 사용).
    /// </summary>
    public void SelectSuite(TestSuite testSuite)
    {
        VerdictSuiteRadio.IsChecked = testSuite == TestSuite.Verdict;
        PipelineSuiteRadio.IsChecked = testSuite == TestSuite.Pipeline;
    }

    /// <summary>
    /// 지금 화면에 고른 설정으로 시험을 돌리고 결과를 보여 준다.
    /// 준비 단계 오류(파일 없음, 잘못된 주소 등)는 창을 닫지 않고 상태 칸에 알린다.
    /// </summary>
    public async Task RunSelectedSuiteAsync()
    {
        var options = ReadOptionsFromScreen();
        SetRunning(true);

        try
        {
            var preparedRun = QaRunService.Prepare(options);
            var testRun = await QaRunService.RunAsync(preparedRun, options.ReportDirectory);
            var savedReports = QaRunService.SaveReports(testRun, options.ReportDirectory);

            ShowTestRun(testRun);
            _lastHtmlReportPath = Path.GetFullPath(savedReports.HtmlReportPath);
            ShowStatus($"보고서를 저장했습니다.\n{_lastHtmlReportPath}", isError: false);
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

    // 시험 종류를 바꾸면 입력 경로 기본값과 API 주소 칸 사용 여부를 바꾼다
    private void SuiteRadio_Checked(object sender, RoutedEventArgs e) => ApplySuiteDefaults();

    private TestSuite SelectedSuite => PipelineSuiteRadio.IsChecked == true ? TestSuite.Pipeline : TestSuite.Verdict;

    // 시험 종류에 맞게 입력 칸 이름·기본값을 바꾼다.
    // 창을 만드는 도중(InitializeComponent 안)에도 불리므로 아직 없는 칸이 있으면 건너뛴다
    private void ApplySuiteDefaults()
    {
        if (CasesPathBox is null || ApiUrlBox is null || CasesPathLabel is null || ApiUrlHint is null)
            return;

        var isPipelineSuite = SelectedSuite == TestSuite.Pipeline;
        CasesPathLabel.Text = isPipelineSuite ? "시나리오 폴더" : "입력값 파일";
        CasesPathBox.Text = isPipelineSuite ? CommandLineOptions.DefaultPipelineCasesPath : CommandLineOptions.DefaultVerdictCasesPath;

        // API 주소는 판정 시험에서만 쓴다
        ApiUrlBox.IsEnabled = !isPipelineSuite;
        ApiUrlHint.Text = isPipelineSuite
            ? "파이프라인 시험은 지금 가짜 파이프라인으로 실행합니다."
            : "비워 두면 가짜 판정 대상으로 실행합니다.";
    }

    // 화면의 입력 칸 값을 실행 옵션으로 모은다. 콘솔의 명령줄 옵션과 같은 모양을 쓴다
    private CommandLineOptions ReadOptionsFromScreen()
    {
        var apiUrl = ApiUrlBox.Text.Trim();
        var reportDirectory = ReportDirectoryBox.Text.Trim();

        return new CommandLineOptions(
            Suite: SelectedSuite,
            CasesPath: CasesPathBox.Text.Trim(),
            ReportDirectory: reportDirectory.Length > 0 ? reportDirectory : CommandLineOptions.DefaultReportDirectory,
            ApiBaseUrl: SelectedSuite == TestSuite.Verdict && apiUrl.Length > 0 ? apiUrl : null);
    }

    // 실행 중에는 버튼을 막아 두 번 눌리지 않게 한다
    private void SetRunning(bool isRunning)
    {
        RunButton.IsEnabled = !isRunning;
        RunButton.Content = isRunning ? "실행 중..." : "실행";
        if (isRunning)
            ShowStatus("시험을 실행하고 있습니다.", isError: false);
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
