using System.Windows;
using System.Windows.Threading;
using System.IO;
using QaRunner.Models;

namespace QaRunner.Desktop;

/// <summary>
/// 데스크탑 앱의 시작점.
/// 보통은 창을 띄우기만 하고, --capture 옵션을 주면 화면을 PNG로 저장하고 바로 끝낸다.
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs startupEventArgs)
    {
        base.OnStartup(startupEventArgs);

        // 실행 파일이 bin 폴더에 있어도 cases 폴더를 찾을 수 있도록 qa 폴더를 기준 폴더로 삼는다
        QaFolderLocator.UseQaFolderAsCurrentDirectory();

        var captureFilePath = FindOptionValue(startupEventArgs.Args, "--capture");
        var smokeDirectory = FindOptionValue(startupEventArgs.Args, "--ui-smoke");
        var mainWindow = new MainWindow(fitToScreen: captureFilePath is null && smokeDirectory is null);

        if (captureFilePath is null && smokeDirectory is null)
        {
            mainWindow.Show();
            return;
        }

        // 화면 캡처 모드: README나 발표 자료에 넣을 화면을 만든다.
        // 사용자 화면에 보이지 않게 창을 화면 밖에 띄우고, 시험을 한 번 돌린 뒤 그대로 찍어서 저장한다.
        //   예: QaRunner.Desktop.exe --capture screen.png --suite load
        mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        mainWindow.Left = -20000;
        mainWindow.Top = -20000;
        mainWindow.ShowActivated = false;
        mainWindow.ShowInTaskbar = false;
        mainWindow.Show();

        if (smokeDirectory is not null)
        {
            try
            {
                await mainWindow.RunUiSmokeAsync(smokeDirectory);
                Shutdown();
            }
            catch (Exception exception)
            {
                Directory.CreateDirectory(smokeDirectory);
                File.WriteAllText(Path.Combine(smokeDirectory, "ui-smoke-error.txt"), exception.ToString());
                Shutdown(1);
            }
            return;
        }

        // --suite로 시험 종류를 고른다 (verdict, pipeline, load, recovery). 부하·복구는 시연용 기본 시간으로 돈다
        mainWindow.SelectSuite(TestSuiteExtensions.ParseOptionValue(FindOptionValue(startupEventArgs.Args, "--suite")));

        await mainWindow.RunSelectedSuiteAsync();

        // 결과 표가 화면에 다 그려질 때까지 한 번 기다린 뒤 찍는다
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        mainWindow.SelectPage(FindOptionValue(startupEventArgs.Args, "--page") ?? "run");
        mainWindow.SaveScreenshot(captureFilePath!);
        Shutdown();
    }

    // "--capture a.png"처럼 옵션 이름 바로 뒤에 오는 값을 찾는다. 없으면 null
    private static string? FindOptionValue(string[] args, string optionName)
    {
        var optionIndex = Array.IndexOf(args, optionName);
        var hasValue = optionIndex >= 0 && optionIndex + 1 < args.Length;
        return hasValue ? args[optionIndex + 1] : null;
    }
}
