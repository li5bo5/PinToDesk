; PinToDesk Inno Setup Installer Script

[Setup]
AppName=PinToDesk
AppVersion=2.0
DefaultDirName={pf}\PinToDesk
DefaultGroupName=PinToDesk
OutputBaseFilename=PinToDesk_Installer
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "..\Publish\SelfContained_V2.0\PinToDesk.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\PinToDesk"; Filename: "{app}\PinToDesk.exe"
Name: "{commondesktop}\PinToDesk"; Filename: "{app}\PinToDesk.exe"; Tasks: desktopicon

[Run]
; 安装完成后自动启动（可选）
Filename: "{app}\PinToDesk.exe"; Flags: nowait postinstall skipifsilent

[Tasks]
Name: "desktopicon"; Description: "在桌面创建快捷方式"; GroupDescription: "选项";
