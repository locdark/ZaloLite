# Tự động Build và Đóng gói Bộ cài đặt ZLByLHL
param(
    [switch]$UploadVPS = $false
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " 📦 BẮT ĐẦU ĐÓNG GÓI BỘ CÀI ĐẶT ZLByLHL   " -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Cyan

# 1. Tắt tiến trình cũ nếu đang chạy
Get-Process -Name ZLByLHL -ErrorAction SilentlyContinue | Stop-Process -Force

# 2. Build Release .NET 9 Single-File
Write-Host "`n[1/3] Đang biên dịch mã nguồn C# (.NET 9)..." -ForegroundColor Green
$publishDir = "C:\Users\MY PC\Downloads\ZLByLHL"
dotnet publish "$scriptDir\ZaloLite.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $publishDir

# 3. Biên dịch Bộ cài đặt Inno Setup
Write-Host "`n[2/3] Đang nén và đóng gói thành file Setup..." -ForegroundColor Green
$isccPath = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $isccPath)) {
    $isccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
}
& "$isccPath" "$scriptDir\ZLByLHL.iss"

$distFile = "C:\Users\MY PC\Downloads\ZLByLHL_Dist\ZLByLHL_Setup_v2.0.1.exe"
if (Test-Path $distFile) {
    $sizeMb = [math]::Round((Get-Item $distFile).Length / 1MB, 2)
    Write-Host "`n[3/3] HOÀN TẤT! File cài đặt:" -ForegroundColor Cyan
    Write-Host "      👉 $distFile ($sizeMb MB)" -ForegroundColor Yellow
}

if ($UploadVPS) {
    Write-Host "`nĐang tải bộ cài lên VPS 153..." -ForegroundColor Green
    $env:SSH_ASKPASS = "c:\Users\MY PC\Downloads\firewallnew-main\askpass_153.bat"
    $env:SSH_ASKPASS_REQUIRE = "force"
    $env:DISPLAY = "dummy:0"
    scp.exe -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o LogLevel=ERROR $distFile root@160.191.244.153:/root/zalobot/public/downloads/ZLByLHL_Setup.exe
    Write-Host "✅ Đã tải lên VPS: http://160.191.244.153/downloads/ZLByLHL_Setup.exe" -ForegroundColor Green
}

Write-Host "`n==========================================" -ForegroundColor Cyan
