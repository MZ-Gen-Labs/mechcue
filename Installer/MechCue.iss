#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef BinaryVersion
  #error BinaryVersion is required
#endif
#ifndef PayloadDir
  #error PayloadDir is required
#endif
#ifndef OutputFolder
  #error OutputFolder is required
#endif
#ifndef ProjectRoot
  #error ProjectRoot is required
#endif
#ifndef AddInOnly
  #define AddInOnly "0"
#endif
#if AddInOnly == "1"
  #define EditionName "MechCue タイムチャート（アドイン版）"
  #define SetupName "MechCue-" + AppVersion + "-AddIn-Setup"
  #define InstallMode "AddInOnly"
#else
  #define EditionName "MechCue タイムチャート"
  #define SetupName "MechCue-" + AppVersion + "-Setup"
  #define InstallMode "Full"
#endif

[Setup]
AppId=MechCue.TimeChart
AppName={#EditionName}
AppVersion={#AppVersion}
AppPublisher=MZ-Gen-Labs
AppPublisherURL=https://github.com/MZ-Gen-Labs/mechcue
AppSupportURL=https://github.com/MZ-Gen-Labs/mechcue/issues
AppUpdatesURL=https://github.com/MZ-Gen-Labs/mechcue/releases
DefaultDirName={autopf}\MechCue
DefaultGroupName=MechCue
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputFolder}
OutputBaseFilename={#SetupName}
VersionInfoVersion={#BinaryVersion}
VersionInfoProductVersion={#BinaryVersion}
VersionInfoTextVersion={#AppVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoCompany=MZ-Gen-Labs
VersionInfoDescription=MechCue Solid Edge assembly time-chart setup
VersionInfoCopyright=Copyright (c) 2026 MechCue contributors
LicenseFile={#ProjectRoot}\LICENSE
Compression=lzma2
SolidCompression=no
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayName={#EditionName}
SetupLogging=yes

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PayloadDir}\MechCue.AddIn.comhost.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\MechCue.AddIn.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\MechCue.AddIn.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\MechCue.AddIn.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\MechCue.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\MechCue.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\MechCue.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
#if AddInOnly == "0"
Source: "{#PayloadDir}\MechCue.exe"; DestDir: "{app}"; Flags: ignoreversion
#endif
Source: "{#ProjectRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ProjectRoot}\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ProjectRoot}\examples\sequence.json"; DestDir: "{app}"; DestName: "Example-Sequence.json"; Flags: onlyifdoesntexist

[Registry]
Root: HKLM; Subkey: "Software\Classes\CLSID\{{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}"; ValueType: string; ValueName: ""; ValueData: "MechCue Time Chart"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\CLSID\{{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}"; ValueType: dword; ValueName: "AutoConnect"; ValueData: "1"
Root: HKLM; Subkey: "Software\Classes\CLSID\{{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}"; ValueType: string; ValueName: "409"; ValueData: "MechCue Time Chart"
Root: HKLM; Subkey: "Software\Classes\CLSID\{{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}"; ValueType: string; ValueName: "411"; ValueData: "MechCue Time Chart"
Root: HKLM; Subkey: "Software\Classes\CLSID\{{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}\InprocServer32"; ValueType: string; ValueName: ""; ValueData: "{app}\MechCue.AddIn.comhost.dll"
Root: HKLM; Subkey: "Software\Classes\CLSID\{{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Both"
Root: HKLM; Subkey: "Software\Classes\CLSID\{{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}\ProgID"; ValueType: string; ValueName: ""; ValueData: "MechCue.TimeChartAddIn"
Root: HKLM; Subkey: "Software\Classes\CLSID\{{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}\Implemented Categories\{{26B1D2D1-2B03-11D2-B589-080036E8B802}"; ValueType: none
Root: HKLM; Subkey: "Software\Classes\CLSID\{{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}\Environment Categories\{{26618395-09D6-11D1-BA07-080036230602}"; ValueType: none
Root: HKLM; Subkey: "Software\Classes\CLSID\{{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}\Summary"; ValueType: string; ValueName: "409"; ValueData: "Time-displacement assembly control"
Root: HKLM; Subkey: "Software\Classes\CLSID\{{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}\Summary"; ValueType: string; ValueName: "411"; ValueData: "Time-displacement assembly control"
Root: HKLM; Subkey: "Software\Classes\MechCue.TimeChartAddIn"; ValueType: none; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\MechCue.TimeChartAddIn\CLSID"; ValueType: string; ValueName: ""; ValueData: "{code:AddInClsid}"
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\MechCue.TimeChart_is1"; ValueType: string; ValueName: "InstallMode"; ValueData: "{#InstallMode}"

#if AddInOnly == "0"
[Icons]
Name: "{autoprograms}\MechCue"; Filename: "{app}\MechCue.exe"
#endif

[Code]
function AddInClsid(Param: String): String;
begin
  Result := '{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}';
end;

function HasDesktopRuntime: Boolean;
var Names: TArrayOfString; I: Integer; Search: TFindRec;
begin
  Result := False;
  if RegGetValueNames(HKLM64, 'Software\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Pos('8.', Names[I]) = 1 then Result := True;
  if not Result then
    if FindFirst(ExpandConstant('{pf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), Search) then
    begin
      try
        repeat
          if Search.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0 then Result := True;
        until not FindNext(Search);
      finally
        FindClose(Search);
      end;
    end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var ClassId, Server, Mode: String; Host: Variant;
begin
  Result := '';
  if RegKeyExists(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\MechCue.TimeChart') then
    Result := '以前のMechCueをWindowsのアプリ一覧からアンインストールしてから実行してください。'
  else if RegQueryStringValue(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\MechCue.TimeChart_is1', 'InstallMode', Mode) and (Mode <> '{#InstallMode}') then
    Result := '別の構成のMechCueを先にアンインストールしてください。'
  else if RegKeyExists(HKCU64, 'Software\Classes\CLSID\' + AddInClsid('')) then
    Result := 'ユーザー単位のMechCue登録を解除してから実行してください。'
  else if not HasDesktopRuntime then
    Result := '.NET 8 Desktop Runtime (x64) が必要です。'
  else if not RegQueryStringValue(HKCR64, 'SolidEdge.Application\CLSID', '', ClassId) then
    Result := 'Solid Edge 2026のインストールを確認してください。'
  else if not RegQueryStringValue(HKCR64, 'CLSID\' + ClassId + '\LocalServer32', '', Server) then
    Result := 'Solid Edgeの登録情報を確認してください。';
  if Result = '' then
  begin
    try
      Host := GetActiveOleObject('SolidEdge.Application');
      Result := 'Solid Edgeの作業を保存して終了してから実行してください。';
    except
      { No running instance accessible; Restart Manager also checks files in use. }
    end;
  end;
end;