using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WhisperTray.Core.Audio;
using static WhisperTray.App.Adapters.NativeMethods;
using WinFormsScreen = System.Windows.Forms.Screen;
using WpfBrush = System.Windows.Media.Brush;
using WpfPoint = System.Windows.Point;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace WhisperTray.App.Views;

/// <summary>
/// Floating recording HUD: a small topmost panel that never takes focus, never appears
/// in the taskbar or Alt+Tab, and lets every mouse click fall through to the window
/// below. Pure view — <see cref="RecordingOverlayController"/> decides what it shows
/// and when. All members must be called on the dispatcher thread.
/// </summary>
public partial class RecordingOverlayWindow : Window
{
    public const int BarCount = 32;

    private const double MinBarHeight = 3;
    private const double BottomMargin = 24;

    private readonly WpfRectangle[] _bars = new WpfRectangle[BarCount];
    private readonly Storyboard _blink;
    private readonly Storyboard _spin;

    public RecordingOverlayWindow()
    {
        InitializeComponent();

        var barBrush = (WpfBrush)FindResource("RecordBrush");
        for (var i = 0; i < BarCount; i++)
        {
            var bar = new WpfRectangle
            {
                Fill = barBrush,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Margin = new Thickness(1.5, 0, 1.5, 0),
                Height = MinBarHeight,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            _bars[i] = bar;
            LevelBars.Children.Add(bar);
        }

        _blink = (Storyboard)FindResource("BlinkStoryboard");
        _spin = (Storyboard)FindResource("SpinStoryboard");
        SourceInitialized += OnSourceInitialized;
    }

    public void ShowRecording(string hotkey)
    {
        HintText.Text = $"{hotkey} — завершить";
        ElapsedText.Text = RecordingClock.Format(TimeSpan.Zero);
        foreach (var bar in _bars)
        {
            bar.Height = MinBarHeight;
        }

        ProcessingPanel.Visibility = Visibility.Collapsed;
        RecordingPanel.Visibility = Visibility.Visible;
        _spin.Stop(this);
        _blink.Begin(this, isControllable: true);
    }

    public void ShowProcessing(string text)
    {
        ProcessingText.Text = text;
        RecordingPanel.Visibility = Visibility.Collapsed;
        ProcessingPanel.Visibility = Visibility.Visible;
        _blink.Stop(this);
        _spin.Begin(this, isControllable: true);
    }

    public void UpdateRecording(string elapsed, ReadOnlySpan<float> levels)
    {
        if (!string.Equals(ElapsedText.Text, elapsed, StringComparison.Ordinal))
        {
            ElapsedText.Text = elapsed;
        }

        var maxHeight = LevelBars.ActualHeight > 0 ? LevelBars.ActualHeight : LevelBars.Height;
        var count = Math.Min(levels.Length, _bars.Length);
        for (var i = 0; i < count; i++)
        {
            _bars[i].Height = Math.Max(MinBarHeight, levels[i] * maxHeight);
        }
    }

    /// <summary>
    /// Moves the panel to the bottom centre of the work area of the monitor that holds
    /// <paramref name="anchorWindow"/> (the dictation target), or of the primary monitor.
    /// Requires the window handle to exist so device pixels can be converted to DIPs.
    /// </summary>
    public void PlaceOnMonitorOf(nint anchorWindow)
    {
        var screen = anchorWindow != 0 ? WinFormsScreen.FromHandle(anchorWindow) : WinFormsScreen.PrimaryScreen;
        if (screen is null)
        {
            return;
        }

        var work = screen.WorkingArea;
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = fromDevice.Transform(new WpfPoint(work.Left, work.Top));
        var bottomRight = fromDevice.Transform(new WpfPoint(work.Right, work.Bottom));

        Left = topLeft.X + ((bottomRight.X - topLeft.X - Width) / 2);
        Top = bottomRight.Y - Height - BottomMargin;
    }

    public void HideOverlay()
    {
        _blink.Stop(this);
        _spin.Stop(this);
        Hide();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // NOACTIVATE keeps focus in the dictation target, TOOLWINDOW hides the panel from
        // Alt+Tab, TRANSPARENT passes mouse input through to whatever lies underneath.
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        _ = SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
    }
}
