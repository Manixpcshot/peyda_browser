; Peyda Browser - Inno Setup script
; Build (CI does this via Package-All.ps1):
;   ISCC.exe /DAppVersion=0.1.0-alpha /DArch=x64 /DPublishDir=..\artifacts\publish\win-x64 /DOutputDir=..\artifacts\release PeydaBrowser.iss
;
; The installer:
;  * installs per-machine to Program Files, keeps user data on upgrade,
;  * creates Start Menu shortcut (+ optional desktop shortcut),
;  * checks for the Microsoft Edge WebView2 Runtime and, if missing, offers to
;    open Microsoft's official download page (nothing is fetched silently),
;  * asks on uninstall whether profile data (%LOCALAPPDATA%\PeydaBrowser) may be deleted.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "Output"
#endif
; Numeric x.x.x.x for the PE version resource - AppVersion may carry a
; SemVer prerelease suffix (e.g. 0.1.0-alpha), which VersionInfoVersion rejects.
#ifndef NumericVersion
  #define NumericVersion "0.0.0.0"
#endif

#define AppName "Peyda Browser"
#define AppPublisher "Peyda Browser Contributors"
#define AppExe "PeydaBrowser.exe"
#define AppIdValue "{{8D5C5A95-86BD-4C38-BCC3-6D3FBD797640}"

[Setup]
AppId={#AppIdValue}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/Manixpcshot/peyda_browser
AppSupportURL=https://github.com/Manixpcshot/peyda_browser/issues
VersionInfoVersion={#NumericVersion}
DefaultDirName={autopf}\Peyda Browser
DefaultGroupName=Peyda Browser
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=PeydaBrowser-Setup-{#AppVersion}-win-{#Arch}
SetupIconFile=..\src\PeydaBrowser.App\PeydaBrowser.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0.17763
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Peyda Browser"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\Peyda Browser"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  WebView2ClientGuid = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function IsWebView2Installed(): Boolean;
var
  pv: String;
begin
  Result :=
    RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\' + WebView2ClientGuid, 'pv', pv) or
    RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WebView2ClientGuid, 'pv', pv) or
    RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WebView2ClientGuid, 'pv', pv);
  if Result then
    Result := (pv <> '') and (pv <> '0.0.0.0');
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not IsWebView2Installed() then
  begin
    if MsgBox('Peyda Browser needs the Microsoft Edge WebView2 Runtime, which was not detected on this PC.' + #13#10#13#10 +
              'Open Microsoft''s official WebView2 download page now? Setup will continue afterwards; please run Peyda Browser again once the runtime is installed.',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://go.microsoft.com/fwlink/p/?LinkId=2124703', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\PeydaBrowser');
    if DirExists(DataDir) then
    begin
      if MsgBox('Also delete your Peyda Browser profiles, history, bookmarks and settings?' + #13#10 + DataDir,
                mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;
