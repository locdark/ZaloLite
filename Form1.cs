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
        public const string CURRENT_VERSION = "2.0.0";
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

        // Panel AI & Panel Tools
        private Panel aiPanel = null!;
        private Panel toolsPanel = null!;
        private WebView2? toolsWebView;

        // AI Controls
        private ComboBox cboAiPreset = null!;
        private TextBox txtCustomerMsg = null!;
        private TextBox txtAiInstruction = null!;
        private TextBox txtAiOutput = null!;
        private Button btnAiGenerate = null!;
        private Label lblAiStatus = null!;

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

        // Endpoints Backend
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        private const string VPS_153_BASE = "http://160.191.244.153";
        private const string AI_GATEWAY_URL = "http://180.93.114.120:3000/v1/chat/completions";
        private const string AI_MASTER_KEY = "sk-I8LFCh2Yenz1IAkbacmKfwKsVYEiwuB60GMCLOY3sYDNuuQ5";
        private const string AI_MODEL = "gemini-3.5-flash";

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

            // Tải icon cho Form
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
                Size = new Size(520, 310),
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
                Location = new Point(20, 20),
                AutoSize = true
            };
            dlg.Controls.Add(lblTitle);

            var lblDesc = new Label
            {
                Text = isVipActivated
                    ? $"Trạng thái: ĐÃ KÍCH HOẠT ({activePlan.ToUpper()})\nHạn dùng: {licenseExpiry:dd/MM/yyyy HH:mm}\nMã: {licenseKey}"
                    : "Nhập mã License Key để mở khóa 8 Nick Zalo, Trợ lý AI CSKH và Bot ZaloTools:",
                Location = new Point(22, 55),
                Size = new Size(460, 45),
                ForeColor = isVipActivated ? Color.FromArgb(46, 204, 113) : Color.FromArgb(80, 80, 80)
            };
            dlg.Controls.Add(lblDesc);

            var txtKeyInput = new TextBox
            {
                Location = new Point(22, 110),
                Size = new Size(460, 30),
                Font = new Font("Consolas", 11f, FontStyle.Bold),
                PlaceholderText = "VD: LOCDARK-VIP999-TEST01"
            };
            if (!string.IsNullOrEmpty(licenseKey)) txtKeyInput.Text = licenseKey;
            dlg.Controls.Add(txtKeyInput);

            var lblStatus = new Label
            {
                Text = "",
                Location = new Point(22, 150),
                Size = new Size(460, 25),
                ForeColor = Color.Red
            };
            dlg.Controls.Add(lblStatus);

            var btnSubmit = new Button
            {
                Text = "⚡ KÍCH HOẠT NGAY",
                Location = new Point(22, 185),
                Size = new Size(220, 42),
                BackColor = Color.FromArgb(0, 132, 255),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnSubmit.Click += async (s, e) => {
                string inputKey = txtKeyInput.Text.Trim();
                if (string.IsNullOrEmpty(inputKey))
                {
                    lblStatus.Text = "Vui lòng nhập License Key!";
                    return;
                }

                btnSubmit.Enabled = false;
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
                    btnSubmit.Enabled = true;
                }
            };
            dlg.Controls.Add(btnSubmit);

            var btnWeb = new Button
            {
                Text = "🛒 Mua Key / Gia Hạn",
                Location = new Point(260, 185),
                Size = new Size(220, 42),
                BackColor = Color.FromArgb(240, 243, 246),
                ForeColor = Color.FromArgb(40, 40, 40),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnWeb.Click += (s, e) => {
                Process.Start(new ProcessStartInfo { FileName = VPS_153_BASE, UseShellExecute = true });
            };
            dlg.Controls.Add(btnWeb);

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
                Height = 410,
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
                Height = 310,
                BackColor = Color.Transparent
            };
            sidebarPanel.Controls.Add(bottomToolsPanel);

            btnVipStatus = CreateSidebarIconButton(isVipActivated ? "💎" : "👑", isVipActivated ? $"Gói {activePlan.ToUpper()} (HSD: {licenseExpiry:dd/MM/yyyy})" : "Kích hoạt bản quyền VIP", isVipActivated ? Color.FromArgb(46, 204, 113) : Color.FromArgb(241, 196, 15), Color.White);
            btnVipStatus.Location = new Point(10, 8);
            btnVipStatus.Click += (s, e) => ShowLicenseDialog();

            Button btnAiToggle = CreateSidebarIconButton("🤖", "Trợ lý AI CSKH (VPS 120 Gemini Cluster)", Color.FromArgb(0, 132, 255), Color.White);
            btnAiToggle.Location = new Point(10, 68);
            btnAiToggle.Click += (s, e) => ToggleRightPanel("AI");

            Button btnToolsToggle = CreateSidebarIconButton("🛠️", "ZaloTools Bot Suite (VPS 153)", Color.FromArgb(230, 126, 34), Color.White);
            btnToolsToggle.Location = new Point(10, 128);
            btnToolsToggle.Click += async (s, e) => {
                ToggleRightPanel("TOOLS");
                await InitToolsWebViewAsync();
            };

            Button btnStartupToggle = CreateSidebarIconButton("🚀", "Cài đặt: Tự bật khi khởi động máy", IsAutoStartEnabled() ? Color.FromArgb(46, 204, 113) : Color.FromArgb(54, 57, 63), Color.White);
            btnStartupToggle.Location = new Point(10, 188);
            btnStartupToggle.Click += (s, e) => {
                ToggleAutoStart();
                btnStartupToggle.BackColor = IsAutoStartEnabled() ? Color.FromArgb(46, 204, 113) : Color.FromArgb(54, 57, 63);
            };

            Button btnDarkToggle = CreateSidebarIconButton("🌙", "Bật/Tắt Giao diện tối", Color.FromArgb(54, 57, 63), Color.White);
            btnDarkToggle.Location = new Point(10, 248);
            btnDarkToggle.Click += async (s, e) => await ToggleDarkMode();

            bottomToolsPanel.Controls.Add(btnVipStatus);
            bottomToolsPanel.Controls.Add(btnAiToggle);
            bottomToolsPanel.Controls.Add(btnToolsToggle);
            bottomToolsPanel.Controls.Add(btnStartupToggle);
            bottomToolsPanel.Controls.Add(btnDarkToggle);

            AddAccountButton(1);

            rightSidebar = new Panel
            {
                Dock = DockStyle.Right,
                Width = 360,
                BackColor = Color.FromArgb(248, 249, 250),
                Visible = false
            };
            this.Controls.Add(rightSidebar);

            BuildAiPanel();
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

                targetWeb.CoreWebView2.DOMContentLoaded += async (s, args) =>
                {
                    await InjectAntiRecallScriptAsync(targetWeb);
                };

                targetWeb.Source = new Uri("https://chat.zalo.me");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khởi tạo tài khoản #{accId}: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

        #region Panel AI Co-Pilot (VPS 120 AI Gateway)
        private void BuildAiPanel()
        {
            aiPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(245, 247, 250),
                Padding = new Padding(12),
                AutoScroll = true
            };
            rightSidebar.Controls.Add(aiPanel);

            // Container chứa các control theo thứ tự tuần tự
            var contentLayout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.Transparent,
                Padding = new Padding(0)
            };
            aiPanel.Controls.Add(contentLayout);

            var lblTitle = new Label
            {
                Text = "🤖 AI CSKH & CHỐT ĐƠN (ZLByLHL)",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 204),
                Width = 330,
                Height = 28
            };
            contentLayout.Controls.Add(lblTitle);

            var lblPreset = new Label { Text = "Kịch bản bán hàng:", Width = 330, Height = 20, ForeColor = Color.FromArgb(60, 60, 60) };
            contentLayout.Controls.Add(lblPreset);

            cboAiPreset = new ComboBox
            {
                Width = 330,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Height = 28
            };
            cboAiPreset.Items.AddRange(new object[] {
                "1. Tư vấn báo giá & chốt đơn khéo léo",
                "2. Xử lý khiếu nại khách hàng lịch sự",
                "3. Xin lỗi chậm trễ & tặng voucher",
                "4. Giới thiệu chương trình khuyến mãi hôm nay",
                "5. Dịch tin nhắn sang tiếng Anh chuyên nghiệp"
            });
            cboAiPreset.SelectedIndex = 0;
            contentLayout.Controls.Add(cboAiPreset);

            var btnFetchMsg = new Button
            {
                Text = "📥 Lấy tin nhắn khách đang chat",
                Width = 330,
                Height = 36,
                BackColor = Color.FromArgb(235, 240, 250),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 8, 0, 8)
            };
            btnFetchMsg.Click += async (s, e) => await ExtractLastCustomerMessageAsync();
            contentLayout.Controls.Add(btnFetchMsg);

            var lblCust = new Label { Text = "Tin nhắn của khách:", Width = 330, Height = 20, ForeColor = Color.FromArgb(60, 60, 60) };
            contentLayout.Controls.Add(lblCust);

            txtCustomerMsg = new TextBox
            {
                Width = 330,
                Multiline = true,
                Height = 65,
                ScrollBars = ScrollBars.Vertical
            };
            contentLayout.Controls.Add(txtCustomerMsg);

            var lblInst = new Label { Text = "Ghi chú thêm (Giá, hàng, yêu cầu):", Width = 330, Height = 20, ForeColor = Color.FromArgb(60, 60, 60) };
            contentLayout.Controls.Add(lblInst);

            txtAiInstruction = new TextBox
            {
                Width = 330,
                Height = 26,
                PlaceholderText = "VD: Đang giảm 20%, freeship đơn từ 500k..."
            };
            contentLayout.Controls.Add(txtAiInstruction);

            btnAiGenerate = new Button
            {
                Text = "✨ AI SOẠN CÂU TRẢ LỜI",
                Width = 330,
                Height = 38,
                BackColor = Color.FromArgb(0, 132, 255),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 8, 0, 4)
            };
            btnAiGenerate.Click += async (s, e) => await CallAiGatewayGenerateReplyAsync();
            contentLayout.Controls.Add(btnAiGenerate);

            lblAiStatus = new Label
            {
                Text = "Sẵn sàng kết nối VPS 120 (4x Gemini Load-Balanced)",
                Width = 330,
                Height = 22,
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8f)
            };
            contentLayout.Controls.Add(lblAiStatus);

            var lblOut = new Label { Text = "Câu trả lời gợi ý của AI:", Width = 330, Height = 20, ForeColor = Color.FromArgb(60, 60, 60) };
            contentLayout.Controls.Add(lblOut);

            txtAiOutput = new TextBox
            {
                Width = 330,
                Multiline = true,
                Height = 110,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.White
            };
            contentLayout.Controls.Add(txtAiOutput);

            var btnInsertToZalo = new Button
            {
                Text = "🚀 CHÈN VÀO KHUNG CHAT ZALO",
                Width = 330,
                Height = 42,
                BackColor = Color.FromArgb(46, 204, 113),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 8, 0, 15)
            };
            btnInsertToZalo.Click += async (s, e) => await InsertTextIntoZaloChatAsync(txtAiOutput.Text);
            contentLayout.Controls.Add(btnInsertToZalo);
        }

        private async Task ExtractLastCustomerMessageAsync()
        {
            if (!accountWebViews.TryGetValue(currentAccountId, out var web) || web?.CoreWebView2 == null) return;

            string js = @"
                (function() {
                    // 1. Text đang bôi đen
                    const sel = window.getSelection().toString().trim();
                    if (sel && sel.length > 0) return sel;

                    // 2. Tìm trong khu vực tin nhắn
                    const chatRoot = document.querySelector('#chatViewContainer, #messageViewContainer, .chat-view, [class*=""chat-thread""]') || document.body;

                    // Các selector tin nhắn của Zalo Web
                    const selectors = [
                        '.chat-message-text',
                        '.card--text .text',
                        '.card--text',
                        '[class*=""content-text""]',
                        '.msg-text',
                        '.bubble-wrapper .text',
                        '[id^=""msg-""] [class*=""text""]',
                        '.chat-item [class*=""text""]'
                    ];

                    const bubbles = Array.from(chatRoot.querySelectorAll(selectors.join(', ')));

                    for (let i = bubbles.length - 1; i >= 0; i--) {
                        const el = bubbles[i];
                        // Loại trừ tin do chính mình gửi (bên phải / có class me)
                        const isMe = el.closest('.me, .chat-item--me, [class*=""bubble-right""], [class*=""--me""], [class*=""right""]');
                        if (isMe) continue;

                        let txt = (el.innerText || el.textContent || '').trim();
                        if (txt && txt.length > 0 && !txt.match(/^\d{1,2}:\d{2}$/) && !txt.includes('Gửi yêu cầu kết bạn')) {
                            txt = txt.replace(/\n?\d{1,2}:\d{2}$/, '').trim();
                            if (txt.length > 0) return txt;
                        }
                    }

                    // 3. Quét hình học các phần tử bên trái
                    const paragraphs = Array.from(chatRoot.querySelectorAll('p, span, div'));
                    const rectRoot = chatRoot.getBoundingClientRect();
                    const midX = rectRoot.left + (rectRoot.width / 2);

                    for (let j = paragraphs.length - 1; j >= 0; j--) {
                        const p = paragraphs[j];
                        if (p.children.length > 0) continue;
                        const txt = (p.innerText || '').trim();
                        if (!txt || txt.length < 2 || txt.match(/^\d{1,2}:\d{2}$/)) continue;

                        const rect = p.getBoundingClientRect();
                        if (rect.top > rectRoot.top && rect.bottom < rectRoot.bottom && rect.right < (midX + 40) && rect.left > rectRoot.left + 40) {
                            if (!txt.includes('Tải ngay') && !txt.includes('Tìm kiếm') && !txt.includes('Gửi yêu cầu')) {
                                return txt;
                            }
                        }
                    }

                    return '';
                })();
            ";

            string resultJson = await web.ExecuteScriptAsync(js);
            string rawText = JsonSerializer.Deserialize<string>(resultJson) ?? "";
            if (!string.IsNullOrEmpty(rawText))
            {
                txtCustomerMsg.Text = rawText;
                lblAiStatus.Text = "✅ Đã trích xuất tin nhắn khách gần nhất!";
            }
            else
            {
                lblAiStatus.Text = "⚠️ Chưa tìm thấy tin nhắn, bạn có thể bôi đen văn bản.";
            }
        }

        private async Task CallAiGatewayGenerateReplyAsync()
        {
            string custText = txtCustomerMsg.Text.Trim();
            if (string.IsNullOrEmpty(custText))
            {
                MessageBox.Show("Vui lòng nhập tin nhắn của khách hàng!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnAiGenerate.Enabled = false;
            lblAiStatus.Text = "⏳ Đang gọi Cụm 4 Key Gemini (VPS 120)...";

            string promptType = cboAiPreset.SelectedItem?.ToString() ?? "Tư vấn báo giá";
            string extraNote = txtAiInstruction.Text.Trim();

            string systemPrompt = $@"Bạn là trợ lý bán hàng và CSKH chuyên nghiệp trên Zalo.
Nhiệm vụ: Soạn câu trả lời bằng tiếng Việt cực kỳ thân thiện, lễ phép, khéo léo và tự nhiên.
Kịch bản: {promptType}.
Ghi chú thêm của shop: {extraNote}.
Chỉ trả về nội dung tin nhắn để gửi trực tiếp cho khách, không kèm lời giải thích rườm rà.";

            try
            {
                var requestBody = new
                {
                    model = AI_MODEL,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = $"Khách hàng nhắn: \"{custText}\"" }
                    },
                    temperature = 0.7
                };

                string jsonPayload = JsonSerializer.Serialize(requestBody);
                using var request = new HttpRequestMessage(HttpMethod.Post, AI_GATEWAY_URL);
                request.Headers.Add("Authorization", $"Bearer {AI_MASTER_KEY}");
                request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var resp = await httpClient.SendAsync(request);
                string respJson = await resp.Content.ReadAsStringAsync();

                if (resp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(respJson);
                    string aiReply = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString() ?? "";

                    txtAiOutput.Text = aiReply.Trim();
                    lblAiStatus.Text = "⚡ Hoàn tất! Bấm 'Chèn vào khung chat Zalo' để gửi.";
                }
                else
                {
                    lblAiStatus.Text = $"❌ Lỗi Gateway: {resp.StatusCode}";
                    txtAiOutput.Text = respJson;
                }
            }
            catch (Exception ex)
            {
                lblAiStatus.Text = "❌ Lỗi kết nối AI Gateway!";
                txtAiOutput.Text = $"Lỗi: {ex.Message}";
            }
            finally
            {
                btnAiGenerate.Enabled = true;
            }
        }

        private async Task InsertTextIntoZaloChatAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!accountWebViews.TryGetValue(currentAccountId, out var web) || web?.CoreWebView2 == null) return;

            string escapedText = JsonSerializer.Serialize(text);
            string js = $@"
                (function() {{
                    const input = document.querySelector('div[contenteditable=""true""]') || 
                                  document.getElementById('input_rich_text') || 
                                  document.querySelector('[class*=""chat-input""]') ||
                                  document.querySelector('.rich-input');
                    if (input) {{
                        input.focus();
                        try {{
                            document.execCommand('selectAll', false, null);
                            document.execCommand('insertText', false, {escapedText});
                        }} catch(e) {{
                            input.innerText = {escapedText};
                        }}
                        input.dispatchEvent(new Event('input', {{ bubbles: true }}));
                        return true;
                    }}
                    return false;
                }})();
            ";

            await web.ExecuteScriptAsync(js);
            lblAiStatus.Text = "🚀 Đã chèn thành công vào ô chat Zalo!";
        }
        #endregion

        #region Panel ZaloTools Suite (VPS 153)
        private void BuildToolsPanel()
        {
            toolsPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Visible = false
            };
            rightSidebar.Controls.Add(toolsPanel);

            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.FromArgb(230, 126, 34) };
            var lblToolsTitle = new Label
            {
                Text = "🛠️ ZALOTOOLS SUITE (VPS 153)",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Dock = DockStyle.Left,
                Width = 260,
                Padding = new Padding(10, 8, 0, 0)
            };
            var btnReloadTools = new Button
            {
                Text = "🔄 Tải lại",
                Dock = DockStyle.Right,
                Width = 80,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(211, 84, 0),
                Cursor = Cursors.Hand
            };
            btnReloadTools.Click += (s, e) => toolsWebView?.CoreWebView2?.Reload();

            pnlTop.Controls.Add(lblToolsTitle);
            pnlTop.Controls.Add(btnReloadTools);
            toolsPanel.Controls.Add(pnlTop);
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

        private void ToggleRightPanel(string panelName)
        {
            if (panelName == "AI")
            {
                if (rightSidebar.Visible && aiPanel.Visible)
                {
                    rightSidebar.Visible = false;
                }
                else
                {
                    aiPanel.Visible = true;
                    toolsPanel.Visible = false;
                    rightSidebar.Visible = true;
                }
            }
            else if (panelName == "TOOLS")
            {
                if (rightSidebar.Visible && toolsPanel.Visible)
                {
                    rightSidebar.Visible = false;
                }
                else
                {
                    toolsPanel.Visible = true;
                    aiPanel.Visible = false;
                    rightSidebar.Visible = true;
                }
            }
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
            if (keyData == (Keys.Control | Keys.Space))
            {
                ToggleRightPanel("AI");
                return true;
            }
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
