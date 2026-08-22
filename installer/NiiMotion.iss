#define MyAppName "NiiMotion"
#define MyAppVersion "0.5.0"
#define MyAppPublisher "NiiMotion Project"
#define MyAppExeName "NiiRMotion.App.exe"

[Setup]
AppId={{F15D7B38-6A2F-47AF-A675-4694295263A4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\NiiMotion
DefaultGroupName=NiiMotion
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=NiiMotion-Setup-{#MyAppVersion}-x64
SetupIconFile=..\src\NiiRMotion.App\Assets\niirmotion.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dynamic
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductName={#MyAppName}
VersionInfoCompany={#MyAppPublisher}
LicenseFile=LICENSE.txt

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstü kısayolu oluştur"; GroupDescription: "Kısayollar:"; Flags: checkedonce

[Files]
Source: "..\artifacts\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\docs\first-run-guide-tr.md"; DestDir: "{app}\Docs"; Flags: ignoreversion
Source: "..\docs\device-setup-tr.md"; DestDir: "{app}\Docs"; Flags: ignoreversion
Source: "..\docs\troubleshooting-tr.md"; DestDir: "{app}\Docs"; Flags: ignoreversion
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}\Docs"; Flags: ignoreversion

[Icons]
Name: "{group}\NiiMotion"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\NiiMotion"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "NiiMotion'ı başlat"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SteamPath, VrPathReg, DriverPath: String;
  ResultCode: Integer;
begin
  if CurUninstallStep <> usUninstall then Exit;
  DriverPath := ExpandConstant('{app}\OpenVRDriver');
  if RegQueryStringValue(HKCU, 'Software\Valve\Steam', 'SteamPath', SteamPath) then
  begin
    VrPathReg := SteamPath + '\steamapps\common\SteamVR\bin\win64\vrpathreg.exe';
    if FileExists(VrPathReg) then
      Exec(VrPathReg, 'removedriver "' + DriverPath + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
