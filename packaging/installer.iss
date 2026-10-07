#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef Architecture
  #define Architecture "x64"
#endif
#ifndef PublishDir
  #error PublishDir must point to the dotnet publish output.
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
AppId={{A175C2F9-5189-44F6-A958-91B2AF03420A}
AppName=FlClash 更新器
AppVersion={#AppVersion}
AppPublisher=kowyo
AppPublisherURL=https://github.com/kowyo/flclash-updater
AppSupportURL=https://github.com/kowyo/flclash-updater/issues
AppUpdatesURL=https://github.com/kowyo/flclash-updater/releases
DefaultDirName={localappdata}\Programs\FlClashUpdater
DefaultGroupName=FlClash 更新器
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
#if Architecture == "ARM64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=FlClashUpdater-{#AppVersion}-{#Architecture}-setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\FlClashUpdater-WinUI3.exe
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\FlClashUpdater-WinUI3.exe"; DestDir: "{app}"; Flags: ignoreversion

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Icons]
Name: "{autoprograms}\FlClash 更新器"; Filename: "{app}\FlClashUpdater-WinUI3.exe"
Name: "{autodesktop}\FlClash 更新器"; Filename: "{app}\FlClashUpdater-WinUI3.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\FlClashUpdater-WinUI3.exe"; Description: "{cm:LaunchProgram,FlClash 更新器}"; Flags: nowait postinstall skipifsilent
