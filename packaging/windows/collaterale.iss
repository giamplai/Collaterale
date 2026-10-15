; Inno Setup script (built in CI on Windows): iscc /DAppVersion=X packaging\windows\collaterale.iss
#ifndef AppVersion
#define AppVersion "0.0.0"
#endif
[Setup]
AppName=Collaterale
AppVersion={#AppVersion}
DefaultDirName={autopf}\Collaterale
DefaultGroupName=Collaterale
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputBaseFilename=Collaterale-{#AppVersion}-win-x64-setup
OutputDir=..\..\artifacts\packages
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
[Files]
Source: "..\..\artifacts\publish\win-x64\Collaterale.exe"; DestDir: "{app}"
[Icons]
Name: "{group}\Collaterale"; Filename: "{app}\Collaterale.exe"
[Registry]
Root: HKA; Subkey: "Software\Classes\*\shell\CollateraleSelect"; ValueType: string; ValueData: "Collaterale: select as left side"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\*\shell\CollateraleSelect\command"; ValueType: string; ValueData: """{app}\Collaterale.exe"" --select-left ""%1"""
Root: HKA; Subkey: "Software\Classes\*\shell\CollateraleCompare"; ValueType: string; ValueData: "Collaterale: compare with the selected"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\*\shell\CollateraleCompare\command"; ValueType: string; ValueData: """{app}\Collaterale.exe"" --compare-selected ""%1"""
Root: HKA; Subkey: "Software\Classes\Directory\shell\CollateraleSelect"; ValueType: string; ValueData: "Collaterale: select as left side"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Directory\shell\CollateraleSelect\command"; ValueType: string; ValueData: """{app}\Collaterale.exe"" --select-left ""%1"""
Root: HKA; Subkey: "Software\Classes\Directory\shell\CollateraleCompare"; ValueType: string; ValueData: "Collaterale: compare with the selected"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Directory\shell\CollateraleCompare\command"; ValueType: string; ValueData: """{app}\Collaterale.exe"" --compare-selected ""%1"""
