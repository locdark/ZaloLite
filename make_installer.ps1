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
taskkill.exe /F /IM ZLByLHL.exe /T 2>$null
Start-Sleep -Seconds 1

# 2. Build Release .NET 9 Single-File
Write-Host "`n[1/4] Đang biên dịch mã nguồn C# (.NET 9)..." -ForegroundColor Green
$publishDir = "C:\Users\MY PC\Downloads\ZLByLHL"
dotnet publish "$scriptDir\ZaloLite.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $publishDir

# Ký số cho file thực thi chính
Write-Host "`n[2/4] Đang gắn chữ ký số (Digital Signature)..." -ForegroundColor Green
$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
$pfx = "$scriptDir\ZLByLHL_CodeSign.pfx"
$pwd = "ZLByLHL2026@"
if ((Test-Path $signtool) -and (Test-Path $pfx)) {
    & $signtool sign /f $pfx /p $pwd /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 "$publishDir\ZLByLHL.exe" 2>$null
    if ($LASTEXITCODE -ne 0) {
        & $signtool sign /f $pfx /p $pwd /fd SHA256 "$publishDir\ZLByLHL.exe"
    }
}

# 3. Biên dịch Bộ cài đặt Inno Setup
Write-Host "`n[3/4] Đang nén và đóng gói thành file Setup..." -ForegroundColor Green
$isccPath = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $isccPath)) {
    $isccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
}
& "$isccPath" "$scriptDir\ZLByLHL.iss"

$distFile = (Get-ChildItem "C:\Users\MY PC\Downloads\ZLByLHL_Dist\ZLByLHL_Setup_*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
if ($distFile -and (Test-Path $distFile)) {
    if ((Test-Path $signtool) -and (Test-Path $pfx)) {
        & $signtool sign /f $pfx /p $pwd /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $distFile 2>$null
        if ($LASTEXITCODE -ne 0) {
            & $signtool sign /f $pfx /p $pwd /fd SHA256 $distFile
        }
    }
    $sizeMb = [math]::Round((Get-Item $distFile).Length / 1MB, 2)
    Write-Host "`n[4/4] HOÀN TẤT! File cài đặt:" -ForegroundColor Cyan
    Write-Host "      👉 $distFile ($sizeMb MB)" -ForegroundColor Yellow
}

if ($UploadVPS) {
    Write-Host "`nĐang tải bộ cài và version.json lên VPS 153..." -ForegroundColor Green
    $env:SSH_ASKPASS = "c:\Users\MY PC\Downloads\firewallnew-main\askpass_153.bat"
    $env:SSH_ASKPASS_REQUIRE = "force"
    $env:DISPLAY = "dummy:0"
    scp.exe -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o LogLevel=ERROR $distFile root@160.191.244.153:/root/zalobot/public/downloads/ZLByLHL_Setup.exe
    scp.exe -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o LogLevel=ERROR "$publishDir\ZLByLHL.exe" root@160.191.244.153:/root/zalobot/public/downloads/ZLByLHL.exe
    scp.exe -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o LogLevel=ERROR "$scriptDir\version.json" root@160.191.244.153:/root/zalobot/public/downloads/version.json
    Write-Host "✅ Đã tải lên VPS: https://dairong2.online/downloads/ZLByLHL_Setup.exe" -ForegroundColor Green
}

Write-Host "`n==========================================" -ForegroundColor Cyan

