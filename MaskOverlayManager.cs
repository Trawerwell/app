using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AppPicker;

/// <summary>Experimental masks for full-display capture. Each mask is an AppPicker-owned window.</summary>
internal sealed class MaskOverlayManager : IDisposable
{
    private readonly Dictionary<IntPtr, Window> _overlays = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    public MaskOverlayManager()
    {
        _timer.Tick += (_, _) => PositionMasks();
        _timer.Start();
    }

    public bool SetTargets(IEnumerable<IntPtr> targets)
    {
        var wanted = targets.Where(x => x != IntPtr.Zero).ToHashSet();
        foreach (var target in _overlays.Keys.Where(x => !wanted.Contains(x)).ToList())
        {
            _overlays[target].Close();
            _overlays.Remove(target);
        }
        foreach (var target in wanted)
        {
            if (_overlays.ContainsKey(target)) continue;
            var mask = new Window
            {
                Width = 1,
                Height = 1,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Black,
                Opacity = 0.01,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                ResizeMode = ResizeMode.NoResize
            };
            mask.Show();
            var handle = new WindowInteropHelper(mask).Handle;
            if (handle == IntPtr.Zero || !SetWindowDisplayAffinity(handle, 0x00000001u))
            {
                mask.Close();
                return false;
            }
            var style = GetWindowLongPtr(handle, -20).ToInt64();
            SetWindowLongPtr(handle, -20, new IntPtr(style | 0x20 | 0x80 | 0x08000000));
            _overlays.Add(target, mask);
        }
        PositionMasks();
        return true;
    }

    private void PositionMasks()
    {
        foreach (var (target, mask) in _overlays)
        {
            if (!IsWindow(target) || IsIconic(target) || !IsWindowVisible(target) || !GetWindowRect(target, out var rect))
            {
                mask.Hide();
                continue;
            }
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) { mask.Hide(); continue; }
            if (!mask.IsVisible) mask.Show();
            var handle = new WindowInteropHelper(mask).Handle;
            SetWindowPos(handle, new IntPtr(-1), rect.Left, rect.Top, width, height, 0x0010 | 0x0040);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        foreach (var mask in _overlays.Values) mask.Close();
        _overlays.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}

