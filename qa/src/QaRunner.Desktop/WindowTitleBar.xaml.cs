using System.Windows;
using System.Windows.Controls;

namespace QaRunner.Desktop;

/// <summary>메인 창과 별도 팀 창에서 같은 Windows 창 조작을 제공한다.</summary>
public partial class WindowTitleBar : UserControl
{
    public WindowTitleBar() => InitializeComponent();
    public static readonly DependencyProperty ShowHelpProperty = DependencyProperty.Register(
        nameof(ShowHelp), typeof(bool), typeof(WindowTitleBar), new PropertyMetadata(true));
    public bool ShowHelp { get => (bool)GetValue(ShowHelpProperty); set => SetValue(ShowHelpProperty, value); }

    /// <summary>제목 옆 "/ 화면 이름"에 보여 줄 지금 화면 이름. 비우면 구분선째 숨긴다.</summary>
    public static readonly DependencyProperty SectionProperty = DependencyProperty.Register(
        nameof(Section), typeof(string), typeof(WindowTitleBar), new PropertyMetadata("QA 작업 공간"));
    public string Section { get => (string)GetValue(SectionProperty); set => SetValue(SectionProperty, value); }
    private void Help_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window) UsageHelp.Open(window);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window) SystemCommands.MinimizeWindow(window);
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not { } window) return;
        if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window);
        else SystemCommands.MaximizeWindow(window);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window) SystemCommands.CloseWindow(window);
    }
}
