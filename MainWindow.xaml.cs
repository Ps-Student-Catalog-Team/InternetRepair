using NetworkTroubleshooter; // 引入 VpnManager 所在命名空间
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace InternetRepair
{
    public partial class MainWindow : Window
    {
        private VpnManager _vpnManager = new VpnManager();
        private DispatcherTimer _statusTimer;
        private CancellationTokenSource _cts; // 用于取消连接操作
        private bool _isConnected; // 是否已连接成功

        public MainWindow()
        {
            InitializeComponent();
            InitializeStatusTimer();
        }
        private void InitializeStatusTimer()
        {
            _statusTimer = new DispatcherTimer();
            _statusTimer.Interval = TimeSpan.FromSeconds(5); // 每5秒刷新一次
            _statusTimer.Tick += (s, e) => UpdateNetworkStatus();
            _statusTimer.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            if (_isConnected)
            {
                // 异步执行清理，但窗口关闭无需等待
                _ = DisconnectAndCleanupAsync();
            }
            _statusTimer?.Stop();
            base.OnClosed(e);
        }

        // 处理所有超链接的点击事件（打开默认浏览器）
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        // 窗口加载时获取最新公告和网络状态
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadAnnouncementAsync();
            UpdateNetworkStatus();
        }

        // 从服务器获取最新公告内容
        private async Task<string> GetAnnouncementFromServerAsync()
        {
            return "学生目录目前已重新获取部分密码\r\n\r\n更新了VPN状态页面φ(゜▽゜*)♪\r\n\r\n以后VPN的最新密码将会同步在VPN状态页面\r\n\r\n本网站仅向学生提供服务\r\n使用服务产生的一切后果由使用者承担";
        }

        private async Task LoadAnnouncementAsync()
        {
            try
            {
                string announcement = await GetAnnouncementFromServerAsync();
                公告内容.Text = announcement;
            }
            catch (Exception ex)
            {
                公告内容.Text = $"获取公告失败：{ex.Message}";
            }
        }

        // 更新网络状态显示（使用 VpnManager 获取当前代理设置）
        private void UpdateNetworkStatus()
        {
            // 获取代理设置
            _vpnManager.GetSystemProxySettings(out bool proxyEnabled, out string proxyServer, out string bypassList);

            // 获取 VPN 状态（通过 VpnManager）
            string vpnInfo = _vpnManager.GetActiveVpnInfo();

            // 组合显示
            if (proxyEnabled)
            {
                网络状态文本.Text = $"✓ 系统代理已启用\n代理服务器：{proxyServer}\n";
            }
            else
            {
                网络状态文本.Text = $"✓ 系统代理未启用\n直接连接互联网\n\n{vpnInfo}";
            }
        }

        // 连接按钮点击事件
        // 连接按钮点击事件
        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            // 如果当前按钮是“开始连接”，则开始连接
            if (连接按钮.Content.ToString() == "开始连接")
            {
                // 创建取消令牌
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                // 禁用按钮并改变文字
                连接按钮.Content = "取消连接";
                // 注意：按钮保持启用状态，以便用户可以取消

                连接进度条.Value = 0;
                进度显示.Text = "连接状态：正在准备...";
                _isConnected = false;

                try
                {
                    if (Proxy1Radio.IsChecked == true)
                    {
                        await ConnectViaProxy1Async(token);
                    }
                    else if (Proxy2Radio.IsChecked == true)
                    {
                        await ConnectViaProxy2Async(token);
                    }
                    else if (VpnRadio.IsChecked == true)
                    {
                        await ConnectViaVpnAsync(token);
                    }
                    else
                    {
                        MessageBox.Show("请选择一种连接方式。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        连接按钮.Content = "开始连接";
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    // 用户主动取消，不做额外提示
                    进度显示.Text = "连接状态：已取消";
                    连接进度条.Value = 0;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"连接失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    进度显示.Text = "连接状态：失败";
                    连接进度条.Value = 0;
                }
                finally
                {
                    // 如果连接成功，按钮已变为“取消连接”，无需恢复；
                    // 如果连接失败或取消，恢复按钮文字
                    if (!_isConnected)
                    {
                        连接按钮.Content = "开始连接";
                    }
                }
            }
            else // 当前是“取消连接”状态，点击取消
            {
                // 请求取消操作
                _cts?.Cancel();

                // 如果已经连接成功，则断开连接并清理配置
                if (_isConnected)
                {
                    await DisconnectAndCleanupAsync();
                }

                // 恢复按钮文字和状态
                连接按钮.Content = "开始连接";
                进度显示.Text = "连接状态：已断开";
                连接进度条.Value = 0;
                _isConnected = false;
            }
        }

        // 断开已连接的配置（代理或VPN）
        private async Task DisconnectAndCleanupAsync()
        {
            // 根据之前选择的连接类型进行清理
            if (Proxy1Radio.IsChecked == true || Proxy2Radio.IsChecked == true)
            {
                await Task.Run(() =>
                {
                    _vpnManager.DisableSystemProxy(); // 禁用代理（不清除配置）
                                                      // 或者调用 ClearAndDisableSystemProxy() 完全清除
                });
            }
            else if (VpnRadio.IsChecked == true)
            {
                await Task.Run(() =>
                {
                    _vpnManager.DeleteVpn("以太网 4");
                });
            }
            // 更新网络状态显示
            UpdateNetworkStatus();
        }

        // 通过代理1接入（支持取消）
        private async Task ConnectViaProxy1Async(CancellationToken cancellationToken)
        {
            string proxyServer = "127.0.0.1:8080";
            string bypassList = "localhost;127.*;192.168.*";

            // 检查取消请求
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Run(() =>
            {
                Dispatcher.Invoke(() => 进度显示.Text = "连接状态：正在设置代理1...");
                Dispatcher.Invoke(() => 连接进度条.Value = 50);

                bool success = _vpnManager.SetSystemProxy(true, proxyServer, bypassList);
                if (!success)
                    throw new Exception("设置代理失败，请检查权限或注册表。");
            }, cancellationToken);

            Dispatcher.Invoke(() => 连接进度条.Value = 100);
            Dispatcher.Invoke(() => 进度显示.Text = "连接状态：代理1已启用");
            MessageBox.Show("代理1已成功启用。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            _isConnected = true;
            UpdateNetworkStatus();
        }

        // 通过代理2接入（支持取消）
        private async Task ConnectViaProxy2Async(CancellationToken cancellationToken)
        {
            string proxyServer = "127.0.0.1:8888";
            string bypassList = "localhost;127.*;192.168.*";

            cancellationToken.ThrowIfCancellationRequested();

            await Task.Run(() =>
            {
                Dispatcher.Invoke(() => 进度显示.Text = "连接状态：正在设置代理2...");
                Dispatcher.Invoke(() => 连接进度条.Value = 50);

                bool success = _vpnManager.SetSystemProxy(true, proxyServer, bypassList);
                if (!success)
                    throw new Exception("设置代理失败，请检查权限或注册表。");
            }, cancellationToken);

            Dispatcher.Invoke(() => 连接进度条.Value = 100);
            Dispatcher.Invoke(() => 进度显示.Text = "连接状态：代理2已启用");
            MessageBox.Show("代理2已成功启用。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            _isConnected = true;
            UpdateNetworkStatus();
        }

        // 通过 VPN 接入（支持取消）
        private async Task ConnectViaVpnAsync(CancellationToken cancellationToken)
        {
            string entryName = "以太网 4";
            string serverAddress = "10.88.202.73";
            string userName = "ps";
            string password = @"\@(^O^)@/";
            string preSharedKey = "pysyzx";

            cancellationToken.ThrowIfCancellationRequested();

            await Task.Run(() =>
            {
                Dispatcher.Invoke(() => 进度显示.Text = "连接状态：正在创建 VPN 连接...");
                Dispatcher.Invoke(() => 连接进度条.Value = 30);

                bool success = _vpnManager.CreateAndConnectVpn(entryName, serverAddress, userName, password, preSharedKey);
                if (!success)
                    throw new Exception("创建或连接 VPN 失败，请检查参数或网络。");
            }, cancellationToken);

            Dispatcher.Invoke(() => 连接进度条.Value = 100);
            Dispatcher.Invoke(() => 进度显示.Text = "连接状态：VPN 已连接");
            MessageBox.Show("VPN 连接已建立。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            _isConnected = true;
            UpdateNetworkStatus();
        }

    }
}