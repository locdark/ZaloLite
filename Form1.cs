using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ZaloLite
{
    public partial class Form1 : Form
    {
        private WebView2 webView;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private bool isDarkMode = false;
        private bool isClosingExplicitly = false;

        public Form1()
        {
            InitializeComponent();
            SetupApp();
        }

        private void SetupApp()
        {
            // Cấu hình Form
            this.Text = "Zalo Pro - Siêu Nhẹ (WebView2 Edition)";
            this.Size = new Size(1200, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(800, 600);

            // Tray Menu
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Mở Zalo", null, (s, e) => RestoreFromTray());
            trayMenu.Items.Add("Bật/Tắt Dark Mode", null, async (s, e) => await ToggleDarkMode());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Thoát hoàn toàn", null, (s, e) => {
                isClosingExplicitly = true;
                Application.Exit();
            });

            // Tray Icon
            trayIcon = new NotifyIcon
            {
                Text = "Zalo Pro - Đang chạy nền",
                Icon = SystemIcons.Application,
                ContextMenuStrip = trayMenu,
                Visible = true
            };
            trayIcon.DoubleClick += (s, e) => RestoreFromTray();

            // Khởi tạo WebView2
            webView = new WebView2
            {
                Dock = DockStyle.Fill
            };
            this.Controls.Add(webView);

            this.Load += async (s, e) => await InitWebViewAsync();
            this.FormClosing += Form1_FormClosing;
        }

        private async System.Threading.Tasks.Task InitWebViewAsync()
        {
            try
            {
                // Thư mục lưu dữ liệu Zalo vĩnh viễn (Cookie, Lịch sử, Ảnh cache)
                string dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ZaloProData"
                );
                Directory.CreateDirectory(dataDir);

                var options = new CoreWebView2EnvironmentOptions();
                var env = await CoreWebView2Environment.CreateAsync(null, dataDir, options);

                await webView.EnsureCoreWebView2Async(env);

                // Tự động cấp quyền Micro, Camera, Thông báo cho Zalo để gọi điện không bị chặn
                webView.CoreWebView2.PermissionRequested += (s, args) =>
                {
                    if (args.PermissionKind == CoreWebView2PermissionKind.Notifications ||
                        args.PermissionKind == CoreWebView2PermissionKind.Microphone ||
                        args.PermissionKind == CoreWebView2PermissionKind.Camera)
                    {
                        args.State = CoreWebView2PermissionState.Allow;
                    }
                };

                // Tối ưu cài đặt
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = true;

                // Khi tải xong trang -> Tự động chèn hỗ trợ font và giao diện
                webView.CoreWebView2.DOMContentLoaded += async (s, args) =>
                {
                    // Chèn script fix giao diện và tối ưu mượt mà
                    string js = @"
                        console.log('Zalo Pro Wrapper Loaded');
                    ";
                    await webView.ExecuteScriptAsync(js);
                };

                // Mở Zalo Web chính chủ
                webView.Source = new Uri("https://chat.zalo.me");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khởi tạo WebView2: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async System.Threading.Tasks.Task ToggleDarkMode()
        {
            if (webView?.CoreWebView2 == null) return;

            isDarkMode = !isDarkMode;
            if (isDarkMode)
            {
                string darkCss = @"
                    (function() {
                        let style = document.getElementById('zadark-style');
                        if (!style) {
                            style = document.createElement('style');
                            style.id = 'zadark-style';
                            style.innerHTML = `
                                html { filter: invert(90%) hue-rotate(180deg) !important; background: #121212 !important; }
                                img, video, [style*='background-image'], .avatar, canvas, .emoji { filter: invert(100%) hue-rotate(180deg) !important; }
                            `;
                            document.head.appendChild(style);
                        }
                    })();
                ";
                await webView.ExecuteScriptAsync(darkCss);
            }
            else
            {
                string removeDark = @"
                    (function() {
                        let style = document.getElementById('zadark-style');
                        if (style) style.remove();
                    })();
                ";
                await webView.ExecuteScriptAsync(removeDark);
            }
        }

        private void RestoreFromTray()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
            this.Activate();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!isClosingExplicitly && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                trayIcon.ShowBalloonTip(2000, "Zalo Pro", "Zalo đang chạy ngầm trong khay hệ thống.", ToolTipIcon.Info);
            }
            else
            {
                trayIcon?.Dispose();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Phím tắt Ctrl + Shift + D để bật/tắt Dark Mode
            if (keyData == (Keys.Control | Keys.Shift | Keys.D))
            {
                _ = ToggleDarkMode();
                return true;
            }
            // F12 mở DevTools
            if (keyData == Keys.F12 && webView?.CoreWebView2 != null)
            {
                webView.CoreWebView2.OpenDevToolsWindow();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
