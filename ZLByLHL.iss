; Inno Setup Script for ZLByLHL Commercial Edition
#define MyAppName "ZLByLHL"
#define MyAppVersion "2.0.2"
#define MyAppPublisher "Locdark (Hoài Lộc)"
#define MyAppURL "https://dairong2.online"
#define MyAppExeName "ZLByLHL.exe"

[Setup]
AppId={{D8B3A2FB-961B-4C47-AAD7-85599CE1430D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/api/client/check-update
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=C:\Users\MY PC\Downloads\ZLByLHL_Dist
OutputBaseFilename=ZLByLHL_Setup_v{#MyAppVersion}
SetupIconFile=C:\Users\MY PC\Downloads\ZaloLite\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Tạo lối tắt trên Màn hình chính (Desktop Shortcut)"; GroupDescription: "Tùy chọn bổ sung:"; Flags: unchecked
Name: "startupicon"; Description: "Tự động khởi động cùng Windows (Chạy ngầm khay hệ thống)"; GroupDescription: "Tùy chọn bổ sung:"; Flags: unchecked

[Files]
Source: "C:\Users\MY PC\Downloads\ZLByLHL\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\Users\MY PC\Downloads\ZLByLHL\WebView2Loader.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\Users\MY PC\Downloads\ZLByLHL\app.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\Users\MY PC\Downloads\ZLByLHL\runtimes\*"; DestDir: "{app}\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: DirExists('C:\Users\MY PC\Downloads\ZLByLHL\runtimes')

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"
Name: "{group}\Gỡ cài đặt {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--startup"; IconFilename: "{app}\app.ico"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Khởi chạy {#MyAppName} ngay bây giờ"; Flags: nowait postinstall skipifsilent
