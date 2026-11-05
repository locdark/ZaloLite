# 🚀 ZLByLHL - Commercial Edition (v2.0.0)

> **ZLByLHL** là phiên bản Zalo Desktop thương mại siêu nhẹ (Ultra Lightweight), chạy trên nền tảng **.NET 9 + Microsoft Edge WebView2**, tích hợp Cụm 4 Key Gemini AI Gateway, Hệ thống Quản trị Bot cho thuê, Chống thu hồi tin nhắn và Bản quyền License Key.

Được phát triển bởi **Locdark (Hoài Lộc)**.

---

## ✨ Tính Năng Nổi Bật

1. ⚡ **Siêu Nhẹ & Tối Ưu RAM:**
   - Bộ cài file chạy duy nhất `ZLByLHL.exe` chỉ **~1.1 MB**.
   - Mức tiêu thụ RAM chỉ **~150MB - 250MB** (so với 1.8GB - 2GB của Zalo PC chính thức).
   - Tự động chạy ngầm dưới khay hệ thống (System Tray), khởi động tức thì.

2. 🚀 **Tự Bật Khi Khởi Động Máy (Auto-Start with Windows):**
   - Tùy chọn bật/tắt khởi động cùng Windows ngay trên thanh menu hoặc khay hệ thống.
   - Khi máy tính bật, ZLByLHL tự động chạy ngầm dưới khay đồng hồ, không làm phiền người dùng.

3. 👥 **Quản Lý Đa Tài Khoản (Multi-Account Profile):**
   - Chạy cùng lúc tới **8 tài khoản Zalo độc lập**.
   - Mỗi tài khoản lưu dữ liệu, cookie riêng biệt, chuyển đổi tab trong 0 giây, không bao giờ bị đá phiên đăng nhập.

4. 🤖 **AI Co-Pilot CSKH & Chốt Đơn (Cụm 4 Key Gemini Load-Balanced):**
   - Kết nối trực tiếp Cụm AI Gateway 4 key Google Gemini (`gemini-3.5-flash`) trên VPS 120.
   - 1-Click đọc tin nhắn của khách hàng và tự động tạo câu trả lời chốt sales, tư vấn lịch sự, chuẩn kịch bản.
   - 1-Click điền thẳng nội dung vào ô chat Zalo.

5. 🛡️ **Chống Thu Hồi Tin Nhắn (Anti-Recall):**
   - Tự động bắt sự kiện DOM: Khi đối phương bấm "Thu hồi tin nhắn", phần mềm vẫn lưu vết và hiển thị nội dung gốc kèm thông báo nổi bật.

6. 🛠️ **Tích Hợp Sẵn ZaloTools Bot Suite (VPS 153):**
   - Nhúng trực tiếp Dashboard quản lý bot cho thuê, quét thành viên nhóm, gửi tin hàng loạt.

7. 💎 **Hệ Thống Bản Quyền License Key (Khóa Phần Cứng HWID):**
   - Xác thực bản quyền qua máy chủ VPS 153 (`/api/client/verify-license`).
   - Khóa vào mã phần cứng máy tính (HWID), ngăn chặn share lậu bản quyền.

8. 🔄 **Cập Nhật Phần Mềm Từ Xa (Remote OTA Auto-Updater):**
   - Tự động kiểm tra bản cập nhật mới từ server VPS 153 khi mở app (`/api/client/check-update`).
   - Tự động tải bản vá lỗi và cập nhật 1-Click.

---

## 🛠️ Hướng Dẫn Build

Yêu cầu: [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

```bash
# Clone source code
git clone https://github.com/locdark/ZaloLite.git
cd ZaloLite

# Build bản Release Single File (~1.1MB)
dotnet publish ZaloLite.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish
```

---

## 📜 Bản Quyền & Giấy Phép
Phát triển bởi **Locdark**. Mọi quyền được bảo lưu.
