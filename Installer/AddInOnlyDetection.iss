// Detection of a former add-in-only setup, shared by MechCue.iss and scripts/Test-InstallerDetection.ps1.
// The test harness overrides DetectRoot/DetectKey to use a temporary HKCU key.
#ifndef DetectRoot
  #define DetectRoot "HKLM64"
#endif
#ifndef DetectKey
  #define DetectKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\MechCue.TimeChart_is1"
#endif

function ListHas(const List, Item: String): Boolean;
begin
  Result := Pos(',' + Item + ',', ',' + List + ',') > 0;
end;

{ Setup recreates its uninstall key after [Registry], so values written there do not persist.
  Detect a former add-in-only setup from what it left: no standalone choice recorded by Inno,
  and no MechCue.exe in the previous folder (or its "(アドイン版)" display name). }
function PreviousAddInOnlySetup: Boolean;
var Selected, Deselected, AppPath, Name: String;
begin
  Result := False;
  if not RegKeyExists({#DetectRoot}, '{#DetectKey}') then Exit;
  RegQueryStringValue({#DetectRoot}, '{#DetectKey}', 'Inno Setup: Selected Components', Selected);
  RegQueryStringValue({#DetectRoot}, '{#DetectKey}', 'Inno Setup: Deselected Components', Deselected);
  if ListHas(Selected, 'standalone') or ListHas(Deselected, 'standalone') then Exit;
  if RegQueryStringValue({#DetectRoot}, '{#DetectKey}', 'Inno Setup: App Path', AppPath) and DirExists(AppPath) then
    Result := not FileExists(AddBackslash(AppPath) + 'MechCue.exe')
  else if RegQueryStringValue({#DetectRoot}, '{#DetectKey}', 'DisplayName', Name) then
    Result := Pos('アドイン版', Name) > 0;
end;
