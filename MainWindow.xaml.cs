using NetworkTroubleshooter; // 引入 VpnManager 所在命名空间
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace InternetRepair
{
    public partial class MainWindow : Window
    {
        private VpnManager _vpnManager = new VpnManager();
        private DispatcherTimer _statusTimer;
        private CancellationTokenSource _cts; // 用于取消连接操作
        private bool _isConnected; // 是否已连接成功
        private string _vpnPassword; // 存储从API获取的VPN密码（新增）

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

        // 窗口加载时获取最新公告、网络状态以及VPN密码
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadAnnouncementAsync();
            await LoadVpnPasswordAsync(); // 新增：获取VPN密码
            UpdateNetworkStatus();
        }

        // 新增：从API获取VPN密码并存储到 _vpnPassword
        private async Task LoadVpnPasswordAsync()
        {
            try
            {
                using HttpClient client = new HttpClient();
                HttpResponseMessage response = await client.GetAsync("http://10.88.202.73:3132/api/vpn-password");
                string json = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(json);
                _vpnPassword = doc.RootElement.GetProperty("password").GetString();
            }
            catch (Exception ex)
            {
                // 如果获取密码失败，记录日志或提示用户，这里设置为空并显示错误
                _vpnPassword = null;
                MessageBox.Show($"获取VPN密码失败：{ex.Message}\nVPN连接将不可用。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task<(string Announcement, string ModifiedTime)> GetAnnouncementFromServerAsync()
        {
            const string remoteApiUrl = "http://10.88.202.73:3132/api/announcement";
            const string localApiUrl = "http://localhost:3132/api/announcement";

            // 1. 判断远程主机是否可达，选择 URL
            string apiUrl = await IsHostReachableAsync("10.88.202.73") ? remoteApiUrl : localApiUrl;

            // 2. 创建 HttpClient，设置超时 1 秒
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(1);

            try
            {
                // 3. 请求 API
                var json = await httpClient.GetStringAsync(apiUrl);

                // 4. 解析 JSON
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                // 5. 提取修改时间（如果不存在则返回空字符串）
                string modifiedTime = root.TryGetProperty("serverModifiedTime", out var timeElement)
                    ? timeElement.GetString() ?? string.Empty
                    : string.Empty;

                // 6. 提取 newest.content 数组
                if (root.TryGetProperty("newest", out var newestElement) &&
                    newestElement.TryGetProperty("content", out var contentElement) &&
                    contentElement.ValueKind == JsonValueKind.Array)
                {
                    var contentList = new List<string>();
                    foreach (var item in contentElement.EnumerateArray())
                    {
                        var rawText = item.GetString() ?? string.Empty;
                        var plainText = HtmlToPlainText(rawText);
                        contentList.Add(plainText);
                    }

                    string announcement = string.Join("\r\n\r\n", contentList);
                    return (announcement, modifiedTime);
                }
                else
                {
                    // 数据结构不符合预期
                    return ("公告数据格式错误，请联系管理员。", modifiedTime);
                }
            }
            catch (Exception ex)
            {
                // 记录日志（略），返回错误信息，修改时间为空
                return ($"无法获取公告：{ex.Message}", string.Empty);
            }
        }

        /// <summary>
        /// 简单的 HTML 转纯文本方法，移除所有标签，保留内部文本。
        /// </summary>
        private static string HtmlToPlainText(string html)
        {
            if (string.IsNullOrEmpty(html))
                return html;

            // 移除 HTML 标签
            var plain = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", "");
            // 解码 HTML 实体
            plain = System.Net.WebUtility.HtmlDecode(plain);
            // 合并空白字符
            plain = System.Text.RegularExpressions.Regex.Replace(plain, @"\s+", " ");
            return plain.Trim();
        }

        /// <summary>
        /// 异步检测指定主机是否可达（通过 ICMP Ping，超时 1 秒）
        /// </summary>
        private static async Task<bool> IsHostReachableAsync(string host)
        {
            using var ping = new Ping();
            try
            {
                // 使用异步 Ping 方法（推荐）
                var reply = await ping.SendPingAsync(host, 1000);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        private async Task LoadAnnouncementAsync()
        {
            try
            {
                var (announcement, modifiedTime) = await GetAnnouncementFromServerAsync();
                公告内容.Text = announcement;
                公告时间.Content = string.IsNullOrEmpty(modifiedTime) ? "未知时间" : modifiedTime;
            }
            catch (Exception ex)
            {
                公告内容.Text = $"获取公告失败：{ex.Message}";
                公告时间.Content = string.Empty;
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
                        // 检查VPN密码是否已成功获取
                        if (string.IsNullOrEmpty(_vpnPassword))
                        {
                            throw new Exception("VPN密码未获取到，请检查网络或稍后重试。");
                        }
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
            string proxyServer = "10.88.20.273:10001";
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
            _isConnected = true;
            UpdateNetworkStatus();
        }

        // 通过代理2接入（支持取消）
        private async Task ConnectViaProxy2Async(CancellationToken cancellationToken)
        {
            string proxyServer = "10.88.202.73:10002";
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
            _isConnected = true;
            UpdateNetworkStatus();
        }

        // 通过 VPN 接入（支持取消）
        private async Task ConnectViaVpnAsync(CancellationToken cancellationToken)
        {
            string entryName = "以太网 4";
            string serverAddress = "10.88.202.73";
            string userName = "ps";
            string preSharedKey = "pysyzx";

            cancellationToken.ThrowIfCancellationRequested();

            await Task.Run(() =>
            {
                Dispatcher.Invoke(() => 进度显示.Text = "连接状态：正在创建 VPN 连接...");
                Dispatcher.Invoke(() => 连接进度条.Value = 30);

                // 使用类字段 _vpnPassword 作为密码
                bool success = _vpnManager.CreateAndConnectVpn(entryName, serverAddress, userName, _vpnPassword, preSharedKey);
                if (!success)
                    throw new Exception("创建或连接 VPN 失败，请检查参数或网络。");
            }, cancellationToken);

            Dispatcher.Invoke(() => 连接进度条.Value = 100);
            Dispatcher.Invoke(() => 进度显示.Text = "连接状态：VPN 已连接");
            _isConnected = true;
            UpdateNetworkStatus();
        }
    }
}