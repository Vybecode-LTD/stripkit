using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using StripKit.ViewModels;

namespace StripKit.Views;

public partial class AssembleView : UserControl
{
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#FFE8440A"));
    private static readonly string[] AcceptedExtensions = [".png", ".webp", ".bmp", ".jpg", ".jpeg"];

    public AssembleView()
    {
        InitializeComponent();

        // Drop handlers are scoped to this tab's drop border so they don't collide with the other
        // tabs' drop zones. DragOver must set DragEffects or Drop never fires. (See the
        // avalonia-drag-drop-files skill.)
        FramesDropBorder.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        FramesDropBorder.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        FramesDropBorder.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        FramesDropBorder.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private FrameSequenceViewModel? Vm => DataContext as FrameSequenceViewModel;

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
