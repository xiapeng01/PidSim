using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PidSimulator.Services;

namespace PidSimulator.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeService.Instance.ApplyTitleBar(this);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        UpdateMaximizeButtonContent();
    }

    private void HeaderBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? source = e.OriginalSource as DependencyObject;

        while (source is not null && !ReferenceEquals(source, this))
        {
            if (source is System.Windows.Controls.Button)
                return;

            source = VisualTreeHelper.GetParent(source);
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.OriginalSource is not System.Windows.Controls.Control)
        {
            Chart.SuspendRendering = true;
            try
            {
                DragMove();
            }
            finally
            {
                Chart.SuspendRendering = false;
            }
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void UpdateMaximizeButtonContent()
    {
        if (MaximizeButton is not null)
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }
}
