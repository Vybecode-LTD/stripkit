using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using StripKit.ViewModels;

namespace StripKit.Views;

public partial class ImporterView : UserControl
{
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#FFE8440A"));
    private static readonly string[] AcceptedExtensions = [".png", ".webp", ".bmp", ".jpg", ".jpeg"];

    public ImporterView()
    {
        InitializeComponent();

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
