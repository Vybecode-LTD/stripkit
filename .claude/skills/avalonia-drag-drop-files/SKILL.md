---
name: avalonia-drag-drop-files
description: >-
  Wire file drag-and-drop into an Avalonia 11 view so users can drop files onto a
  window or control. Use when adding a drop a file here zone to an Avalonia app,
  when a drop handler never fires, when migrating from Avalonia 10 drag-drop, when
  reading the local paths of dropped files, when filtering dropped files by
  extension, or when showing visual feedback while dragging over a target. Covers
  enabling AllowDrop, handling the DragOver and Drop routed events, why DragOver
  must set DragEffects or Drop will not fire, reading dropped items via
  DataFormats.Files and IStorageItem.TryGetLocalPath (replacing the removed
  FileNames API), handling multiple files, and delegating to the view model so
  logic stays out of code-behind. Triggers on Avalonia drag and drop, drop files,
  AllowDrop, DragDrop.DropEvent, DragOver, DataFormats.Files, GetFiles,
  TryGetLocalPath, drop handler not firing, drag drop not working, drop a file
  onto the window.
---

# Avalonia File Drag-and-Drop

Letting a user drop a file onto an Avalonia window is four small steps, and three
of the four are where people get stuck: you must opt the target *in*, you must
handle `DragOver` (not just `Drop`), and you must read files through the modern
storage API, because the old `FileNames` API was removed in Avalonia 11.

## Core principle

A drop target is inert until you (1) set `AllowDrop`, and (2) tell Avalonia, in
`DragOver`, that you accept the drag by setting `e.DragEffects`. If you skip the
`DragOver` step, the cursor shows "no entry" and **the `Drop` event never
fires** — the single most common reason drag-drop "doesn't work."

## Procedure

1. **Opt the target in.** In XAML on the control or window you want to accept
   drops:

   ```xml
   <Border x:Name="DropZone" DragDrop.AllowDrop="True"
           Background="#FF1E1E1E" CornerRadius="6">
       <TextBlock x:Name="DropHint" Text="Drop a PNG here"
                  HorizontalAlignment="Center" VerticalAlignment="Center"/>
   </Border>
   ```

2. **Attach handlers in code-behind.** Drag-drop is genuinely a view concern, so
   wiring it in code-behind is correct — but keep the *logic* in the view model.

   ```csharp
   using Avalonia.Input;
   using Avalonia.Platform.Storage;

   public MainWindow()
   {
       InitializeComponent();
       AddHandler(DragDrop.DragOverEvent, OnDragOver);
       AddHandler(DragDrop.DropEvent, OnDrop);
       // optional, for highlight on enter/leave:
       AddHandler(DragDrop.DragEnterEvent, OnDragOver);
   }
   ```

3. **Accept the drag in `DragOver`.** Only signal Copy when the payload actually
   contains files; otherwise signal None so the user sees it is not droppable.

   ```csharp
   private void OnDragOver(object? sender, DragEventArgs e)
   {
       e.DragEffects = e.Data.Contains(DataFormats.Files)
           ? DragDropEffects.Copy
           : DragDropEffects.None;
   }
   ```

4. **Read the files in `Drop`.** Avalonia 11 returns `IStorageItem`s; get a local
   path with the `TryGetLocalPath()` extension. Filter and hand off to the VM.

   ```csharp
   private void OnDrop(object? sender, DragEventArgs e)
   {
       if (DataContext is not MainWindowViewModel vm) return;
       var items = e.Data.GetFiles();          // IEnumerable<IStorageItem>?
       if (items is null) return;

       foreach (var item in items)
       {
           var path = item.TryGetLocalPath();
           if (path is null) continue;
           if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
           vm.LoadDroppedFile(path);            // logic lives in the view model
           break;                               // first match only, if single-file
       }
   }
   ```

## Avalonia 11 API notes (the migration trap)

- Use `DataFormats.Files` — **not** `DataFormats.FileNames` (removed).
- Use `e.Data.GetFiles()` returning `IEnumerable<IStorageItem>?` — **not**
  `GetFileNames()` (removed).
- `TryGetLocalPath()` is an extension in `Avalonia.Platform.Storage`; remember the
  `using`. It can return `null` for non-file-system items (e.g. a virtual file).
- `DragEventArgs`, `DataFormats`, and `DragDropEffects` live in `Avalonia.Input`.

## Visual feedback (optional but expected)

Toggle a highlight in `DragEnter`/`DragLeave`, and reset it in `Drop`:

```csharp
private void OnDragEnter(object? sender, DragEventArgs e)
{
    OnDragOver(sender, e);                       // also set the effect
    if (e.Data.Contains(DataFormats.Files))
        DropZone.BorderBrush = Brushes.Orange;   // or your accent
}
private void OnDragLeave(object? sender, DragEventArgs e)
    => DropZone.BorderBrush = Brushes.Transparent;
```

## Keep logic in the view model

The handler should only extract paths and call a method or command on the view
model (`vm.LoadDroppedFile(path)` or an `ICommand`). Do not open files, parse, or
mutate domain state inside the code-behind handler — that defeats MVVM testability
and duplicates the logic you already have behind the "Open file" button.

## Anti-patterns

- Handling `Drop` but not `DragOver` — `Drop` then never fires.
- Forgetting `DragDrop.AllowDrop="True"` on the target.
- Using the removed `DataFormats.FileNames` / `GetFileNames()` from Avalonia 10.
- Assuming `TryGetLocalPath()` is non-null — guard it.
- Doing file I/O and business logic in the code-behind handler instead of the VM.
- Not filtering by extension, then failing later on an unsupported drop.
