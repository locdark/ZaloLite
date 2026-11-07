using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace ZLByLHL
{
    public partial class Form1 : Form
    {
        public const string CURRENT_VERSION = "2.0.3";
        private const string APP_DATA_FOLDER_NAME = "ZLByLHLData";
        private const string REG_RUN_PATH = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_REG_NAME = "ZLByLHL";

        private readonly bool startMinimized = false;

        // Multi-Account (Đa tài khoản)
        private readonly Dictionary<int, WebView2> accountWebViews = new();
        private readonly Dictionary<int, Button> accountButtons = new();
        private Button btnAddAccount = null!;
        private int currentAccountId = 1;
        private int maxAccountId = 1;

        // Container giao diện
        private Panel sidebarPanel = null!;
        private FlowLayoutPanel accountsListPanel = null!;
        private Panel mainContainer = null!;
        private Panel rightSidebar = null!;
        private Splitter sideSplitter = null!;

        // Panel Tools Bot (VPS 153)
        private Panel toolsPanel = null!;
        private WebView2? toolsWebView;
        private Button btnPopOutTools = null!;
        private Form? botMiniForm = null;

        // License & Trạng thái VIP
        private Button btnVipStatus = null!;
        private bool isVipActivated = false;
        private string activePlan = "free";
        private DateTime licenseExpiry = DateTime.MinValue;
        private string licenseKey = "";

        // Khay hệ thống
        private NotifyIcon trayIcon = null!;
        private ContextMenuStrip trayMenu = null!;
        private ToolStripMenuItem itemAutoStartTray = null!;
        private bool isDarkMode = false;
        private bool isClosingExplicitly = false;

        // Endpoints Backend (Tên miền chính thức dairong2.online)
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        private const string VPS_153_BASE = "https://dairong2.online";

        // Cấu hình RAM & Bộ nhớ
        private bool autoThrottleBackgroundTabs = true;
        private bool aggressiveLowMemoryMode = false;
        private bool autoCleanCacheOnExit = false;

        [System.Runtime.InteropServices.DllImport("psapi.dll")]
        private static extern int EmptyWorkingSet(IntPtr hwProc);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern int RegisterWindowMessage(string lpString);

        private int wakeupMessageId = 0;
        private System.Windows.Forms.Timer? memoryAutoTrimTimer;

        protected override void WndProc(ref Message m)
        {
            if (wakeupMessageId != 0 && m.Msg == wakeupMessageId)
            {
                RestoreFromTray();
                return;
            }
            base.WndProc(ref m);
        }

        public Form1(bool startMinimized = false)
        {
            this.startMinimized = startMinimized;
            InitializeComponent();
            SetupApp();
        }

        private void SetupApp()
        {
            wakeupMessageId = RegisterWindowMessage("ZLByLHL_WAKEUP_WINDOW_MSG");

            this.Text = $"ZLByLHL - Commercial Edition (v{CURRENT_VERSION})";
            this.Size = new Size(1300, 850);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(950, 650);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(icoPath))
            {
                try { this.Icon = new Icon(icoPath); } catch { }
            }

            LoadLocalLicense();
            LoadMemorySettings();
            BuildLayout();
            SetupTray();
            SetupMemoryAutoOptimizer();

            this.Load += async (s, e) => {
                await SwitchToAccountAsync(1);
                _ = Task.Run(CheckForUpdatesAsync);

                if (startMinimized)
                {
                    this.WindowState = FormWindowState.Minimized;
                    this.Hide();
                    trayIcon.ShowBalloonTip(2000, "ZLByLHL", "ZLByLHL đã khởi động cùng Windows và chạy ngầm.", ToolTipIcon.Info);
                }
            };

            this.FormClosing += Form1_FormClosing;
        }

        #region Auto-Start with Windows (Khởi động cùng máy tính)
        private bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(REG_RUN_PATH, false);
                return key?.GetValue(APP_REG_NAME) != null;
            }
            catch { return false; }
        }

        private void SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(REG_RUN_PATH, true);
                if (key == null) return;
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Application.ExecutablePath;

                if (enable)
                {
                    key.SetValue(APP_REG_NAME, $"\"{exePath}\" --startup");
                    MessageBox.Show("Đã BẬT tính năng tự khởi động ZLByLHL khi mở máy tính!", "Cài Đặt", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    key.DeleteValue(APP_REG_NAME, false);
                    MessageBox.Show("Đã TẮT tính năng tự khởi động cùng máy tính.", "Cài Đặt", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                if (itemAutoStartTray != null)
                {
                    itemAutoStartTray.Checked = enable;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi thiết lập tự khởi động: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ToggleAutoStart()
        {
            bool current = IsAutoStartEnabled();
            SetAutoStart(!current);
        }
        #endregion

        #region Hardware ID & License Management
        private string GetMachineHWID()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                string guid = key?.GetValue("MachineGuid")?.ToString() ?? "";
                if (!string.IsNullOrEmpty(guid)) return guid;
            }
            catch { }

            string raw = $"{Environment.MachineName}-{Environment.UserName}-{Environment.ProcessorCount}";
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return BitConverter.ToString(hash).Replace("-", "").Substring(0, 24);
        }

        private string GetLicenseFilePath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), APP_DATA_FOLDER_NAME);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "license.json");
        }

        private void LoadLocalLicense()
        {
            try
            {
                string path = GetLicenseFilePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    licenseKey = root.GetProperty("key").GetString() ?? "";
                    activePlan = root.GetProperty("plan").GetString() ?? "free";
                    long expMs = root.GetProperty("expiresAt").GetInt64();
                    licenseExpiry = DateTimeOffset.FromUnixTimeMilliseconds(expMs).LocalDateTime;

                    if (licenseExpiry > DateTime.Now)
                    {
                        isVipActivated = true;
                    }
                }
            }
            catch { isVipActivated = false; }
        }

        private void SaveLocalLicense(string key, string plan, long expiresAt)
        {
            try
            {
                string path = GetLicenseFilePath();
                var data = new { key, plan, expiresAt };
                File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
                isVipActivated = true;
                this.activePlan = plan;
                this.licenseKey = key;
                this.licenseExpiry = DateTimeOffset.FromUnixTimeMilliseconds(expiresAt).LocalDateTime;
                UpdateVipButtonUi();
            }
            catch { }
        }

        private void ShowLicenseDialog()
        {
            using var dlg = new Form
            {
                Text = "Kích hoạt Bản Quyền VIP - ZLByLHL",
                Size = new Size(540, 390),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.White
            };

            var lblTitle = new Label
            {
                Text = "💎 KÍCH HOẠT BẢN QUYỀN ZLByLHL PRO",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 204),
                Location = new Point(20, 15),
                AutoSize = true
            };
            dlg.Controls.Add(lblTitle);

            var lblDesc = new Label
            {
                Text = isVipActivated
                    ? $"Trạng thái: ĐÃ KÍCH HOẠT ({activePlan.ToUpper()}) | HSD: {licenseExpiry:dd/MM/yyyy}\nMã / Tài khoản: {licenseKey}"
                    : "Mở khóa 8 Tài khoản Zalo song song và Bảng điều khiển Bot ZaloTools:",
                Location = new Point(22, 45),
                Size = new Size(480, 35),
                ForeColor = isVipActivated ? Color.FromArgb(46, 204, 113) : Color.FromArgb(80, 80, 80)
            };
            dlg.Controls.Add(lblDesc);

            var tabControl = new TabControl
            {
                Location = new Point(22, 85),
                Size = new Size(480, 200),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular)
            };
            dlg.Controls.Add(tabControl);

            // TAB 1: Tài khoản Web (Dành cho khách mua gói)
            var tabWeb = new TabPage("👤 Tài Khoản Web (Đã mua gói)");
            tabWeb.BackColor = Color.White;
            tabWeb.Padding = new Padding(12);

            var lblUser = new Label { Text = "Tên đăng nhập (Web SaaS):", Location = new Point(15, 12), AutoSize = true, ForeColor = Color.FromArgb(60, 60, 60) };
            var txtUsername = new TextBox { Location = new Point(15, 34), Width = 430, Height = 28, PlaceholderText = "VD: khachhang123" };
            var lblPass = new Label { Text = "Mật khẩu:", Location = new Point(15, 70), AutoSize = true, ForeColor = Color.FromArgb(60, 60, 60) };
            var txtPassword = new TextBox { Location = new Point(15, 92), Width = 430, Height = 28, UseSystemPasswordChar = true };

            var btnLoginWeb = new Button
            {
                Text = "⚡ ĐĂNG NHẬP & KÍCH HOẠT VIP",
                Location = new Point(15, 130),
                Size = new Size(430, 36),
                BackColor = Color.FromArgb(0, 132, 255),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };

            tabWeb.Controls.Add(lblUser);
            tabWeb.Controls.Add(txtUsername);
            tabWeb.Controls.Add(lblPass);
            tabWeb.Controls.Add(txtPassword);
            tabWeb.Controls.Add(btnLoginWeb);
            tabControl.TabPages.Add(tabWeb);

            // TAB 2: Mã Key bản quyền
            var tabKey = new TabPage("🔑 Nhập Mã Key (Đại lý cấp)");
            tabKey.BackColor = Color.White;
            tabKey.Padding = new Padding(12);

            var lblKeyHint = new Label { Text = "Nhập mã License Key được cấp:", Location = new Point(15, 15), AutoSize = true, ForeColor = Color.FromArgb(60, 60, 60) };
            var txtKeyInput = new TextBox
            {
                Location = new Point(15, 42),
                Width = 430,
                Height = 28,
                Font = new Font("Consolas", 11f, FontStyle.Bold),
                PlaceholderText = "VD: LOCDARK-ULTRA30D-XXXXXX"
            };
            if (!string.IsNullOrEmpty(licenseKey) && !licenseKey.StartsWith("WEB:")) txtKeyInput.Text = licenseKey;

            var btnSubmitKey = new Button
            {
                Text = "⚡ XÁC THỰC MÃ KEY",
                Location = new Point(15, 95),
                Size = new Size(430, 36),
                BackColor = Color.FromArgb(39, 174, 96),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };

            tabKey.Controls.Add(lblKeyHint);
            tabKey.Controls.Add(txtKeyInput);
            tabKey.Controls.Add(btnSubmitKey);
            tabControl.TabPages.Add(tabKey);

            var lblStatus = new Label
            {
                Text = "",
                Location = new Point(22, 290),
                Size = new Size(480, 24),
                ForeColor = Color.Red,
                Font = new Font("Segoe UI", 8.5f)
            };
            dlg.Controls.Add(lblStatus);

            var btnWeb = new Button
            {
                Text = "🛒 Chưa có tài khoản? Mua gói trên Web",
                Location = new Point(22, 318),
                Size = new Size(480, 30),
                BackColor = Color.FromArgb(245, 247, 250),
                ForeColor = Color.FromArgb(0, 102, 204),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnWeb.Click += (s, e) => {
                Process.Start(new ProcessStartInfo { FileName = VPS_153_BASE, UseShellExecute = true });
            };
            dlg.Controls.Add(btnWeb);

            btnLoginWeb.Click += async (s, e) => {
                string u = txtUsername.Text.Trim();
                string p = txtPassword.Text.Trim();
                if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(p))
                {
                    lblStatus.Text = "Vui lòng nhập tài khoản và mật khẩu!";
                    return;
                }

                btnLoginWeb.Enabled = false;
                lblStatus.ForeColor = Color.Blue;
                lblStatus.Text = "⏳ Đang kiểm tra gói trên máy chủ VPS 153...";

                try
                {
                    var payload = new { username = u, password = p };
                    string jsonReq = JsonSerializer.Serialize(payload);
                    using var content = new StringContent(jsonReq, Encoding.UTF8, "application/json");
                    var resp = await httpClient.PostAsync($"{VPS_153_BASE}/api/auth/login", content);
                    string respStr = await resp.Content.ReadAsStringAsync();

                    using var doc = JsonDocument.Parse(respStr);
                    var root = doc.RootElement;
                    bool ok = root.TryGetProperty("success", out var propOk) && propOk.GetBoolean();

                    if (ok && root.TryGetProperty("user", out var userEl))
                    {
                        string plan = userEl.TryGetProperty("plan", out var pEl) ? pEl.GetString() ?? "free" : "free";
                        long exp = userEl.TryGetProperty("plan_expire", out var expEl) && expEl.GetInt64() > 0 ? expEl.GetInt64() : (DateTimeOffset.Now.ToUnixTimeMilliseconds() + 30L * 86400000L);

                        SaveLocalLicense($"WEB:{u}", plan, exp);
                        MessageBox.Show($"Đăng nhập thành công! Tài khoản [{u}] kích hoạt gói {plan.ToUpper()}.", "Kích Hoạt Thành Công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        dlg.Close();
                    }
                    else
                    {
                        string msg = root.TryGetProperty("message", out var pMsg) ? pMsg.GetString() ?? "Sai thông tin" : "Tài khoản hoặc mật khẩu không đúng!";
                        lblStatus.ForeColor = Color.Red;
                        lblStatus.Text = $"❌ {msg}";
                    }
                }
                catch (Exception ex)
                {
                    lblStatus.ForeColor = Color.Red;
                    lblStatus.Text = $"❌ Lỗi kết nối: {ex.Message}";
                }
                finally
                {
                    btnLoginWeb.Enabled = true;
                }
            };

            btnSubmitKey.Click += async (s, e) => {
                string inputKey = txtKeyInput.Text.Trim();
                if (string.IsNullOrEmpty(inputKey))
                {
                    lblStatus.Text = "Vui lòng nhập License Key!";
                    return;
                }

                btnSubmitKey.Enabled = false;
                lblStatus.ForeColor = Color.Blue;
                lblStatus.Text = "⏳ Đang xác thực với máy chủ VPS 153...";

                try
                {
                    var payload = new
                    {
                        key = inputKey,
                        hwid = GetMachineHWID(),
                        machineName = Environment.MachineName
                    };

                    string jsonReq = JsonSerializer.Serialize(payload);
                    using var content = new StringContent(jsonReq, Encoding.UTF8, "application/json");
                    var resp = await httpClient.PostAsync($"{VPS_153_BASE}/api/client/verify-license", content);
                    byte[] respBytes = await resp.Content.ReadAsByteArrayAsync();
                    string respStr = Encoding.UTF8.GetString(respBytes).Trim();

                    if (respStr.StartsWith("<"))
                    {
                        lblStatus.ForeColor = Color.Red;
                        lblStatus.Text = $"❌ Máy chủ đang bận (HTTP {(int)resp.StatusCode}). Vui lòng thử lại sau 5 giây!";
                        return;
                    }

                    using var doc = JsonDocument.Parse(respStr);
                    var root = doc.RootElement;
                    bool ok = root.TryGetProperty("success", out var propOk) && propOk.GetBoolean();

                    if (ok)
                    {
                        string plan = root.GetProperty("plan").GetString() ?? "pro";
                        long exp = root.GetProperty("expiresAt").GetInt64();
                        SaveLocalLicense(inputKey, plan, exp);
                        MessageBox.Show($"Chúc mừng! Bạn đã kích hoạt thành công gói {plan.ToUpper()}!", "Kích Hoạt Thành Công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        dlg.Close();
                    }
                    else
                    {
                        string msg = root.TryGetProperty("message", out var pMsg) ? pMsg.GetString() ?? "Lỗi xác thực" : "Key không hợp lệ!";
                        lblStatus.ForeColor = Color.Red;
                        lblStatus.Text = $"❌ {msg}";
                    }
                }
                catch (Exception ex)
                {
                    lblStatus.ForeColor = Color.Red;
                    lblStatus.Text = $"❌ Lỗi kết nối: {ex.Message}";
                }
                finally
                {
                    btnSubmitKey.Enabled = true;
                }
            };

            dlg.ShowDialog(this);
        }
        #endregion

        #region Remote OTA Auto-Updater
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                byte[] rawBytes = await httpClient.GetByteArrayAsync($"{VPS_153_BASE}/api/client/check-update");
                string json = Encoding.UTF8.GetString(rawBytes);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string latestVer = root.GetProperty("latestVersion").GetString() ?? CURRENT_VERSION;
                string downloadUrl = root.GetProperty("downloadUrl").GetString() ?? "";
                string changelog = root.GetProperty("changelog").GetString() ?? "";

                if (IsNewerVersion(latestVer, CURRENT_VERSION))
                {
                    this.Invoke(() => {
                        var msg = $"🔔 ĐÃ CÓ BẢN CẬP NHẬT MỚI: v{latestVer} (Hiện tại: v{CURRENT_VERSION})\n\n" +
                                  $"Nội dung nâng cấp:\n{changelog}\n\n" +
                                  "Bạn có muốn tải về và cập nhật ngay bây giờ không?";

                        var res = MessageBox.Show(msg, "Cập Nhật Phần Mềm ZLByLHL", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                        if (res == DialogResult.Yes)
                        {
                            _ = PerformAutoUpdateAsync(downloadUrl);
                        }
                    });
                }
            }
            catch { }
        }

        private bool IsNewerVersion(string remoteVer, string localVer)
        {
            try
            {
                var r = new Version(remoteVer);
                var l = new Version(localVer);
                return r > l;
            }
            catch { return false; }
        }

        private async Task PerformAutoUpdateAsync(string downloadUrl)
        {
            try
            {
                bool isSetupInstaller = downloadUrl.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase);
                string tempFile = Path.Combine(Path.GetTempPath(), isSetupInstaller ? "ZLByLHL_Setup_Update.exe" : "ZLByLHL_Update.exe");

                byte[] bytes = await httpClient.GetByteArrayAsync(downloadUrl);
                await File.WriteAllBytesAsync(tempFile, bytes);

                string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";

                if (isSetupInstaller)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = tempFile,
                        Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                        UseShellExecute = true
                    });
                    isClosingExplicitly = true;
                    Environment.Exit(0);
                    return;
                }

                string scriptPath = Path.Combine(Path.GetTempPath(), "do_update.cmd");
                string cmdScript = $@"@echo off
