using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ZLByLHL;

static class Program
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Kiểm tra Single Instance thông qua Process
        var current = Process.GetCurrentProcess();
        var existing = Process.GetProcessesByName(current.ProcessName)
                              .FirstOrDefault(p => p.Id != current.Id);

        if (existing != null)
        {
            // Đã có ứng dụng đang chạy -> Gửi tín hiệu đánh thức cửa sổ cũ lên màn hình
            int msgId = RegisterWindowMessage("ZLByLHL_WAKEUP_WINDOW_MSG");
            if (msgId != 0)
            {
                PostMessage((IntPtr)0xffff, (uint)msgId, IntPtr.Zero, IntPtr.Zero); // HWND_BROADCAST
            }
            return;
        }

        bool startMinimized = false;
        foreach (var arg in args)
        {
            if (arg.Equals("--startup", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase))
            {
                startMinimized = true;
                break;
            }
        }

        Application.Run(new Form1(startMinimized));
    }
}
