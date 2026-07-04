using System;
using System.Collections.Generic;
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

public partial class AssembleView : UserControl
{
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#FFE8440A"));

    // Single source of truth with the view model, so drag-drop accepts exactly what "Choose folder…"
    // and "Add files…" do — including the HDR formats (.exr / .hdr / 16-bit .tif). A private duplicate
    // here once omitted them and silently dropped dragged HDR frames (BUG-021).
    private static string[] AcceptedExtensions => FrameSequenceViewModel.AcceptedExtensions;

    // Auto-play is a view-side animation concern (mirrors the Create tab): it steps PreviewValue
    // through the frames on a timer, ping-ponging at the ends. Kept out of the view model.
    private readonly DispatcherTimer _playTimer;
    private double _direction = 1.0;

    public AssembleView()
    {
        InitializeComponent();

        _playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _playTimer.Tick += OnPlayTick;
        DetachedFromVisualTree += (_, _) => StopPlay();

        // Drop handlers are scoped to this tab's drop border so they don't collide with the other
        // tabs' drop zones. DragOver must set DragEffects or Drop never fires. (See the
        // avalonia-drag-drop-files skill.)
        FramesDropBorder.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        FramesDropBorder.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        FramesDropBorder.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        FramesDropBorder.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private FrameSequenceViewModel? Vm => DataContext as FrameSequenceViewModel;

    // ---- preview transport (mirrors the Create tab) ----

    private void OnPlayClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || Vm.Frames.Count < 2) return;
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

    // Step the preview by one frame in the sequence (the frames are discrete, so this snaps).
    private void StepPreview(int delta)
    {
        if (Vm is null) return;
        int n = Vm.Frames.Count;
        if (n < 2) return;
        int cur = (int)Math.Round(Vm.PreviewValue * (n - 1));
        int next = Math.Clamp(cur + delta, 0, n - 1);
        Vm.PreviewValue = (double)next / (n - 1);
    }

    private void OnPlayTick(object? sender, EventArgs e)
    {
        if (Vm is null) return;
        int n = Vm.Frames.Count;
        if (n < 2) { StopPlay(); return; }
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
            FramesDropBorder.BorderBrush = AccentBrush;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
        => FramesDropBorder.BorderBrush = Brushes.Transparent;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        FramesDropBorder.BorderBrush = Brushes.Transparent;
        if (Vm is null)
            return;

        var items = e.Data.GetFiles();
        if (items is null)
            return;

        // A whole rendered sequence is the common drop: accept image files, and expand a dropped
        // folder into its image files. The view model natural-sorts the combined set.
        var paths = new List<string>();
        foreach (var item in items)
        {
            var path = item.TryGetLocalPath();
            if (path is null)
                continue;

            if (Directory.Exists(path))
                paths.AddRange(Directory.EnumerateFiles(path)
                    .Where(f => AcceptedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)));
            else if (AcceptedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                paths.Add(path);
        }

        if (paths.Count > 0)
        {
            Vm.AddDroppedPaths(paths);
            e.Handled = true;
        }
    }

    // Copy the previewed render recipe (Blender / CSV / JSON) to the clipboard — a view (top-level) concern.
    private async void OnCopyRecipe(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || string.IsNullOrEmpty(Vm.GeneratedRecipe)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(Vm.GeneratedRecipe);
        Vm.StatusMessage = $"Copied the {Vm.RecipePreviewTarget} render recipe to the clipboard.";
    }
}
