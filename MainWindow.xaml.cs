using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AppPicker;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<AppEntry> _apps = [];
    private readonly ObservableCollection<AppEntry> _selected = [];
    private readonly Dictionary<string, AppEntry> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _applyingCapture;

    public MainWindow()
    {
        InitializeComponent();
        AppsList.ItemsSource = _apps;
        SelectedList.ItemsSource = _selected;
        LoadSettings();
        Loaded += (_, _) => { RefreshApps(); ApplyCapture(); };
        _timer.Tick += (_, _) => RefreshApps();
        _timer.Start();
        Closing += (_, _) => { _timer.Stop(); SaveSettings(); };
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshApps();
    private void Search_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => FilterApps();

    private void RefreshApps()
    {
        var found = AppEnumerator.GetApps(new WindowInteropHelper(this).Handle);
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in found)
        {
            running.Add(app.Id);
            if (!_known.TryGetValue(app.Id, out var entry)) _known[app.Id] = entry = new AppEntry(app.Id, app.Name);
            entry.Name = app.Name;
            entry.Title = app.Title;
            entry.IsRunning = true;
            entry.IconSource ??= IconLoader.Load(app.Path);
        }
        foreach (var entry in _known.Values) if (!running.Contains(entry.Id)) entry.IsRunning = false;
        FilterApps();
        UpdateStatus();
    }

    private void FilterApps()
    {
        if (SearchBox is null) return;
        var query = SearchBox.Text.Trim();
        var items = _known.Values.Where(x => x.IsRunning && (query.Length == 0 ||
            x.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            x.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        _apps.Clear();
        foreach (var item in items) _apps.Add(item);
        EmptyText.Text = query.Length == 0 ? "Открытых приложений пока нет" : "По вашему запросу ничего не найдено";
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AppEntry app }) return;
        if (app.IsSelected) _selected.Remove(app);
        else _selected.Add(app);
        app.IsSelected = !app.IsSelected;
        SaveSettings();
        UpdateStatus();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AppEntry app }) return;
        _selected.Remove(app);
        app.IsSelected = false;
        SaveSettings();
        UpdateStatus();
    }

    private void UpdateStatus() => StatusText.Text = $"Запущено приложений: {_known.Values.Count(x => x.IsRunning)} · Выбрано: {_selected.Count}";

    private void Capture_Changed(object sender, RoutedEventArgs e)
    {
        if (_applyingCapture || !IsLoaded) return;
        ApplyCapture();
        SaveSettings();
    }

    private void ApplyCapture()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        if (SetWindowDisplayAffinity(handle, OwnCaptureCheck.IsChecked == true ? 0x00000011u : 0u)) return;
        _applyingCapture = true;
        OwnCaptureCheck.IsChecked = false;
        _applyingCapture = false;
        System.Windows.MessageBox.Show("Windows не смогла исключить окно AppPicker из захвата. Нужна Windows 10 версии 2004 или новее.", "Приватность AppPicker", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath));
            if (settings is null) return;
            foreach (var saved in settings.Selected ?? [])
            {
                if (string.IsNullOrWhiteSpace(saved.Id) || _known.ContainsKey(saved.Id)) continue;
                var app = new AppEntry(saved.Id, saved.Name) { IsSelected = true };
                _known[app.Id] = app;
                _selected.Add(app);
            }
            OwnCaptureCheck.IsChecked = settings.ExcludeOwnWindow;
        }
        catch (Exception ex) { StatusText.Text = $"Не удалось прочитать настройки: {ex.Message}"; }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var data = new Settings(_selected.Select(x => new SavedApp(x.Id, x.Name)).ToList(), OwnCaptureCheck.IsChecked == true);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { StatusText.Text = $"Не удалось сохранить настройки: {ex.Message}"; }
    }

    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppPicker", "settings.json");
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
}

public sealed class AppEntry(string id, string name) : INotifyPropertyChanged
{
    public string Id { get; } = id;
    private string _name = name;
    private string _title = "";
    private bool _selected;
    private bool _running;
    private ImageSource? _icon;
    public string Name { get => _name; set => Set(ref _name, value, nameof(Name)); }
    public string Title { get => _title; set => Set(ref _title, value, nameof(Title)); }
    public bool IsSelected { get => _selected; set => Set(ref _selected, value, nameof(IsSelected)); }
    public bool IsRunning { get => _running; set { Set(ref _running, value, nameof(IsRunning)); PropertyChanged?.Invoke(this, new(nameof(StateText))); } }
    public string StateText => IsRunning ? "Запущено" : "Не запущено";
    public ImageSource? IconSource { get => _icon; set => Set(ref _icon, value, nameof(IconSource)); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
    }
}

internal record SavedApp(string Id, string Name);
internal record Settings(List<SavedApp>? Selected, bool ExcludeOwnWindow);
internal record FoundApp(string Id, string Name, string Title, string? Path);

internal static class AppEnumerator
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr extra);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    public static List<FoundApp> GetApps(IntPtr ownHandle)
    {
        var results = new Dictionary<string, FoundApp>(StringComparer.OrdinalIgnoreCase);
        EnumWindows((hwnd, _) =>
        {
            if (hwnd == ownHandle || !IsWindowVisible(hwnd) || GetWindow(hwnd, 4) != IntPtr.Zero) return true;
            var style = IntPtr.Size == 8 ? GetWindowLongPtr(hwnd, -20).ToInt64() : GetWindowLong32(hwnd, -20);
            if ((style & 0x80) != 0) return true;
            if (DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            var title = new StringBuilder(512);
            GetWindowText(hwnd, title, title.Capacity);
            if (string.IsNullOrWhiteSpace(title.ToString())) return true;
            GetWindowThreadProcessId(hwnd, out var pid);
            try
            {
                using var process = Process.GetProcessById((int)pid);
                if (process.Id == Environment.ProcessId || process.ProcessName.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase)) return true;
                string? path;
                try { path = process.MainModule?.FileName; } catch { path = null; }
                var name = path is null ? process.ProcessName : Path.GetFileNameWithoutExtension(path);
                if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase) && title.ToString().Equals("Program Manager", StringComparison.OrdinalIgnoreCase)) return true;
                var id = path ?? $"process:{process.ProcessName}";
                if (!results.ContainsKey(id)) results[id] = new FoundApp(id, name, title.ToString(), path);
            }
            catch { }
            return true;
        }, IntPtr.Zero);
        return results.Values.ToList();
    }
}

internal static class IconLoader
{
    public static ImageSource? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
            image.Freeze();
            return image;
        }
        catch { return null; }
    }
}

