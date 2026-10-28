using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QaRunner.Desktop;

/// <summary>앱을 시작할 때 압축한 배경 중 한 장만 디코딩한다.</summary>
internal static class DesktopBackgrounds
{
    private const int BackgroundCount = 20;

    internal static void ApplyRandom(Border hero)
    {
        var number = Random.Shared.Next(1, BackgroundCount + 1);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(
            $"pack://application:,,,/QaRunner.Desktop;component/Assets/Backgrounds/{number:00}.jpg");
        bitmap.EndInit();
        bitmap.Freeze();
        hero.Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(hero, BitmapScalingMode.HighQuality);
    }
}
