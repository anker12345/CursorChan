using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using CursorShelf.Core;
using Microsoft.Win32;

namespace CursorShelf;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<SchemeItem> _items = [];
    private readonly ObservableCollection<RoleItem> _roles = [];
    private readonly ICollectionView _view;
    private readonly DispatcherTimer _sizeTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private bool _ready;

    private SchemeItem? Selected => Gallery.SelectedItem as SchemeItem;

    public MainWindow()
    {
        InitializeComponent();

        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = o => o is SchemeItem s &&
            (SearchBox.Text.Length == 0 || s.Name.Contains(SearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        Gallery.ItemsSource = _view;
        RoleList.ItemsSource = _roles;

        SizeSlider.Value = SystemCursors.GetBaseSize();
        UpdateSizeText();
        _sizeTimer.Tick += (_, _) => { _sizeTimer.Stop(); ApplySize(); };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); StatusText.Text = ""; };

        ReloadAll();
        _ready = true;
    }

    // ---------- 一覧 ----------

    private void ReloadAll(string? selectName = null, SchemeKind? selectKind = null)
    {
        var library = SchemeLibrary.Load().OrderBy(s => s.Name, StringComparer.CurrentCulture).ToList();
        var libNames = library.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installed = SystemCursors.LoadInstalledSchemes()
            .Where(s => !(s.Kind == SchemeKind.User && libNames.Contains(s.Name)));

        _items.Clear();
        _items.Add(new SchemeItem(SystemCursors.WindowsDefault()));
        foreach (var s in library) _items.Add(new SchemeItem(s));
        foreach (var s in installed) _items.Add(new SchemeItem(s));
        UpdateApplied();

        var target =
            _items.FirstOrDefault(i => selectName != null && i.Name == selectName && (selectKind == null || i.Scheme.Kind == selectKind))
            ?? _items.FirstOrDefault(i => i.IsApplied)
            ?? _items.FirstOrDefault();
        Gallery.SelectedItem = target;
        if (target != null) Gallery.ScrollIntoView(target);
        ShowDetail();
    }

    private void UpdateApplied()
    {
        var current = SystemCursors.CurrentSchemeName();
        foreach (var item in _items)
        {
            var s = item.Scheme;
            item.IsApplied = s.Kind == SchemeKind.WindowsDefault
                ? string.IsNullOrEmpty(current) || current.Equals(SystemCursors.WindowsDefaultRegistryName, StringComparison.OrdinalIgnoreCase)
                : string.Equals(s.RegistryName, current, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void Gallery_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowDetail();

    private void ShowDetail()
    {
        _roles.Clear();
        var item = Selected;
        DetailEmpty.Visibility = item == null ? Visibility.Visible : Visibility.Collapsed;
        DetailContent.Visibility = item == null ? Visibility.Collapsed : Visibility.Visible;
        if (item == null) return;

        var editable = item.Scheme.IsEditable;
        NameText.Text = item.Name;
        NameText.Visibility = editable ? Visibility.Collapsed : Visibility.Visible;
        NameBox.Text = item.Name;
        NameBox.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        UpdateKindText(item);
        DuplicateText.Text = editable ? "複製" : "コピーして編集";
        FolderButton.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        DeleteButton.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;

        foreach (var role in CursorRoles.All) _roles.Add(new RoleItem(role, item));
    }

    private void UpdateKindText(SchemeItem item)
    {
        var count = item.Scheme.Kind == SchemeKind.WindowsDefault ? CursorRoles.All.Count : item.Scheme.AssignedCount;
        KindText.Text = $"{item.KindLabel}　・　{count} / {CursorRoles.All.Count} 種類を設定" +
                        (item.IsApplied ? "　・　使用中" : "");
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _view.Refresh();
    }

    // ---------- 適用 ----------

    private void Apply(SchemeItem item, bool quiet = false)
    {
        try
        {
            SystemCursors.Apply(item.Scheme);
            UpdateApplied();
            if (Selected != null) UpdateKindText(Selected);
            if (!quiet) Status($"「{item.Name}」を適用しました");
        }
        catch (Exception ex)
        {
            ShowError("適用できませんでした", ex);
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (Selected != null) Apply(Selected);
    }

    private void Card_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as ListBoxItem)?.DataContext is SchemeItem item) Apply(item);
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var def = _items.First(i => i.Scheme.Kind == SchemeKind.WindowsDefault);
        Apply(def, true);
        Status("Windows 標準のカーソルに戻しました");
    }

    // ---------- セットの管理 ----------

    private void NewSet_Click(object sender, RoutedEventArgs e)
    {
        var name = SchemeLibrary.UniqueName("新しいセット", _items.Select(i => i.Name));
        SchemeLibrary.CreateNew(name);
        ReloadAll(name, SchemeKind.Library);
        Status("新しいセットを作りました。右のボタンで役割ごとにカーソルを設定できます");
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        try
        {
            var name = SchemeLibrary.UniqueName(item.Name + " のコピー", _items.Select(i => i.Name));
            SchemeLibrary.Duplicate(item.Scheme, name);
            ReloadAll(name, SchemeKind.Library);
            Status($"「{name}」を作りました");
        }
        catch (Exception ex)
        {
            ShowError("コピーできませんでした", ex);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { Scheme.IsEditable: true } item) return;
        var r = MessageBox.Show(this, $"「{item.Name}」を削除しますか？\nライブラリにコピーしたカーソルファイルも消えます。",
            "セットの削除", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (r != MessageBoxResult.Yes) return;
        try
        {
            if (item.IsApplied) SystemCursors.Apply(SystemCursors.WindowsDefault());
            SystemCursors.UnregisterUserScheme(item.Scheme.Name);
            SchemeLibrary.Delete(item.Scheme);
            ReloadAll();
            Status($"「{item.Name}」を削除しました");
        }
        catch (Exception ex)
        {
            ShowError("削除できませんでした", ex);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Selected?.Scheme.Folder is { } folder && Directory.Exists(folder))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitRename();
        else if (e.Key == Key.Escape && Selected != null) NameBox.Text = Selected.Name;
    }

    private void NameBox_LostFocus(object sender, RoutedEventArgs e) => CommitRename();

    private void CommitRename()
    {
        if (Selected is not { Scheme.IsEditable: true } item) return;
        var name = NameBox.Text.Trim();
        if (name.Length == 0 || name == item.Name) { NameBox.Text = item.Name; return; }
        name = SchemeLibrary.UniqueName(name, _items.Where(i => i != item).Select(i => i.Name));
        var wasApplied = item.IsApplied;
        SystemCursors.UnregisterUserScheme(item.Scheme.Name);
        item.Scheme.Name = name;
        item.Scheme.RegistryName = name;
        SchemeLibrary.Save(item.Scheme);
        if (wasApplied) SystemCursors.Apply(item.Scheme);
        ReloadAll(name, SchemeKind.Library);
        Status($"名前を「{name}」に変えました");
    }

    // ---------- 役割ごとの編集 ----------

    private void RoleFile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not RoleItem r) return;
        var dlg = new OpenFileDialog
        {
            Title = $"「{r.Label}」のカーソルを選ぶ",
            Filter = "カーソル (*.cur;*.ani)|*.cur;*.ani|すべてのファイル (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        if (CursorDecoder.Load(dlg.FileName) == null)
        {
            MessageBox.Show(this, "カーソルとして読み込めないファイルでした。", "CursorShelf", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        EditRole(r, s => SchemeLibrary.SetRoleFile(s, r.Role.Name, dlg.FileName));
    }

    private void RoleImage_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not RoleItem r) return;
        var dlg = new OpenFileDialog
        {
            Title = $"「{r.Label}」にする画像を選ぶ",
            Filter = "画像 (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|すべてのファイル (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;

        PngCursorWindow win;
        try { win = new PngCursorWindow(dlg.FileName, r.Label) { Owner = this }; }
        catch (Exception ex) { ShowError("画像を開けませんでした", ex); return; }
        if (win.ShowDialog() != true) return;

        var temp = Path.Combine(Path.GetTempPath(), $"CursorShelf-{Guid.NewGuid():N}.cur");
        try
        {
            CursorDecoder.WriteCursor(win.Image, win.HotX, win.HotY, temp);
            EditRole(r, s => SchemeLibrary.SetRoleFile(s, r.Role.Name, temp));
        }
        finally
        {
            try { File.Delete(temp); } catch { /* 一時ファイル */ }
        }
    }

    private void RoleClear_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is RoleItem r)
            EditRole(r, s => SchemeLibrary.ClearRole(s, r.Role.Name));
    }

    private void EditRole(RoleItem r, Action<CursorScheme> edit)
    {
        try
        {
            edit(r.Owner.Scheme);
            r.Owner.Refresh();
            foreach (var role in _roles) role.Refresh();
            UpdateKindText(r.Owner);
            if (r.Owner.IsApplied) Apply(r.Owner, true);
            Status($"「{r.Label}」を変更しました");
        }
        catch (Exception ex)
        {
            ShowError("変更できませんでした", ex);
        }
    }

    // ---------- 取り込み ----------

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = ImportButton.ContextMenu!;
        menu.PlacementTarget = ImportButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ImportFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "取り込むカーソルセットを選ぶ",
            Filter = "カーソルセット (*.zip;*.inf;*.cur;*.ani)|*.zip;*.inf;*.cur;*.ani|すべてのファイル (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) == true) ImportPaths(dlg.FileNames);
    }

    private void ImportFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "カーソルが入っているフォルダーを選ぶ", Multiselect = true };
        if (dlg.ShowDialog(this) == true) ImportPaths(dlg.FolderNames);
    }

    private void ImportPaths(string[] paths)
    {
        ImportResult result;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            result = SchemeImporter.Import(paths, _items.Select(i => i.Name));
        }
        catch (Exception ex)
        {
            ShowError("取り込めませんでした", ex);
            return;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        ReloadAll(result.Imported.LastOrDefault()?.Name, SchemeKind.Library);
        if (result.Imported.Count > 0)
            Status($"{result.Imported.Count} 個のセットを取り込みました");
        if (result.Messages.Count > 0)
            MessageBox.Show(this, string.Join("\n\n", result.Messages), "取り込みのお知らせ",
                MessageBoxButton.OK, result.Imported.Count > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        var ok = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e) => DropOverlay.Visibility = Visibility.Collapsed;

    private void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            ImportPaths(paths);
    }

    // ---------- サイズ ----------

    private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateSizeText();
        if (!_ready) return;
        _sizeTimer.Stop();
        _sizeTimer.Start();
    }

    private void UpdateSizeText()
    {
        if (SizeText != null) SizeText.Text = $"{(int)SizeSlider.Value} px";
    }

    private void ApplySize()
    {
        try
        {
            SystemCursors.SetBaseSize((int)SizeSlider.Value);
            Status($"カーソルのサイズを {(int)SizeSlider.Value} px にしました");
        }
        catch (Exception ex)
        {
            ShowError("サイズを変えられませんでした", ex);
        }
    }

    // ---------- 小物 ----------

    private void Status(string text)
    {
        StatusText.Text = text;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void ShowError(string title, Exception ex) =>
        MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
}
