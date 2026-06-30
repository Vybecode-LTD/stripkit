using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using StripKit.ViewModels;

namespace StripKit.Views;

public partial class MainWindow : Window
{
    // Auto-play is a purely view-side animation concern: it nudges PreviewValue
    // on a timer so the control appears to sweep. Keeping it out of the view
    // model preserves the view model's testability.
    private readonly DispatcherTimer _playTimer;
    private double _direction = 1.0;

    // House accent — highlights the preview border while a file is dragged over it.
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#FFE8440A"));

    // Extensions SkiaSharp can decode — mirrors the "Load source image…" file picker.
    private static readonly string[] AcceptedExtensions = [".png", ".webp", ".bmp", ".jpg", ".jpeg"];

    public MainWindow()
    {
        InitializeComponent();
        _playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _playTimer.Tick += OnPlayTick;
        Closed += (_, _) => _playTimer.Stop();

        // File drag-and-drop onto the preview only. Handlers are scoped to
        // PreviewBorder (which has AllowDrop set in XAML) so they don't collide with
        // the Import tab's own drop zone. DragOver MUST set DragEffects or Drop never
        // fires. Handlers only extract a path and delegate to the view model.
        PreviewBorder.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        PreviewBorder.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        PreviewBorder.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        PreviewBorder.AddHandler(DragDrop.DropEvent, OnDrop);

        // Alignment crosshair: drag over the source to set its content centre.
        GuideCanvas.PointerPressed += OnGuidePressed;
        GuideCanvas.PointerMoved += OnGuideMoved;
        GuideCanvas.PointerReleased += OnGuideReleased;
        GuideCanvas.SizeChanged += (_, _) => PositionCrosshair();
        DataContextChanged += OnDataContextChangedForGuide;
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private void OnPlayClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (_playTimer.IsEnabled) StopPlay();
        else
        {
            _playTimer.Start();
            Vm.IsPlaying = true;
            PlayIcon.Text = "";   // pause glyph
            PlayLabel.Text = "Stop";
        }
    }

    private void StopPlay()
    {
        if (!_playTimer.IsEnabled) return;
        _playTimer.Stop();
        if (Vm is not null) Vm.IsPlaying = false;
        PlayIcon.Text = "";   // play glyph
        PlayLabel.Text = "Play";
    }

    private void OnStepBack(object? sender, RoutedEventArgs e) { StopPlay(); StepPreview(-1); }
    private void OnStepForward(object? sender, RoutedEventArgs e) { StopPlay(); StepPreview(1); }

    private void OnResetPreview(object? sender, RoutedEventArgs e)
    {
        StopPlay();
        if (Vm is not null) Vm.PreviewValue = 0.5; // the centred, un-rotated start view
    }

    // Step the preview by one EXPORT frame. The preview renders continuously, but the step
    // buttons snap to the real output frames so you can inspect them one at a time.
    private void StepPreview(int delta)
    {
        if (Vm is null) return;
        int n = Math.Max(2, Vm.FrameCount);
        int cur = (int)Math.Round(Vm.PreviewValue * (n - 1));
        int next = Math.Clamp(cur + delta, 0, n - 1);
        Vm.PreviewValue = (double)next / (n - 1);
    }

