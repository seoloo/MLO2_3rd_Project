using System.Windows;
using System.Windows.Controls;

namespace QaRunner.Desktop;

public partial class HelpWindow : Window
{
    private readonly MainWindow? _main;
    private readonly AdaptiveWindowLayout _layout;
    public HelpWindow(MainWindow? main = null, bool fitToScreen = true)
    {
        InitializeComponent();
        _main = main;
        _layout = new AdaptiveWindowLayout(this, GuideViewport, GuideRoot, GuideZoom, fitToScreen);
    }
    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (_main is null) return;
        if (_main.NavigateFromGuide((string)((Button)sender).Tag)) Close();
    }
}
