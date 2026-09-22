using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AppPicker;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<WindowEntry> _windows = [];
    private readonly ObservableCollection<WindowEntry> _selected = [];
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public MainWindow()
    {
        InitializeComponent();
        WindowsList.ItemsSource = _windows;
        SelectedList.ItemsSource = _selected;
        RefreshWindows();
        _refreshTimer.Tick += (_, _) => RefreshWindows();
        _refreshTimer.Start();
        Closing += (_, _) => _refreshTimer.Stop();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshWindows();

    private void RefreshWindows()
    {
        var selectedHandles = _selected.Select(x => x.Handle).ToHashSet();
        var ownHandle = new WindowInteropHelper(this).Handle;
        var current = WindowEnumerator.GetVisibleWindows().Where(x => x.Handle != ownHandle).ToList();
        _windows.Clear();
        foreach (var item in current) { item.IsSelected = selectedHandles.Contains(item.Handle); _windows.Add(item); }
        for (var i = _selected.Count - 1; i >= 0; i--)
            if (!current.Any(x => x.Handle == _selected[i].Handle)) _selected.RemoveAt(i);
        StatusText.Text = $"Найдено видимых окон: {_windows.Count}";
    }

    private void WindowCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WindowEntry item }) return;
        var existing = _selected.FirstOrDefault(x => x.Handle == item.Handle);
        if (existing is null) { item.IsSelected = true; _selected.Add(item); }
        else { _selected.Remove(existing); item.IsSelected = false; }
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WindowEntry item }) return;
        _selected.Remove(item);
        var match = _windows.FirstOrDefault(x => x.Handle == item.Handle);
        if (match is not null) match.IsSelected = false;
    }

    private void OwnCapture_Click(object sender, RoutedEventArgs e)
    {
        var affinity = OwnCaptureCheck.IsChecked == true ? 0x00000011u : 0u;
        if (!SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, affinity))
        {
            OwnCaptureCheck.IsChecked = false;
            MessageBox.Show("Windows не смогла применить настройку. Она поддерживается начиная с Windows 10 версии 2004.", "Настройка захвата", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
}

public sealed class WindowEntry : INotifyPropertyChanged
{
    public IntPtr Handle { get; init; }
    public string Title { get; init; } = "";
    public string ProcessName { get; init; } = "";
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set { _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

internal static class WindowEnumerator
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr extra);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    public static List<WindowEntry> GetVisibleWindows()
    {
        var results = new List<WindowEntry>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var title = new StringBuilder(512);
            GetWindowText(hwnd, title, title.Capacity);
            if (string.IsNullOrWhiteSpace(title.ToString())) return true;
            GetWindowThreadProcessId(hwnd, out var pid);
            try { using var process = Process.GetProcessById((int)pid); results.Add(new WindowEntry { Handle = hwnd, Title = title.ToString(), ProcessName = process.ProcessName }); }
            catch { }
            return true;
        }, IntPtr.Zero);
        return results.OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
