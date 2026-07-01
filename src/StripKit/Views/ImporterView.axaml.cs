using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using StripKit.ViewModels;

namespace StripKit.Views;

public partial class ImporterView : UserControl
{
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#FFE8440A"));
    private static readonly string[] AcceptedExtensions = [".png", ".webp", ".bmp", ".jpg", ".jpeg"];

    // Auto-play is a view-side animation concern (identical to the Create tab's transport): it steps
    // PreviewValue through the strip's frames on a timer, ping-ponging at the ends.
    private readonly DispatcherTimer _playTimer;
    private double _direction = 1.0;

    public ImporterView()
    {
        InitializeComponent();

        _playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _playTimer.Tick += OnPlayTick;
        DetachedFromVisualTree += (_, _) => StopPlay();

        // Scope the drop handlers to this control's drop border so they do not
        // collide with the Create tab's preview drop zone. DragOver must set
        // DragEffects or Drop never fires; handlers only extract a path and call
        // the view model. (See the avalonia-drag-drop-files skill.)
        ImportDropBorder.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        ImportDropBorder.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        ImportDropBorder.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        ImportDropBorder.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private ImporterViewModel? Vm => DataContext as ImporterViewModel;

    // ---- preview transport (identical to the Create tab) ----

    private void OnPlayClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || !Vm.HasStrip) return;
        if (_playTimer.IsEnabled) StopPlay();
        else
        {
            _playTimer.Start();
            PlayIcon.Text = "\uE769";   // pause glyph
            PlayLabel.Text = "Stop";
        }
    }

    private void StopPlay()
    {
        if (!_playTimer.IsEnabled) return;
        _playTimer.Stop();
        PlayIcon.Text = "\uE768";       // play glyph
        PlayLabel.Text = "Play";
    }

    private void OnStepBack(object? sender, RoutedEventArgs e) { StopPlay(); StepPreview(-1); }
    private void OnStepForward(object? sender, RoutedEventArgs e) { StopPlay(); StepPreview(1); }

    private void OnResetPreview(object? sender, RoutedEventArgs e)
    {
        StopPlay();
        if (Vm is not null) Vm.PreviewValue = 0.0;   // back to the first frame
    }

    private void StepPreview(int delta)
    {
        if (Vm is null) return;
        int n = Math.Max(2, Vm.FrameCount);
        int cur = (int)Math.Round(Vm.PreviewValue * (n - 1));
        int next = Math.Clamp(cur + delta, 0, n - 1);
        Vm.PreviewValue = (double)next / (n - 1);
    }

    private void OnPlayTick(object? sender, EventArgs e)
    {
        if (Vm is null || !Vm.HasStrip) { StopPlay(); return; }
        int n = Math.Max(2, Vm.FrameCount);
        int cur = (int)Math.Round(Vm.PreviewValue * (n - 1));
        int next = cur + (int)_direction;
        if (next >= n - 1) { next = n - 1; _direction = -1.0; }
        else if (next <= 0) { next = 0; _direction = 1.0; }
        Vm.PreviewValue = (double)next / (n - 1);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        OnDragOver(sender, e);
        if (e.Data.Contains(DataFormats.Files))
            ImportDropBorder.BorderBrush = AccentBrush;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
        => ImportDropBorder.BorderBrush = Brushes.Transparent;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        ImportDropBorder.BorderBrush = Brushes.Transparent;
        if (Vm is null)
            return;

        var items = e.Data.GetFiles();
        if (items is null)
            return;

        foreach (var item in items)
        {
            var path = item.TryGetLocalPath();
            if (path is null)
                continue;
            if (!AcceptedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                continue;

            Vm.LoadStripFromPath(path);
            e.Handled = true;
            break;
        }
    }
}
