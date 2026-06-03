using System;
using System.ComponentModel;
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
        if (Vm is null)
            return;

        if (_playTimer.IsEnabled)
        {
            _playTimer.Stop();
            Vm.IsPlaying = false;
            PlayButton.Content = "▶ Play";
        }
        else
        {
            _playTimer.Start();
            Vm.IsPlaying = true;
            PlayButton.Content = "⏸ Stop";
        }
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

    // ---- alignment crosshair (drag to set the source content centre) ----

    private MainWindowViewModel? _guideVm;

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
            // The source image re-lays-out when shown; reposition after layout settles.
            Dispatcher.UIThread.Post(PositionCrosshair, DispatcherPriority.Background);
        }
    }

    private void OnGuidePressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is null || !Vm.ShowCenterGuide) return;
        e.Pointer.Capture(GuideCanvas);
        SetCenterFromPointer(e.GetPosition(GuideCanvas));
        e.Handled = true;
    }

    private void OnGuideMoved(object? sender, PointerEventArgs e)
    {
        if (Vm is null || !Vm.ShowCenterGuide) return;
        if (!e.GetCurrentPoint(GuideCanvas).Properties.IsLeftButtonPressed) return;
        SetCenterFromPointer(e.GetPosition(GuideCanvas));
    }

    private void OnGuideReleased(object? sender, PointerReleasedEventArgs e) => e.Pointer.Capture(null);

    private void SetCenterFromPointer(Point p)
    {
        var rect = LetterboxRect();
        if (rect is null || Vm is null) return;
        var (left, top, w, h) = rect.Value;
        if (w <= 0 || h <= 0) return;
        Vm.SourceCenterX = Math.Clamp((p.X - left) / w, 0, 1);
        Vm.SourceCenterY = Math.Clamp((p.Y - top) / h, 0, 1);
    }

    private void PositionCrosshair()
    {
        if (Vm is null || !Vm.ShowCenterGuide) return;
        var rect = LetterboxRect();
        if (rect is null) return;
        var (left, top, w, h) = rect.Value;
        double cx = left + Vm.SourceCenterX * w;
        double cy = top + Vm.SourceCenterY * h;

        CrossH.Width = w;
        Canvas.SetLeft(CrossH, left);
        Canvas.SetTop(CrossH, cy - CrossH.Height / 2);

        CrossV.Height = h;
        Canvas.SetLeft(CrossV, cx - CrossV.Width / 2);
        Canvas.SetTop(CrossV, top);

        Canvas.SetLeft(CrossRing, cx - CrossRing.Width / 2);
        Canvas.SetTop(CrossRing, cy - CrossRing.Height / 2);
    }

    // The displayed (letterboxed) rect of the Uniform-fit source within the overlay,
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
}