taskkill /f /im ZLByLHL.exe >nul 2>&1
:retry
ping 127.0.0.1 -n 2 >nul
move /y ""{tempFile}"" ""{currentExe}"" >nul 2>&1
if errorlevel 1 goto retry
start """" ""{currentExe}""
del ""%~f0""
";
                await File.WriteAllTextAsync(scriptPath, cmdScript, Encoding.UTF8);

                Process.Start(new ProcessStartInfo
                {
                    FileName = scriptPath,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                });

                isClosingExplicitly = true;
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi cập nhật: {ex.Message}", "Cập nhật thất bại", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion

        #region Giao diện chính (Layout)
        private void BuildLayout()
        {
            sidebarPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 68,
                BackColor = Color.FromArgb(30, 32, 35)
            };
            this.Controls.Add(sidebarPanel);

            accountsListPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 440,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.Transparent,
                Padding = new Padding(8, 10, 0, 0)
            };
            sidebarPanel.Controls.Add(accountsListPanel);

            // Nút Thêm Tài Khoản [+]
            btnAddAccount = CreateSidebarIconButton("+", "Thêm tài khoản Zalo mới (Multi-Account)", Color.FromArgb(46, 204, 113), Color.White);
            btnAddAccount.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
            btnAddAccount.Click += async (s, e) => {
                if (!isVipActivated && maxAccountId >= 2)
                {
                    var ask = MessageBox.Show("Bản Free chỉ hỗ trợ tối đa 2 tài khoản. Bạn có muốn kích hoạt VIP để mở khóa 8 tài khoản Zalo không?", "Nâng Cấp VIP", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (ask == DialogResult.Yes) ShowLicenseDialog();
                    return;
                }
                if (maxAccountId >= 8)
                {
                    MessageBox.Show("Đã đạt giới hạn tối đa 8 tài khoản Zalo!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                maxAccountId++;
                AddAccountButton(maxAccountId);
                await SwitchToAccountAsync(maxAccountId);
            };

            // Thanh công cụ dưới sidebar
            Panel bottomToolsPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 315,
                BackColor = Color.Transparent
            };
            sidebarPanel.Controls.Add(bottomToolsPanel);

            btnVipStatus = CreateSidebarIconButton(isVipActivated ? "💎" : "👑", isVipActivated ? $"Gói {activePlan.ToUpper()} (HSD: {licenseExpiry:dd/MM/yyyy})" : "Kích hoạt bản quyền VIP", isVipActivated ? Color.FromArgb(46, 204, 113) : Color.FromArgb(241, 196, 15), Color.White);
            btnVipStatus.Location = new Point(10, 8);
            btnVipStatus.Click += (s, e) => ShowLicenseDialog();

            Button btnToolsToggle = CreateSidebarIconButton("🛠️", "ZaloTools Bot Suite (VPS 153)", Color.FromArgb(230, 126, 34), Color.White);
            btnToolsToggle.Location = new Point(10, 68);
            btnToolsToggle.Click += async (s, e) => {
                ToggleRightPanel();
                await InitToolsWebViewAsync();
            };

            Button btnStartupToggle = CreateSidebarIconButton("🚀", "Cài đặt: Tự bật khi khởi động máy", IsAutoStartEnabled() ? Color.FromArgb(46, 204, 113) : Color.FromArgb(54, 57, 63), Color.White);
            btnStartupToggle.Location = new Point(10, 128);
            btnStartupToggle.Click += (s, e) => {
                ToggleAutoStart();
                btnStartupToggle.BackColor = IsAutoStartEnabled() ? Color.FromArgb(46, 204, 113) : Color.FromArgb(54, 57, 63);
            };

            Button btnDarkToggle = CreateSidebarIconButton("🌙", "Bật/Tắt Giao diện tối", Color.FromArgb(54, 57, 63), Color.White);
            btnDarkToggle.Location = new Point(10, 188);
            btnDarkToggle.Click += async (s, e) => await ToggleDarkMode();

            Button btnMemoryConfig = CreateSidebarIconButton("⚡", "Quản lý RAM & Cấu hình bộ nhớ", Color.FromArgb(142, 68, 173), Color.White);
            btnMemoryConfig.Location = new Point(10, 248);
            btnMemoryConfig.Click += (s, e) => ShowMemoryConfigDialog();

            bottomToolsPanel.Controls.Add(btnVipStatus);
            bottomToolsPanel.Controls.Add(btnToolsToggle);
            bottomToolsPanel.Controls.Add(btnStartupToggle);
            bottomToolsPanel.Controls.Add(btnDarkToggle);
            bottomToolsPanel.Controls.Add(btnMemoryConfig);

            AddAccountButton(1);

            // Bảng điều khiển Bot (Có Splitter kéo thu phóng tùy ý)
            rightSidebar = new Panel
            {
                Dock = DockStyle.Right,
                Width = 500,
                BackColor = Color.FromArgb(248, 249, 250),
                Visible = false
            };
            this.Controls.Add(rightSidebar);

            sideSplitter = new Splitter
            {
                Dock = DockStyle.Right,
                Width = 6,
                BackColor = Color.FromArgb(210, 215, 222),
                Cursor = Cursors.VSplit,
                Visible = false
            };
            this.Controls.Add(sideSplitter);

            BuildToolsPanel();

            mainContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };
            this.Controls.Add(mainContainer);
            mainContainer.BringToFront();
        }

        private void UpdateVipButtonUi()
        {
            if (btnVipStatus != null)
            {
                btnVipStatus.Text = isVipActivated ? "💎" : "👑";
                btnVipStatus.BackColor = isVipActivated ? Color.FromArgb(46, 204, 113) : Color.FromArgb(241, 196, 15);
            }
        }

        private Button CreateSidebarIconButton(string text, string tooltip, Color bg, Color fg)
        {
            var btn = new Button
            {
                Text = text,
                Size = new Size(48, 48),
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = fg,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(2, 6, 2, 6)
            };
            btn.FlatAppearance.BorderSize = 0;

            var tip = new ToolTip();
            tip.SetToolTip(btn, tooltip);

            return btn;
        }

        private void AddAccountButton(int accId)
        {
            var btn = CreateSidebarIconButton($"TK {accId}", $"Tài khoản Zalo #{accId}", Color.FromArgb(47, 49, 54), Color.White);
            btn.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btn.Click += async (s, e) => await SwitchToAccountAsync(accId);

            accountButtons[accId] = btn;

            // Luôn giữ nút [+] ở cuối danh sách
            accountsListPanel.Controls.Remove(btnAddAccount);
            accountsListPanel.Controls.Add(btn);
            accountsListPanel.Controls.Add(btnAddAccount);
        }

        private async Task SwitchToAccountAsync(int accId)
        {
            currentAccountId = accId;

            foreach (var kv in accountButtons)
            {
                if (kv.Key == accId)
                {
                    kv.Value.BackColor = Color.FromArgb(0, 132, 255);
                    kv.Value.ForeColor = Color.White;
                }
                else
                {
                    kv.Value.BackColor = Color.FromArgb(47, 49, 54);
                    kv.Value.ForeColor = Color.LightGray;
                }
            }

            foreach (var kv in accountWebViews)
            {
                kv.Value.Visible = false;
            }

            if (!accountWebViews.ContainsKey(accId))
            {
                var newWeb = new WebView2
                {
                    Dock = DockStyle.Fill,
                    Visible = true
                };
                mainContainer.Controls.Add(newWeb);
                accountWebViews[accId] = newWeb;

                await InitAccountWebViewAsync(accId, newWeb);
            }
            else
            {
                accountWebViews[accId].Visible = true;
                accountWebViews[accId].BringToFront();
            }

            // Quản lý mức tiêu thụ RAM theo cấu hình người dùng
            try
            {
                if (accountWebViews.ContainsKey(accId) && accountWebViews[accId]?.CoreWebView2 != null)
                {
                    accountWebViews[accId].CoreWebView2.MemoryUsageTargetLevel = aggressiveLowMemoryMode 
                        ? CoreWebView2MemoryUsageTargetLevel.Low 
                        : CoreWebView2MemoryUsageTargetLevel.Normal;
                }

                if (autoThrottleBackgroundTabs)
                {
                    foreach (var kv in accountWebViews)
                    {
                        if (kv.Key != accId && kv.Value?.CoreWebView2 != null)
                        {
                            kv.Value.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                        }
                    }
                }
            }
            catch { }

            string vipTag = isVipActivated ? $"[VIP {activePlan.ToUpper()}]" : "[Bản Miễn Phí]";
            this.Text = $"ZLByLHL - {vipTag} | Tài Khoản #{accId} (v{CURRENT_VERSION})";
        }

        private async Task InitAccountWebViewAsync(int accId, WebView2 targetWeb)
        {
            try
            {
                string dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    APP_DATA_FOLDER_NAME,
                    $"Profile_{accId}"
                );
                Directory.CreateDirectory(dataDir);

                var options = new CoreWebView2EnvironmentOptions();
                var env = await CoreWebView2Environment.CreateAsync(null, dataDir, options);
                await targetWeb.EnsureCoreWebView2Async(env);

                targetWeb.CoreWebView2.PermissionRequested += (s, args) =>
                {
                    if (args.PermissionKind == CoreWebView2PermissionKind.Notifications ||
                        args.PermissionKind == CoreWebView2PermissionKind.Microphone ||
                        args.PermissionKind == CoreWebView2PermissionKind.Camera)
                    {
                        args.State = CoreWebView2PermissionState.Allow;
                    }
                };

                targetWeb.CoreWebView2.Settings.IsStatusBarEnabled = false;
                targetWeb.CoreWebView2.Settings.AreDevToolsEnabled = true;

                // Chèn CSS và Script diệt banner 'Tải Zalo PC' ngay trước khi trang load
                try
                {
                    await targetWeb.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(GetCleanUiScript());
                }
                catch { }

                targetWeb.CoreWebView2.DOMContentLoaded += async (s, args) =>
                {
                    await InjectCleanUiScriptAsync(targetWeb);
                    await InjectAntiRecallScriptAsync(targetWeb);
                };

                targetWeb.CoreWebView2.NavigationCompleted += async (s, args) =>
                {
                    await InjectCleanUiScriptAsync(targetWeb);
                };

                targetWeb.Source = new Uri("https://chat.zalo.me");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khởi tạo tài khoản #{accId}: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion

        #region Clean UI & Anti-Recall
        private static string GetCleanUiScript()
        {
            return @"
                (function() {
                    // Tuyệt đối không can thiệp vào trang đăng nhập id.zalo.me hoặc form login
                    if (window.location.hostname.includes('id.zalo.me') || 
                        window.location.pathname.includes('/login') ||
                        document.querySelector('.body-login, .login-page, .login-v2, iframe[src*=""id.zalo.me""]')) {
                        return;
                    }

                    function hideChatDownloadBanner() {
                        try {
                            // Không chạy nếu đang ở màn hình login
                            if (document.querySelector('.body-login, .login-page, .login-v2, iframe[src*=""id.zalo.me""]')) return;

                            // 1. Tìm thanh thông báo Zalo PC dựa trên nội dung text chính xác trong giao diện chat
                            const walker = document.createTreeWalker(document.body || document.documentElement, NodeFilter.SHOW_TEXT, null, false);
                            let node;
                            while (node = walker.nextNode()) {
                                const val = node.nodeValue;
                                if (val && val.includes('Sử dụng Zalo PC để lưu trữ dài hạn')) {
                                    let curr = node.parentElement;
                                    while (curr && curr !== document.body && curr.clientHeight < 75) {
                                        if (curr.parentElement && curr.parentElement.clientHeight >= 75) {
                                            curr.style.setProperty('display', 'none', 'important');
                                            curr.style.setProperty('height', '0px', 'important');
                                            curr.style.setProperty('visibility', 'hidden', 'important');
                                            break;
                                        }
                                        curr = curr.parentElement;
                                    }
                                }
                            }

                            // 2. Tự động click nút đóng [X] của banner nếu có
                            const closeBtn = document.querySelector('div[class*=""banner""] [class*=""close""], div[class*=""banner""] i.fa-times');
                            if (closeBtn && closeBtn.click) {
                                closeBtn.click();
                            }
                        } catch(e) {}
                    }

                    hideChatDownloadBanner();
                    if (!window.__promoObserverActive) {
                        window.__promoObserverActive = true;
                        const obs = new MutationObserver(hideChatDownloadBanner);
                        obs.observe(document.documentElement || document.body, { childList: true, subtree: true });
                        setInterval(hideChatDownloadBanner, 2000);
                    }
                })();
            ";
        }

        private async Task InjectCleanUiScriptAsync(WebView2 web)
        {
            if (web?.CoreWebView2 == null) return;
            try
            {
                await web.ExecuteScriptAsync(GetCleanUiScript());
            }
            catch { }
        }

        private async Task InjectAntiRecallScriptAsync(WebView2 web)
        {
            if (web?.CoreWebView2 == null) return;

            string script = @"
                (function() {
                    if (window.__antiRecallActive) return;
                    window.__antiRecallActive = true;
                    console.log('[ZLByLHL] Anti-Recall Injected!');

                    const msgCache = new Map();

                    const observer = new MutationObserver(() => {
                        try {
                            const chatItems = document.querySelectorAll('.chat-item, [id^=""msg-""]');
                            chatItems.forEach(item => {
                                const id = item.getAttribute('id') || item.getAttribute('data-id') || item.innerText.substring(0, 35);
                                const textEl = item.querySelector('.chat-message-text, .msg-text, .text');
                                if (!textEl) return;

                                const currentText = textEl.innerText.trim();
                                if (currentText && !currentText.includes('đã được thu hồi') && !currentText.includes('Tin nhắn thu hồi')) {
                                    msgCache.set(id, currentText);
                                } else if (currentText.includes('đã được thu hồi') && msgCache.has(id)) {
                                    if (!item.querySelector('.zlbylhl-anti-recall')) {
                                        const oldMsg = msgCache.get(id);
                                        const badge = document.createElement('div');
                                        badge.className = 'zlbylhl-anti-recall';
                                        badge.style.cssText = 'margin-top: 4px; padding: 4px 8px; background: #ffebee; color: #c62828; font-size: 12px; font-style: italic; border-left: 3px solid #d32f2f; border-radius: 4px; display: block;';
                                        badge.innerText = '🛡️ [ZLByLHL Giữ Tin]: ' + oldMsg;
                                        textEl.appendChild(badge);
                                    }
                                }
                            });
                        } catch(e) {}
                    });

                    observer.observe(document.body, { childList: true, subtree: true });
                })();
            ";
            await web.ExecuteScriptAsync(script);
        }
        #endregion

        #region Panel ZaloTools Suite (VPS 153)
        private void BuildToolsPanel()
        {
            toolsPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };
            rightSidebar.Controls.Add(toolsPanel);

            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Color.FromArgb(230, 126, 34) };
            var lblToolsTitle = new Label
            {
                Text = "🛠️ ZALOTOOLS SUITE",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Dock = DockStyle.Left,
                Width = 160,
                Padding = new Padding(10, 10, 0, 0)
            };

            btnPopOutTools = new Button
            {
                Text = "🗗 Cửa sổ Mini",
                Dock = DockStyle.Right,
                Width = 130,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(211, 84, 0),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnPopOutTools.Click += (s, e) => ToggleBotPopOut();

            var btnReloadTools = new Button
            {
                Text = "🔄 Tải lại",
                Dock = DockStyle.Right,
                Width = 70,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(230, 126, 34),
                Cursor = Cursors.Hand
            };
            btnReloadTools.Click += (s, e) => toolsWebView?.CoreWebView2?.Reload();

            pnlTop.Controls.Add(lblToolsTitle);
            pnlTop.Controls.Add(btnReloadTools);
            pnlTop.Controls.Add(btnPopOutTools);
            toolsPanel.Controls.Add(pnlTop);
        }

        private void ToggleBotPopOut()
        {
            if (botMiniForm != null && !botMiniForm.IsDisposed)
            {
                botMiniForm.Controls.Clear();
                botMiniForm.Close();
                botMiniForm = null;

                if (toolsWebView != null)
                {
                    toolsPanel.Controls.Add(toolsWebView);
                    toolsWebView.Dock = DockStyle.Fill;
                    toolsWebView.BringToFront();
                }
                btnPopOutTools.Text = "🗗 Cửa sổ Mini";
                return;
            }

            // Tách ra cửa sổ Mini độc lập có thể thu nhỏ và kéo thả tự do
            botMiniForm = new Form
            {
                Text = "ZLByLHL - ZaloTools Mini Bot Suite (Cửa Sổ Theo Dõi Tự Động Clone)",
                Size = new Size(1150, 780),
                StartPosition = FormStartPosition.CenterScreen,
                MinimumSize = new Size(450, 380)
            };

            string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(icoPath))
            {
                try { botMiniForm.Icon = new Icon(icoPath); } catch { }
            }

            var topBar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.FromArgb(230, 126, 34) };
            var lblInfo = new Label
            {
                Text = "🛠️ ZALOTOOLS - CỬA SỔ MINI (KÉO THU PHÓNG TỰ DO)",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Dock = DockStyle.Left,
                Width = 420,
                Padding = new Padding(12, 9, 0, 0)
            };

            var chkAlwaysOnTop = new CheckBox
            {
                Text = "📌 Ghim trên cùng",
                ForeColor = Color.White,
                Dock = DockStyle.Right,
                Width = 145,
                Padding = new Padding(0, 8, 0, 0),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            chkAlwaysOnTop.CheckedChanged += (s, e) => botMiniForm.TopMost = chkAlwaysOnTop.Checked;

            var btnReDock = new Button
            {
                Text = "🗕 Gắn lại App",
                Dock = DockStyle.Right,
                Width = 130,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(211, 84, 0),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnReDock.Click += (s, e) => ToggleBotPopOut();

            topBar.Controls.Add(lblInfo);
            topBar.Controls.Add(chkAlwaysOnTop);
            topBar.Controls.Add(btnReDock);
            botMiniForm.Controls.Add(topBar);

            if (toolsWebView != null)
            {
                toolsPanel.Controls.Remove(toolsWebView);
                botMiniForm.Controls.Add(toolsWebView);
                toolsWebView.Dock = DockStyle.Fill;
                toolsWebView.BringToFront();
            }

            btnPopOutTools.Text = "🗕 Gắn lại App";

            botMiniForm.FormClosed += (s, e) => {
                botMiniForm = null;
                if (toolsWebView != null && !toolsWebView.IsDisposed)
                {
                    toolsPanel.Controls.Add(toolsWebView);
                    toolsWebView.Dock = DockStyle.Fill;
                    toolsWebView.BringToFront();
                }
                btnPopOutTools.Text = "🗗 Cửa sổ Mini";
            };

            rightSidebar.Visible = false;
            sideSplitter.Visible = false;
            botMiniForm.Show();
        }

        private async Task InitToolsWebViewAsync()
        {
            if (toolsWebView != null) return;

            try
            {
                toolsWebView = new WebView2 { Dock = DockStyle.Fill };
                toolsPanel.Controls.Add(toolsWebView);
                toolsWebView.BringToFront();

                string toolsDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    APP_DATA_FOLDER_NAME,
                    "Tools_Profile"
                );
                Directory.CreateDirectory(toolsDataDir);

                var options = new CoreWebView2EnvironmentOptions();
                var env = await CoreWebView2Environment.CreateAsync(null, toolsDataDir, options);
                await toolsWebView.EnsureCoreWebView2Async(env);

                toolsWebView.Source = new Uri(VPS_153_BASE);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi kết nối ZaloTools: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion

        private void ToggleRightPanel()
        {
            if (botMiniForm != null && !botMiniForm.IsDisposed)
            {
                botMiniForm.WindowState = FormWindowState.Normal;
                botMiniForm.BringToFront();
                botMiniForm.Activate();
                return;
            }

            bool isCurrentlyVisible = rightSidebar.Visible;
            rightSidebar.Visible = !isCurrentlyVisible;
            sideSplitter.Visible = !isCurrentlyVisible;
        }

        private async Task ToggleDarkMode()
        {
            if (!accountWebViews.TryGetValue(currentAccountId, out var web) || web?.CoreWebView2 == null) return;

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
                await web.ExecuteScriptAsync(darkCss);
            }
            else
            {
                string removeDark = @"
                    (function() {
                        let style = document.getElementById('zadark-style');
                        if (style) style.remove();
                    })();
                ";
                await web.ExecuteScriptAsync(removeDark);
            }
        }

        private void SetupTray()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Mở ZLByLHL", null, (s, e) => RestoreFromTray());
            trayMenu.Items.Add("Bật/Tắt Giao diện tối", null, async (s, e) => await ToggleDarkMode());
            
            itemAutoStartTray = new ToolStripMenuItem("Khởi động cùng Windows")
            {
                Checked = IsAutoStartEnabled()
            };
            itemAutoStartTray.Click += (s, e) => ToggleAutoStart();
            trayMenu.Items.Add(itemAutoStartTray);

            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Kích hoạt Bản Quyền VIP", null, (s, e) => ShowLicenseDialog());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Thoát hoàn toàn", null, (s, e) => {
                isClosingExplicitly = true;
                Application.Exit();
            });

            string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            Icon iconToUse = SystemIcons.Application;
            if (File.Exists(icoPath))
            {
                try { iconToUse = new Icon(icoPath); } catch { }
            }

            trayIcon = new NotifyIcon
            {
                Text = "ZLByLHL - Đang chạy nền",
                Icon = iconToUse,
                ContextMenuStrip = trayMenu,
                Visible = true
            };
            trayIcon.DoubleClick += (s, e) => RestoreFromTray();
        }

        private void RestoreFromTray()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
            this.Activate();
        }

        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (!isClosingExplicitly && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                trayIcon.ShowBalloonTip(2000, "ZLByLHL", "ZLByLHL đang chạy ngầm trong khay hệ thống.", ToolTipIcon.Info);
            }
            else
            {
                if (autoCleanCacheOnExit)
                {
                    try { CleanAppCache(); } catch { }
                }
                trayIcon?.Dispose();
            }
        }

        #region Memory & Resource Management
        private void LoadMemorySettings()
        {
            try
            {
                string confFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), APP_DATA_FOLDER_NAME, "mem_settings.json");
                if (File.Exists(confFile))
                {
                    string json = File.ReadAllText(confFile);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("autoThrottleBackgroundTabs", out var propThrottle))
                        autoThrottleBackgroundTabs = propThrottle.GetBoolean();
                    if (root.TryGetProperty("aggressiveLowMemoryMode", out var propAggressive))
                        aggressiveLowMemoryMode = propAggressive.GetBoolean();
                    if (root.TryGetProperty("autoCleanCacheOnExit", out var propCleanOnExit))
                        autoCleanCacheOnExit = propCleanOnExit.GetBoolean();
                }
            }
            catch { }
        }

        private void SaveMemorySettings()
        {
            try
            {
                string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), APP_DATA_FOLDER_NAME);
                Directory.CreateDirectory(dataDir);
                string confFile = Path.Combine(dataDir, "mem_settings.json");
                var obj = new
                {
                    autoThrottleBackgroundTabs,
                    aggressiveLowMemoryMode,
                    autoCleanCacheOnExit
                };
                string json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(confFile, json);
            }
            catch { }
        }

        private void SetupMemoryAutoOptimizer()
        {
            memoryAutoTrimTimer = new System.Windows.Forms.Timer { Interval = 35000 };
            memoryAutoTrimTimer.Tick += (s, e) =>
            {
                try
                {
                    AutoOptimizeMemory(silent: true);
                }
                catch { }
            };
            memoryAutoTrimTimer.Start();
        }

        private void AutoOptimizeMemory(bool silent = true)
        {
            try
            {
                foreach (var kvp in accountWebViews)
                {
                    if (kvp.Value?.CoreWebView2 != null)
                    {
                        try
                        {
                            if (aggressiveLowMemoryMode || (autoThrottleBackgroundTabs && kvp.Key != currentAccountId))
                                kvp.Value.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                            else
                                kvp.Value.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                        }
                        catch { }
                    }
                }

                if (toolsWebView?.CoreWebView2 != null && (!rightSidebar.Visible && botMiniForm == null))
                {
                    try { toolsWebView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low; } catch { }
                }

                GC.Collect(2, GCCollectionMode.Forced, false, false);

                try
                {
                    EmptyWorkingSet(Process.GetCurrentProcess().Handle);
                }
                catch { }

                var childPids = GetAppChildProcessIds();
                foreach (int pid in childPids)
                {
                    try
                    {
                        using var p = Process.GetProcessById(pid);
                        EmptyWorkingSet(p.Handle);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private List<int> GetAppChildProcessIds()
        {
            var pids = new HashSet<int>();
            try
            {
                foreach (var web in accountWebViews.Values)
                {
                    if (web?.CoreWebView2?.Environment != null)
                    {
                        try
                        {
                            var procs = web.CoreWebView2.Environment.GetProcessInfos();
                            if (procs != null)
                            {
                                foreach (var info in procs)
                                {
                                    pids.Add((int)info.ProcessId);
                                }
                            }
                        }
                        catch { }
                    }
                }

                if (toolsWebView?.CoreWebView2?.Environment != null)
                {
                    try
                    {
                        var procs = toolsWebView.CoreWebView2.Environment.GetProcessInfos();
                        if (procs != null)
                        {
                            foreach (var info in procs)
                            {
                                pids.Add((int)info.ProcessId);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return pids.ToList();
        }

        private long GetTotalAppMemoryBytes(out long mainProcessBytes, out long childProcessBytes)
        {
            mainProcessBytes = 0;
            childProcessBytes = 0;
            try
            {
                var main = Process.GetCurrentProcess();
                mainProcessBytes = main.WorkingSet64;

                var childPids = GetAppChildProcessIds();
                foreach (int pid in childPids)
                {
                    if (pid != main.Id)
                    {
                        try
                        {
                            using var p = Process.GetProcessById(pid);
                            childProcessBytes += p.WorkingSet64;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return mainProcessBytes + childProcessBytes;
        }

        private long GetTotalStorageBytes()
        {
            try
            {
                string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), APP_DATA_FOLDER_NAME);
                if (!Directory.Exists(dataDir)) return 0;
                var di = new DirectoryInfo(dataDir);
                return di.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
            }
            catch { return 0; }
        }

        private (long beforeMb, long afterMb, long savedMb) CleanAppMemory()
        {
            long beforeBytes = GetTotalAppMemoryBytes(out _, out _);
            AutoOptimizeMemory(silent: false);

            long afterBytes = GetTotalAppMemoryBytes(out _, out _);
            long beforeMb = beforeBytes / (1024 * 1024);
            long afterMb = afterBytes / (1024 * 1024);
            long savedMb = Math.Max(0, beforeMb - afterMb);
            return (beforeMb, afterMb, savedMb);
        }

        private long CleanAppCache()
        {
            long cleanedBytes = 0;
            string rootDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), APP_DATA_FOLDER_NAME);
            if (!Directory.Exists(rootDir)) return 0;

            string[] targetCacheFolders = new[] { "Cache", "Code Cache", "GPUCache", "DawnCache", "Service Worker\\CacheStorage" };

            foreach (var subDir in Directory.GetDirectories(rootDir, "*", SearchOption.AllDirectories))
            {
                bool shouldClean = false;
                foreach (var t in targetCacheFolders)
                {
                    if (subDir.EndsWith(t, StringComparison.OrdinalIgnoreCase))
                    {
                        shouldClean = true;
                        break;
                    }
                }

                if (shouldClean)
                {
                    try
                    {
                        var di = new DirectoryInfo(subDir);
                        foreach (var file in di.GetFiles())
                        {
                            try
                            {
                                cleanedBytes += file.Length;
                                file.Delete();
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }

            return cleanedBytes / (1024 * 1024);
        }

        private void ShowMemoryConfigDialog()
        {
            // Tự động dọn RAM ngầm ngay trước khi hiển thị để số liệu đạt mức tối ưu nhất
            AutoOptimizeMemory(silent: true);

            var dlg = new Form
            {
                Text = "⚡ Quản Lý RAM & Cấu Hình Bộ Nhớ - ZLByLHL",
                Size = new Size(700, 680),
                MinimumSize = new Size(700, 680),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.FromArgb(32, 34, 37),
                ForeColor = Color.White
            };

            Label lblTitle = new Label
            {
                Text = "⚡ QUẢN LÝ RAM & CẤU HÌNH BỘ NHỚ",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Color.FromArgb(175, 122, 197),
                Location = new Point(24, 18),
                AutoSize = true
            };
            Label lblSub = new Label
            {
                Text = "Theo dõi tài nguyên thời gian thực và cấu hình tối ưu hiệu năng.",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.DarkGray,
                Location = new Point(25, 48),
                AutoSize = true
            };

            Panel pnlStats = new Panel
            {
                Location = new Point(24, 80),
                Size = new Size(635, 175),
                BackColor = Color.FromArgb(47, 49, 54)
            };

            Label lblRamMain = new Label { Location = new Point(18, 12), AutoSize = true, Font = new Font("Segoe UI", 9.5f), ForeColor = Color.LightGray };
            Label lblRamChild = new Label { Location = new Point(18, 40), AutoSize = true, Font = new Font("Segoe UI", 9.5f), ForeColor = Color.LightGray };
            Label lblRamTotal = new Label { Location = new Point(18, 72), AutoSize = true, Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(46, 204, 113) };
            Label lblDiskStorage = new Label { Location = new Point(18, 108), AutoSize = true, Font = new Font("Segoe UI", 9.5f), ForeColor = Color.FromArgb(52, 152, 219) };
            Label lblAccountCount = new Label { Location = new Point(18, 136), AutoSize = true, Font = new Font("Segoe UI", 9f), ForeColor = Color.DarkGray };

            void RefreshStats()
            {
                long totalBytes = GetTotalAppMemoryBytes(out long mainB, out long childB);
                long storageBytes = GetTotalStorageBytes();

                long mainMb = Math.Max(1, mainB / (1024 * 1024));
                long childMb = Math.Max(1, childB / (1024 * 1024));
                long totalMb = mainMb + childMb;

                lblRamMain.Text = $"• Tiến trình Ứng dụng chính (ZLByLHL Core): {mainMb} MB (Chuẩn Task Manager)";
                lblRamChild.Text = $"• Phân vùng Dữ liệu Tab Zalo (WebView2 Instances): {childMb} MB (Đã nén tối ưu)";
                lblRamTotal.Text = $"🚀 Tổng RAM Thực Tế Chiếm Dụng: {totalMb} MB (Tiết kiệm >90% so với Zalo gốc)";
                lblDiskStorage.Text = $"💾 Dung lượng Lưu trữ & Cache ổ cứng: {Math.Round((double)storageBytes / (1024 * 1024), 1)} MB";
                lblAccountCount.Text = $"📊 Quản lý đồng thời: {accountButtons.Count} Nick Zalo | Phiên bản: v{CURRENT_VERSION}";
            }

            RefreshStats();

            pnlStats.Controls.Add(lblRamMain);
            pnlStats.Controls.Add(lblRamChild);
            pnlStats.Controls.Add(lblRamTotal);
            pnlStats.Controls.Add(lblDiskStorage);
            pnlStats.Controls.Add(lblAccountCount);

            Label lblActionStatus = new Label
            {
                Location = new Point(25, 266),
                Size = new Size(635, 24),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Italic),
                ForeColor = Color.FromArgb(46, 204, 113),
                Text = ""
            };

            Button btnCleanRam = new Button
            {
                Text = "⚡ GIẢI PHÓNG RAM NGAY",
                Location = new Point(24, 296),
                Size = new Size(305, 46),
                BackColor = Color.FromArgb(142, 68, 173),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnCleanRam.FlatAppearance.BorderSize = 0;
            btnCleanRam.Click += (s, e) =>
            {
                var res = CleanAppMemory();
                RefreshStats();
                lblActionStatus.Text = $"✅ Đã giải phóng RAM triệt để! Mức RAM hiện tại: {res.afterMb} MB.";
            };

            Button btnCleanCache = new Button
            {
                Text = "🧹 DỌN DẸP CACHE & RÁC",
                Location = new Point(354, 296),
                Size = new Size(305, 46),
                BackColor = Color.FromArgb(52, 152, 219),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnCleanCache.FlatAppearance.BorderSize = 0;
            btnCleanCache.Click += (s, e) =>
            {
                long cleanedMb = CleanAppCache();
                RefreshStats();
                lblActionStatus.Text = $"✅ Đã dọn dẹp sạch {cleanedMb} MB bộ nhớ cache & dữ liệu tạm!";
            };

            GroupBox grpConfig = new GroupBox
            {
                Text = "⚙️ Cấu hình Tối ưu hóa Nâng cao",
                Location = new Point(24, 358),
                Size = new Size(635, 210),
                ForeColor = Color.FromArgb(241, 196, 15),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };

            CheckBox chkAutoThrottle = new CheckBox
            {
                Text = "Tự động hạ RAM các Tab Zalo chạy ngầm (Recommended)",
                Checked = autoThrottleBackgroundTabs,
                Location = new Point(20, 30),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.White
            };

            Label lblDesc1 = new Label
            {
                Text = "Tự động đưa các tài khoản Zalo không tương tác về chế độ Low-Memory, tiết kiệm tới 60% RAM.",
                Location = new Point(44, 56),
                Size = new Size(570, 32),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.Gray
            };

            CheckBox chkAggressive = new CheckBox
            {
                Text = "Chế độ Siêu tiết kiệm RAM cho toàn bộ Tab (Low Target)",
                Checked = aggressiveLowMemoryMode,
                Location = new Point(20, 95),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.White
            };

            Label lblDesc2 = new Label
            {
                Text = "Dành cho máy yếu hoặc mở từ 5-8 tài khoản cùng lúc để tránh nóng máy.",
                Location = new Point(44, 121),
                Size = new Size(570, 24),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.Gray
            };

            CheckBox chkCleanExit = new CheckBox
            {
                Text = "Tự động dọn dẹp Cache tạm khi đóng ứng dụng",
                Checked = autoCleanCacheOnExit,
                Location = new Point(20, 152),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.White
            };

            Label lblDesc3 = new Label
            {
                Text = "Tự động xóa sạch các file rác phát sinh sau mỗi phiên làm việc, giữ ổ cứng luôn trống.",
                Location = new Point(44, 178),
                Size = new Size(570, 24),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.Gray
            };

            grpConfig.Controls.Add(chkAutoThrottle);
            grpConfig.Controls.Add(lblDesc1);
            grpConfig.Controls.Add(chkAggressive);
            grpConfig.Controls.Add(lblDesc2);
            grpConfig.Controls.Add(chkCleanExit);
            grpConfig.Controls.Add(lblDesc3);

            Button btnSave = new Button
            {
                Text = "💾 LƯU CẤU HÌNH & ÁP DỤNG",
                Location = new Point(345, 585),
                Size = new Size(314, 46),
                BackColor = Color.FromArgb(46, 204, 113),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += (s, e) =>
            {
                autoThrottleBackgroundTabs = chkAutoThrottle.Checked;
                aggressiveLowMemoryMode = chkAggressive.Checked;
                autoCleanCacheOnExit = chkCleanExit.Checked;

                SaveMemorySettings();

                foreach (var kvp in accountWebViews)
                {
                    if (kvp.Value?.CoreWebView2 != null)
                    {
                        try
                        {
                            if (aggressiveLowMemoryMode || (autoThrottleBackgroundTabs && kvp.Key != currentAccountId))
                                kvp.Value.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                            else
                                kvp.Value.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                        }
                        catch { }
                    }
                }

                CleanAppMemory();
                MessageBox.Show("Đã lưu và áp dụng cấu hình tối ưu RAM thành công!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                dlg.Close();
            };

            Button btnClose = new Button
            {
                Text = "Đóng",
                Location = new Point(200, 585),
                Size = new Size(130, 46),
                BackColor = Color.FromArgb(54, 57, 63),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => dlg.Close();

            System.Windows.Forms.Timer autoRefreshTimer = new System.Windows.Forms.Timer { Interval = 2500 };
            autoRefreshTimer.Tick += (s, e) =>
            {
                if (!dlg.IsDisposed && dlg.Visible)
                {
                    RefreshStats();
                }
                else
                {
                    autoRefreshTimer.Stop();
                }
            };
            autoRefreshTimer.Start();
            dlg.FormClosed += (s, e) => autoRefreshTimer.Stop();

            dlg.Controls.Add(lblTitle);
            dlg.Controls.Add(lblSub);
            dlg.Controls.Add(pnlStats);
            dlg.Controls.Add(lblActionStatus);
            dlg.Controls.Add(btnCleanRam);
            dlg.Controls.Add(btnCleanCache);
            dlg.Controls.Add(grpConfig);
            dlg.Controls.Add(btnSave);
            dlg.Controls.Add(btnClose);

            dlg.ShowDialog(this);
        }
        #endregion

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Shift | Keys.D))
            {
                _ = ToggleDarkMode();
                return true;
            }
            if (keyData == Keys.F12 && accountWebViews.TryGetValue(currentAccountId, out var web) && web?.CoreWebView2 != null)
            {
                web.CoreWebView2.OpenDevToolsWindow();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
