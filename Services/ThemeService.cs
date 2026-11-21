using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PidSimulator.Services;

public enum AppTheme
{
    Dark,
    Light
}

public sealed class ThemeService
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    private static readonly Uri DarkSource = new("Themes/DarkTheme.xaml", UriKind.Relative);
    private static readonly Uri LightSource = new("Themes/LightTheme.xaml", UriKind.Relative);

    private ResourceDictionary? _current;

    public static ThemeService Instance { get; } = new();

    public AppTheme Current { get; private set; } = AppTheme.Light;

    public void Apply(AppTheme theme)
    {
        Application? app = Application.Current;
        if (app is null)
            return;

        Uri targetSource = theme == AppTheme.Dark ? DarkSource : LightSource;

        if (_current is null)
        {
            _current = app.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source == DarkSource || d.Source == LightSource);
        }

        if (_current?.Source == targetSource)
        {
            Current = theme;
            ApplyTitleBarToWindows();
            return;
        }

        ResourceDictionary dictionary = new() { Source = targetSource };

        if (_current is not null)
            app.Resources.MergedDictionaries.Remove(_current);

        app.Resources.MergedDictionaries.Insert(0, dictionary);
        _current = dictionary;
        Current = theme;

        ApplyTitleBarToWindows();
    }

    public void ApplyTitleBar(Window window)
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;

            int darkMode = Current == AppTheme.Dark ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
        }
        catch
        {
            // 非 Windows 10+ 或不支持 DWM 时忽略，不影响主题资源切换。
        }
    }

    private void ApplyTitleBarToWindows()
    {
        Application? app = Application.Current;
        if (app is null)
            return;

        foreach (Window window in app.Windows)
            ApplyTitleBar(window);
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int size);
}