    // Open an About-box link (URL in the element's Tag) in the default browser.
    private void OnAboutLinkTapped(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: string url } && !string.IsNullOrWhiteSpace(url))
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* couldn't open the browser — ignore */ }
        }
    }

    // Copy the currently-previewed loader snippet to the clipboard. Clipboard access is a
    // view (top-level) concern, so it lives here rather than in the view model.
    private async void OnCopyCode(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || string.IsNullOrEmpty(Vm.GeneratedCode)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(Vm.GeneratedCode);
        Vm.StatusMessage = $"Copied the {Vm.CodePreviewTarget} snippet to the clipboard.";
    }

    private void OnPlayTick(object? sender, EventArgs e)
    {
        if (Vm is null)
            return;

        double next = Vm.PreviewValue + 0.02 * _direction;
        if (next >= 1.0)
        {
            next = 1.0;
            _direction = -1.0;
        }
        else if (next <= 0.0)
        {
            next = 0.0;
            _direction = 1.0;
        }

        Vm.PreviewValue = next;
    }

    // ---- file drag-and-drop (see the avalonia-drag-drop-files skill) ----

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        OnDragOver(sender, e);
        if (e.Data.Contains(DataFormats.Files))
            PreviewBorder.BorderBrush = AccentBrush;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // Accept the drag only when it carries files; otherwise the cursor shows
        // "no entry" and, correctly, nothing drops.
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
        => PreviewBorder.BorderBrush = Brushes.Transparent;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        PreviewBorder.BorderBrush = Brushes.Transparent;
        if (Vm is null)
            return;

        var items = e.Data.GetFiles();
        if (items is null)
            return;

        // Load the first supported image; the tool works on one source at a time.
        foreach (var item in items)
        {
            var path = item.TryGetLocalPath();
            if (path is null)
                continue;
            if (!AcceptedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                continue;

            Vm.LoadSourceFromPath(path);
            e.Handled = true;
            break;
        }
    }

    // ---- alignment crosshair (drag to set the spin centre) ----

    private MainWindowViewModel? _guideVm;

    // Live-drag render coalescing: the crosshair follows the pointer instantly on every event,
    // but the (expensive) preview re-render is applied at most once per UI cycle — otherwise a
    // fast drag queues hundreds of renders and the UI falls seconds behind.
    private double _pendingX, _pendingY;
    private bool _hasPendingCenter, _centerRenderScheduled;

    private void OnDataContextChangedForGuide(object? sender, EventArgs e)
    {
        if (_guideVm is not null)
            _guideVm.PropertyChanged -= OnGuideVmPropertyChanged;
        _guideVm = Vm;
        if (_guideVm is not null)
            _guideVm.PropertyChanged += OnGuideVmPropertyChanged;
        PositionCrosshair();
    }

    private void OnGuideVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.SourceCenterX)
            or nameof(MainWindowViewModel.SourceCenterY)
            or nameof(MainWindowViewModel.ShowCenterGuide)
            or nameof(MainWindowViewModel.PreviewImage))
        {
            // Reposition after layout settles (the frame re-lays-out when it changes).
            Dispatcher.UIThread.Post(PositionCrosshair, DispatcherPriority.Background);
        }
    }

    private void OnGuidePressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is null || !Vm.ShowCenterGuide) return;
        e.Pointer.Capture(GuideCanvas);
        Vm.SetCrosshairPlacing(true);   // hold the art still while dragging
        SetCenterFromPointer(e.GetPosition(GuideCanvas));
        e.Handled = true;
    }

    private void OnGuideMoved(object? sender, PointerEventArgs e)
    {
        if (Vm is null || !Vm.ShowCenterGuide) return;
        if (!e.GetCurrentPoint(GuideCanvas).Properties.IsLeftButtonPressed) return;
        SetCenterFromPointer(e.GetPosition(GuideCanvas));
    }

    private void OnGuideReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        Vm?.SetCrosshairPlacing(false);   // release → orbit the chosen centre so playback shows it
    }

    // A pointer drag sets the spin centre (normalized within the drawn art). The crosshair
    // follows the pointer immediately (cheap); the preview re-render is coalesced to one per
    // UI cycle so a fast drag stays responsive instead of queuing a render per move.
    private void SetCenterFromPointer(Point p)
    {
        if (Vm is null) return;
        var art = ArtRectOnScreen();
        if (art is null) return;
        var (left, top, w, h) = art.Value;
        if (w <= 0 || h <= 0) return;
        double nx = Math.Clamp((p.X - left) / w, 0, 1);
        double ny = Math.Clamp((p.Y - top) / h, 0, 1);

        PlaceCrosshair(left + nx * w, top + ny * h);   // instant

        _pendingX = nx; _pendingY = ny; _hasPendingCenter = true;
        if (_centerRenderScheduled) return;
        _centerRenderScheduled = true;
        Dispatcher.UIThread.Post(ApplyPendingCenter, DispatcherPriority.Background);
    }

    private void ApplyPendingCenter()
    {
        _centerRenderScheduled = false;
        if (!_hasPendingCenter || Vm is null) return;
        _hasPendingCenter = false;
        Vm.SetSourceCenter(_pendingX, _pendingY);   // one batched preview render
    }

    private void PositionCrosshair()
    {
        if (Vm is null || !Vm.ShowCenterGuide) return;
        var art = ArtRectOnScreen();
        if (art is null) return;
        var (al, at, aw, ah) = art.Value;
        PlaceCrosshair(al + Vm.SourceCenterX * aw, at + Vm.SourceCenterY * ah);
    }

    // Position the crosshair (full-frame guide lines crossing at the ring) at a screen point.
    private void PlaceCrosshair(double cx, double cy)
    {
        var frame = LetterboxRect();
        if (frame is null) return;
        var (fl, ft, fW, fH) = frame.Value;

        CrossH.Width = fW;
        Canvas.SetLeft(CrossH, fl);
        Canvas.SetTop(CrossH, cy - CrossH.Height / 2);

        CrossV.Height = fH;
        Canvas.SetLeft(CrossV, cx - CrossV.Width / 2);
        Canvas.SetTop(CrossV, ft);

        Canvas.SetLeft(CrossRing, cx - CrossRing.Width / 2);
        Canvas.SetTop(CrossRing, cy - CrossRing.Height / 2);
    }

    // The displayed (letterboxed) rect of the rendered preview FRAME within the overlay,
    // in GuideCanvas coordinates — which share the preview Image's bounds and margin.
    private (double Left, double Top, double Width, double Height)? LetterboxRect()
    {
        if (PreviewImageControl.Source is not Bitmap bmp) return null;
        double cw = GuideCanvas.Bounds.Width, ch = GuideCanvas.Bounds.Height;
        double iw = bmp.PixelSize.Width, ih = bmp.PixelSize.Height;
        if (cw <= 0 || ch <= 0 || iw <= 0 || ih <= 0) return null;
        double s = Math.Min(cw / iw, ch / ih);
        double w = iw * s, h = ih * s;
        return ((cw - w) / 2, (ch - h) / 2, w, h);
    }

    // On-screen rect of the drawn ART (Contain-fit, rectangle-centred) within the rendered
    // frame — the renderer places the source this way, so the crosshair must map to it
    // (not the whole cell) for the mark to land on the knob.
    private (double Left, double Top, double Width, double Height)? ArtRectOnScreen()
    {
        if (Vm is null) return null;
        var frame = LetterboxRect();
        if (frame is null) return null;
        double fw = Vm.FrameWidth, fh = Vm.FrameHeight, sw = Vm.SourcePixelWidth, sh = Vm.SourcePixelHeight;
        if (fw <= 0 || fh <= 0 || sw <= 0 || sh <= 0) return null;
        double cs = Math.Min(fw / sw, fh / sh);     // "contain" scale, matching the renderer
        double drawW = sw * cs, drawH = sh * cs;
        double drawX = (fw - drawW) / 2, drawY = (fh - drawH) / 2;
        var (fl, ft, fW, fH) = frame.Value;
        double sx = fW / fw, sy = fH / fh;           // frame (1x) → screen
        return (fl + drawX * sx, ft + drawY * sy, drawW * sx, drawH * sy);
    }
}
