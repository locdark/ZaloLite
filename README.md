# Zalo Pro - Siêu Nhẹ (WebView2 Edition)

> Ứng dụng Desktop Zalo siêu nhẹ, chạy trên nền Microsoft Edge WebView2, giải quyết triệt để vấn đề ngốn RAM và phình ổ đĩa của Zalo PC chính thức.

[![.NET 9.0](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/)
[![WebView2](https://img.shields.io/badge/Engine-WebView2-blue.svg)](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6.svg)](https://microsoft.com)
[![RAM](https://img.shields.io/badge/RAM-~170MB-brightgreen.svg)]()

---

## ✨ Tính Năng Nổi Bật

- **Siêu nhẹ:** Tiêu thụ chỉ khoảng **~170 MB RAM** (so với 1.8 GB - 2 GB của Zalo PC Electron gốc - nhẹ hơn gấp 12 lần).
- **Giao diện chuẩn 100%:** Chạy trên lõi Chromium chính thức của Zalo Web, không bao giờ bị lỗi font tiếng Việt, không vỡ layout.
- **Xem ảnh & Video real-time:** Tải ảnh, video, sticker mượt mà tức thì.
- **Không lo phình ổ đĩa:** Không tự động ép tải ngầm hàng chục GB file gốc vào ổ C; cơ chế Web cache tự động giới hạn trần thông minh.
- **Lưu phiên vĩnh viễn:** Toàn bộ tin nhắn và trạng thái đăng nhập được lưu độc lập tại `%LOCALAPPDATA%\ZaloProData`, tách biệt hoàn toàn với trình duyệt.
- **Khay hệ thống (System Tray):** Tắt cửa sổ sẽ tự động thu nhỏ xuống khay đồng hồ, giữ kết nối ngầm nhận thông báo tin nhắn.
- **Hỗ trợ Dark Mode:** Phím tắt `Ctrl + Shift + D` để chuyển đổi giao diện Tối/Sáng bảo vệ mắt.
- **Tự động cấp quyền cuộc gọi:** Camera và Micro được tự động cho phép khi có cuộc gọi thoại/video Zalo.

---

## 🚀 Phím Tắt Tiện Ích

- `Ctrl + Shift + D`: Bật / Tắt chế độ **Dark Mode**.
- `F12`: Mở bảng **DevTools** kiểm tra log mạng và gói tin.

---

## 🛠️ Hướng Dẫn Biên Dịch & Đóng Gói

Yêu cầu máy tính đã cài [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```powershell
# 1. Clone repository
git clone https://github.com/locdark/ZaloLite.git
cd ZaloLite

# 2. Chạy thử bản Debug
dotnet run

# 3. Đóng gói ra bản Release Siêu Nhẹ (~1MB)
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish
```

---

## 📜 Giấy Phép & Tuyên Bố
Dự án được phát triển với mục đích tối ưu hóa hiệu năng cá nhân dựa trên Microsoft Edge WebView2 công khai. Mọi thương hiệu và bản quyền giao diện thuộc về VNG Corporation / Zalo.
