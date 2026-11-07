using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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
        public const string CURRENT_VERSION = "2.0.2";
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

        public Form1(bool startMinimized = false)
        {
            this.startMinimized = startMinimized;
            InitializeComponent();
            SetupApp();
        }

        private void SetupApp()
        {
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
            BuildLayout();
            SetupTray();

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
                string tempExe = Path.Combine(Path.GetTempPath(), "ZLByLHL_New.exe");
                byte[] bytes = await httpClient.GetByteArrayAsync(downloadUrl);
                await File.WriteAllBytesAsync(tempExe, bytes);

                string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                string batchScript = Path.Combine(Path.GetTempPath(), "update_zlbylhl.bat");

                string batContent = $@"
@echo off
timeout /t 2 /nobreak > nul
move /y ""{tempExe}"" ""{currentExe}""
start """" ""{currentExe}""
del ""%~f0""
";
                await File.WriteAllTextAsync(batchScript, batContent);

                Process.Start(new ProcessStartInfo
                {
                    FileName = batchScript,
                    CreateNoWindow = true,
                    UseShellExecute = true
                });

                isClosingExplicitly = true;
                Application.Exit();
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
                Height = 255,
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

            bottomToolsPanel.Controls.Add(btnVipStatus);
            bottomToolsPanel.Controls.Add(btnToolsToggle);
            bottomToolsPanel.Controls.Add(btnStartupToggle);
            bottomToolsPanel.Controls.Add(btnDarkToggle);

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

                // Tự động nhận diện là ZaloPC để Zalo Web không hiển thị gợi ý tải app Zalo PC
                try
                {
                    string currentUa = targetWeb.CoreWebView2.Settings.UserAgent;
                    if (string.IsNullOrEmpty(currentUa))
                    {
                        currentUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                    }
                    if (!currentUa.Contains("ZaloPC"))
                    {
                        targetWeb.CoreWebView2.Settings.UserAgent = currentUa + " ZaloPC-win32-x64 ZaloPC";
                    }
                }
                catch { }

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
                    // 1. Chèn CSS dập tắt ngay lập tức các banner gợi ý cài đặt Zalo PC
                    if (!document.getElementById('zlbylhl-clean-banner')) {
                        const style = document.createElement('style');
                        style.id = 'zlbylhl-clean-banner';
                        style.textContent = `
                            [class*=""banner-download""],
                            [class*=""download-pc""],
                            [class*=""download-banner""],
                            [class*=""banner-sync""],
                            [class*=""banner_download""],
                            .banner-download,
                            .download-banner,
                            .sync-banner,
                            .top-banner,
                            .banner-top,
                            .banner-notify,
                            div[class*=""banner""]:has(a[href*=""zalo.me""]) {
                                display: none !important;
                                height: 0px !important;
                                min-height: 0px !important;
                                max-height: 0px !important;
                                padding: 0px !important;
                                margin: 0px !important;
                                visibility: hidden !important;
                                overflow: hidden !important;
                                opacity: 0 !important;
                                pointer-events: none !important;
                            }
                        `;
                        (document.head || document.documentElement).appendChild(style);
                    }

                    // 2. Quét liên tục và xóa container chứa dòng chữ 'Sử dụng Zalo PC...'
                    function purgeDownloadBanner() {
                        try {
                            // Cách 1: Quét thẻ liên kết tải Zalo PC
                            const dlLinks = document.querySelectorAll('a[href*=""zalo.me/download""], a[href*=""zalo.me/pc""], a[href*=""id.zalo.me""]');
                            dlLinks.forEach(a => {
                                let curr = a.parentElement;
                                while (curr && curr !== document.body && curr.clientHeight < 80) {
                                    if (curr.parentElement && curr.parentElement.clientHeight > 80) {
                                        curr.style.setProperty('display', 'none', 'important');
                                        curr.style.setProperty('height', '0px', 'important');
                                        curr.style.setProperty('visibility', 'hidden', 'important');
                                        curr.style.setProperty('overflow', 'hidden', 'important');
                                        break;
                                    }
                                    curr = curr.parentElement;
                                }
                            });

                            // Cách 2: Quét nội dung văn bản thông báo
                            const walker = document.createTreeWalker(document.body || document.documentElement, NodeFilter.SHOW_TEXT, null, false);
                            let node;
                            while (node = walker.nextNode()) {
                                const val = node.nodeValue;
                                if (val && (val.includes('Sử dụng Zalo PC') || val.includes('lưu trữ dài hạn') || val.includes('dữ liệu trò chuyện của bạn'))) {
                                    let curr = node.parentElement;
                                    while (curr && curr !== document.body && curr.clientHeight < 80) {
                                        if (curr.parentElement && curr.parentElement.clientHeight > 80) {
                                            curr.style.setProperty('display', 'none', 'important');
                                            curr.style.setProperty('height', '0px', 'important');
                                            curr.style.setProperty('visibility', 'hidden', 'important');
                                            curr.style.setProperty('overflow', 'hidden', 'important');
                                            break;
                                        }
                                        curr = curr.parentElement;
                                    }
                                }
                            }
                        } catch(e) {}
                    }

                    purgeDownloadBanner();
                    if (!window.__promoObserverActive) {
                        window.__promoObserverActive = true;
                        const obs = new MutationObserver(purgeDownloadBanner);
                        obs.observe(document.documentElement || document.body, { childList: true, subtree: true });
                        setInterval(purgeDownloadBanner, 1500);
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
                trayIcon?.Dispose();
            }
        }

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
