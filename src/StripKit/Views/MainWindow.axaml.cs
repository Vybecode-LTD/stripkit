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
}
