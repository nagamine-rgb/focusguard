; FocusGuard のインストーラー（Inno Setup 6）
; CI では次のようにビルドする:  ISCC.exe /DAppVersion=1.0.123 windows\installer.iss
;
; - 管理者権限なしで、ユーザーごとの場所（%LOCALAPPDATA%\Programs\FocusGuard）に入れる
; - FocusGuard が動いている間はインストール・アンインストールさせない
;   （Lv.3 中はアプリを終了できないので、インストーラーがロックの抜け道にならない）
; - 設定とブロックの状態（%APPDATA%\FocusGuard）は、更新でもアンインストールでも消さない

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{6F2B9C1E-4D7A-4E5B-9A13-F0C6D2E8B471}
AppName=FocusGuard
AppVersion={#AppVersion}
AppVerName=FocusGuard {#AppVersion}
AppPublisher=nagamine-rgb
AppPublisherURL=https://github.com/nagamine-rgb/focusguard
AppSupportURL=https://github.com/nagamine-rgb/focusguard
AppUpdatesURL=https://github.com/nagamine-rgb/focusguard/releases/latest
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\FocusGuard
DisableDirPage=yes
DefaultGroupName=FocusGuard
DisableProgramGroupPage=yes
OutputDir=..
OutputBaseFilename=FocusGuard-Setup
SetupIconFile=icon.ico
UninstallDisplayIcon={app}\FocusGuard.exe
UninstallDisplayName=FocusGuard
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; 動いている FocusGuard を勝手に閉じない（Lv.3 のロックを外せてしまうため）
CloseApplications=no
AppMutex=Local\FocusGuard_SingleInstance_Mutex,Local\FocusGuard_Watchdog_Mutex

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Messages]
japanese.SetupAppRunningError=FocusGuard が起動しています。%n%nタスクトレイの FocusGuard のアイコンを右クリックして「終了」してから、「OK」を押してください。%nLv.3 のブロック中は終了できないので、ブロックが終わってから更新してください。
japanese.UninstallAppRunningError=FocusGuard が起動しています。%n%nタスクトレイの FocusGuard のアイコンを右クリックして「終了」してから、「OK」を押してください。%nLv.3 のブロック中は終了できないので、ブロックが終わってからアンインストールしてください。

[Tasks]
Name: "desktopicon"; Description: "デスクトップにショートカットを作る"; GroupDescription: "ショートカット:"; Flags: unchecked

[Files]
Source: "FocusGuard.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\FocusGuard"; Filename: "{app}\FocusGuard.exe"
Name: "{userdesktop}\FocusGuard"; Filename: "{app}\FocusGuard.exe"; Tasks: desktopicon

[Registry]
; アンインストール時に「Windows の起動時に自動で起動する」の登録を消す（インストール時は何もしない）
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "FocusGuard"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\FocusGuard.exe"; Description: "FocusGuard を起動する"; Flags: nowait postinstall skipifsilent
