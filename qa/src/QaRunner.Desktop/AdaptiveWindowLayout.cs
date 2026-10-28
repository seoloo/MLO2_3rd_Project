using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace QaRunner.Desktop;

/// <summary>
/// 화면의 실제 작업 영역과 DPI에 맞춰 창을 배치하고 본문만 비례 축소한다.
/// 제목 표시줄과 상태 표시줄은 배율 대상에서 제외해 창 조작 버튼을 유지한다.
/// </summary>
internal sealed class AdaptiveWindowLayout
{
    private readonly Window _window;
    private readonly FrameworkElement _viewport;
    private readonly TextBlock _status;
    private readonly ScaleTransform _scale;
    private readonly bool _fitToScreen;
    private readonly Size _preferredSize;
    private HwndSource? _source;
    private nint _monitor;
    private bool _adjusting;
    private bool _refreshQueued;
    private bool _closed;
    private bool _movingOrSizing;

    public double CurrentScale => _scale.ScaleX;

    public AdaptiveWindowLayout(Window window, FrameworkElement viewport, FrameworkElement content,
        TextBlock status, bool fitToScreen)
    {
        _window = window;
        _viewport = viewport;
        _status = status;
        _fitToScreen = fitToScreen;
        _preferredSize = new Size(window.Width, window.Height);
        _scale = new ScaleTransform(1, 1);
        content.LayoutTransform = _scale;
        if (fitToScreen) window.WindowStartupLocation = WindowStartupLocation.Manual;
        viewport.SizeChanged += Viewport_SizeChanged;
        window.SourceInitialized += Window_SourceInitialized;
        window.LocationChanged += Window_LocationChanged;
        window.Closed += Window_Closed;
    }

    // 작은 창에서는 75%보다 작게 글자를 줄이지 않고 기존 스크롤을 사용한다.
    internal static double ScaleFor(Size viewport) => Math.Round(Math.Clamp(
        Math.Min(viewport.Width / 1280, viewport.Height / 690), 0.75, 1), 2);

    internal static Rect FitBounds(Rect workArea, Size preferredDip, DpiScale dpi)
    {
        var gapX = Math.Min(16 * dpi.DpiScaleX, workArea.Width / 10);
        var gapY = Math.Min(16 * dpi.DpiScaleY, workArea.Height / 10);
        var width = Math.Min(preferredDip.Width * dpi.DpiScaleX, workArea.Width - 2 * gapX);
        var height = Math.Min(preferredDip.Height * dpi.DpiScaleY, workArea.Height - 2 * gapY);
        return new Rect(workArea.Left + (workArea.Width - width) / 2,
            workArea.Top + (workArea.Height - height) / 2, width, height);
    }

    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
        var scale = ScaleFor(e.NewSize);
        if (Math.Abs(_scale.ScaleX - scale) > 0.001) _scale.ScaleX = _scale.ScaleY = scale;
        _status.Text = $"자동 배율 {scale:P0}";
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(_window).Handle);
        _source?.AddHook(WindowMessage);
        _monitor = MonitorFromWindow(_source?.Handle ?? 0, 2);
        if (_fitToScreen) FitWindow(initial: true);
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (!_fitToScreen || _source is null || _adjusting) return;
        var monitor = MonitorFromWindow(_source.Handle, 2);
        if (monitor != _monitor) { _monitor = monitor; if (!_movingOrSizing) QueueFit(); }
    }

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // WPF의 DPI 처리가 끝난 다음 최신 배율과 작업 영역을 읽는다.
        if (message == 0x0231) _movingOrSizing = true;
        if (message == 0x0232) { _movingOrSizing = false; if (_fitToScreen) QueueFit(); }
        if (_fitToScreen && message is 0x02E0 or 0x007E or 0x001A) QueueFit();
        return 0;
    }

    private void QueueFit()
    {
        if (_closed || _refreshQueued) return;
        _refreshQueued = true;
        _window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _refreshQueued = false;
            if (!_closed) FitWindow(initial: false);
        }));
    }

    private void FitWindow(bool initial)
    {
        if (_source is null || _adjusting || _movingOrSizing) return;
        var handle = _source.Handle;
        _monitor = MonitorFromWindow(handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(_monitor, ref info)) return;
        var work = info.Work.ToRect();
        var dpi = VisualTreeHelper.GetDpi(_window);
        var limit = FitBounds(work, _preferredSize, dpi);
        _adjusting = true;
        try
        {
            // 최소 창 크기도 실제 화면보다 크지 않게 제한한다.
            _window.MinWidth = Math.Min(840, (work.Width - Math.Min(32 * dpi.DpiScaleX, work.Width / 5)) / dpi.DpiScaleX);
            _window.MinHeight = Math.Min(500, (work.Height - Math.Min(32 * dpi.DpiScaleY, work.Height / 5)) / dpi.DpiScaleY);
            if (_window.WindowState != WindowState.Normal) return;
            if (!initial && GetWindowRect(handle, out var current))
            {
                var width = Math.Min(current.Right - current.Left, work.Width - Math.Min(32 * dpi.DpiScaleX, work.Width / 5));
                var height = Math.Min(current.Bottom - current.Top, work.Height - Math.Min(32 * dpi.DpiScaleY, work.Height / 5));
                // 다른 모니터로 이동하면 위치만 작업 영역 안으로 보정하고 사용자 크기를 존중한다.
                var left = Math.Clamp((double)current.Left, work.Left, work.Right - width);
                var top = Math.Clamp((double)current.Top, work.Top, work.Bottom - height);
                limit = new Rect(left, top, width, height);
            }
            SetWindowPos(handle, 0, (int)Math.Round(limit.Left), (int)Math.Round(limit.Top),
                (int)Math.Floor(limit.Width), (int)Math.Floor(limit.Height), 0x0014);
        }
        finally { _adjusting = false; }
    }

    // 시작 창이 작업 표시줄을 제외한 현재 모니터 영역 안에 있는지 UI 점검에서 확인한다.
    internal bool IsWithinWorkArea()
    {
        if (_source is null || !GetWindowRect(_source.Handle, out var bounds)) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(MonitorFromWindow(_source.Handle, 2), ref info)
            && info.Work.ToRect().Contains(bounds.ToRect());
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _closed = true;
        _viewport.SizeChanged -= Viewport_SizeChanged;
        _window.SourceInitialized -= Window_SourceInitialized;
        _window.LocationChanged -= Window_LocationChanged;
        _window.Closed -= Window_Closed;
        if (_source is { IsDisposed: false }) _source.RemoveHook(WindowMessage);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint window, nint after,
        int x, int y, int width, int height, uint flags);
}
