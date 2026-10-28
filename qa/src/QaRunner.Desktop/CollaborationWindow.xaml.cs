using System.Windows;
using System.Windows.Controls;
namespace QaRunner.Desktop;
/// <summary>별도 창이 필요한 경우에도 메인과 같은 콘텐츠·공유 저장소를 재사용한다.</summary>
public partial class CollaborationWindow : Window
{
    internal readonly AdaptiveWindowLayout AdaptiveLayout;
    public CollaborationWindow(bool fitToScreen = true)
    {
        InitializeComponent();
        AdaptiveLayout = new AdaptiveWindowLayout(this, WorkspaceViewport, WorkspaceRoot, ZoomStatusText, fitToScreen);
        UsageHelp.Enable(this);
        View.NavigationRequested += page =>
        {
            foreach (var item in Navigation.Children.OfType<RadioButton>())
                item.IsChecked = (string)item.Tag == page;
        };
    }
    private void Page_Checked(object sender, RoutedEventArgs e)
    {
        if (View is not null && sender is RadioButton { Tag: string page }) View.ShowPage(page);
    }
}
