; Fadem's installer: the standalone app, the MusicBee plugin, or both.
; Built by build-installer.ps1, which publishes both and passes their folders in with /D.
; Per-user, so it never asks for elevation: the app goes to %LOCALAPPDATA%\Programs\Fadem and
; the plugin to MusicBee's Plugins folder, which for an installed MusicBee is under %APPDATA%.

#define AppGuid "635505E0-1305-4468-B3A7-7289A6E7DE50"

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.0.0"
#endif
#ifndef AppSource
  #define AppSource "..\artifacts\publish\app"
#endif
#ifndef PluginSource
  #define PluginSource "..\artifacts\publish\plugin"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{{#AppGuid}}
AppName=Fadem
AppVersion={#AppVersion}
AppVerName=Fadem {#AppVersion}
AppPublisher=Daniel Towers
VersionInfoVersion={#NumericVersion}
DefaultDirName={autopf}\Fadem
DisableDirPage=yes
DefaultGroupName=Fadem
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=FademSetup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Fadem
UninstallDisplayIcon={app}\fadem.exe

[Types]
Name: "full"; Description: "Standalone app and MusicBee plugin"
Name: "app"; Description: "Standalone app only"
Name: "plugin"; Description: "MusicBee plugin only"
Name: "custom"; Description: "Custom"; Flags: iscustom

[Components]
Name: "app"; Description: "Fadem (standalone app)"; Types: full app
Name: "plugin"; Description: "Fadem for MusicBee (plugin; needs the 32-bit MusicBee 3)"; Types: full plugin

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Components: app; Flags: unchecked

[InstallDelete]
; The plugin's name before the rename (ADR 0018): left in place, MusicBee would load both.
Type: files; Name: "{code:PluginDir}\mb_faderio.dll"; Components: plugin
Type: filesandordirs; Name: "{code:PluginDir}\faderio"; Components: plugin

[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Components: app; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PluginSource}\mb_fadem.dll"; DestDir: "{code:PluginDir}"; Components: plugin; Flags: ignoreversion
Source: "{#PluginSource}\fadem\*"; DestDir: "{code:PluginDir}\fadem"; Components: plugin; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"

[Icons]
Name: "{autoprograms}\Fadem"; Filename: "{app}\fadem.exe"; Components: app
Name: "{autodesktop}\Fadem"; Filename: "{app}\fadem.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\fadem.exe"; Description: "Start Fadem"; Components: app; Flags: nowait postinstall skipifsilent

[Code]
var
  PluginPage: TInputDirWizardPage;
  PluginNextSteps: TOutputMsgWizardPage;

function PluginDir(Param: String): String;
begin
  Result := PluginPage.Values[0];
end;

function MusicBeeRunning: Boolean;
var
  Locator, Service, Found: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Found := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''MusicBee.exe''');
    Result := Found.Count > 0;
  except
  end;
end;

{ False when the user gives up rather than close MusicBee. }
function WaitForMusicBeeToClose(Action: String): Boolean;
begin
  Result := True;
  while MusicBeeRunning do
    if MsgBox('MusicBee is running. Close it, then click Retry to ' + Action + ' the Fadem plugin.',
              mbError, MB_RETRYCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
end;

procedure InitializeWizard;
begin
  PluginPage := CreateInputDirPage(wpSelectComponents,
    'MusicBee plugins folder', 'Where should the Fadem plugin go?',
    'For an installed MusicBee, keep this folder.' + #13#10 +
    'For a portable MusicBee, choose the Plugins folder inside its folder.',
    False, '');
  PluginPage.Add('');
  PluginPage.Values[0] := ExpandConstant('{param:PLUGINDIR|' +
    GetPreviousData('PluginDir', ExpandConstant('{userappdata}\MusicBee\Plugins')) + '}');

  PluginNextSteps := CreateOutputMsgPage(wpInfoAfter,
    'Setting up the MusicBee plugin', 'Two steps in MusicBee before Fadem can play there.',
    '1. Start MusicBee and open Edit > Edit Preferences > Plugins. Under Fadem, click Configure..., ' +
    'choose a library folder (Fadem keeps its playlists and transitions there), click OK, then Save.' + #13#10#13#10 +
    '2. Open View > Arrange Panels, tick Fadem, and click Save.' + #13#10#13#10 +
    'Optional: in Configure..., tick "Show Fadem in the Now Playing tab" for a full-size view.');
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  if WizardIsComponentSelected('plugin') then
    SetPreviousData(PreviousDataKey, 'PluginDir', PluginPage.Values[0]);
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := ((PageID = PluginPage.ID) or (PageID = PluginNextSteps.ID))
            and not WizardIsComponentSelected('plugin');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectComponents) and (WizardSelectedComponents(False) = '') then
  begin
    MsgBox('Choose the standalone app, the MusicBee plugin, or both.', mbError, MB_OK);
    Result := False;
  end
  else if (CurPageID = PluginPage.ID) and not DirExists(PluginPage.Values[0]) then
    Result := MsgBox('The folder' + #13#10 + PluginPage.Values[0] + #13#10 +
                     'does not exist, so MusicBee may not be there. Create it and install anyway?',
                     mbConfirmation, MB_YESNO) = IDYES
  else if (CurPageID = wpReady) and WizardIsComponentSelected('plugin') then
    Result := WaitForMusicBeeToClose('install');
end;

function InitializeUninstall: Boolean;
var
  Dir: String;
begin
  Result := True;
  if RegQueryStringValue(HKA, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{#AppGuid}}_is1',
                         'Inno Setup CodeFile: PluginDir', Dir)
     and FileExists(AddBackslash(Dir) + 'mb_fadem.dll') then
    Result := WaitForMusicBeeToClose('remove');
end;
