using System.Windows;

namespace vtecPoint.Launcher;

public partial class MainWindow : Window
{
    private readonly ServerHost _server = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoadedAsync;
        Closed += (_, _) => _server.Dispose();
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            await _server.EnsureRunningAsync();

            await WebView.EnsureCoreWebView2Async();
            WebView.Source = new Uri(ServerHost.Url);

            LoadingPanel.Visibility = Visibility.Collapsed;
            WebView.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "vtecPoint — เริ่มระบบไม่สำเร็จ",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }
}
