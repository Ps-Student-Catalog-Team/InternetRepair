using System;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Threading.Tasks;
using System.Windows.Navigation;
using DotRas;

namespace InternetRepair
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        // 处理所有超链接的点击事件（打开默认浏览器）
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            // 使用 UseShellExecute 在新进程中打开 URL
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        // 窗口加载时获取最新公告
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadAnnouncementAsync();
        }

        // 从服务器获取最新公告内容（模拟实现，实际可替换为真实 HTTP 请求）
        private async Task LoadAnnouncementAsync()
        {
            try
            {
                // 模拟从服务器获取公告（替换为您的实际 API 地址）
                string announcement = await GetAnnouncementFromServerAsync();
                公告内容.Text = announcement;
            }
            catch (Exception ex)
            {
                公告内容.Text = $"获取公告失败：{ex.Message}";
            }
        }

        // 异步获取公告（示例：调用一个公共 API，或模拟数据）
        private async Task<string> GetAnnouncementFromServerAsync()
        {
            // 方式一：使用 HttpClient 请求真实服务器（示例）
            // using (var client = new HttpClient())
            // {
            //     client.Timeout = TimeSpan.FromSeconds(10);
            //     string result = await client.GetStringAsync("https://your-server.com/api/announcement");
            //     return result;
            // }

            // 方式二：模拟网络延迟和数据（开发/测试用）
            await Task.Delay(800); // 模拟网络请求延迟
            return "学生目录目前已重新获取部分密码\r\n\r\n更新了VPN状态页面φ(゜▽゜*)♪\r\n\r\n以后VPN的最新密码将会同步在VPN状态页面\r\n\r\n本网站仅向学生提供服务\r\n使用服务产生的一切后果由使用者承担\r\n\r\n";
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}