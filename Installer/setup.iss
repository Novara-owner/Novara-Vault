#define MyAppName "Novara"



#define MyAppVersion "12.0.0"
#define MyAppDisplayVersion "novara12"
#define MyAppPublisher "Novara"
#define MyAppExeName "Novara.exe"


#define PublishDir "..\..\Novara_Publish"








#define ClientFileVersion GetVersionNumbersString(PublishDir + "\Novara.dll")
#if ClientFileVersion == ""
  #error PublishDir has no Novara.dll - run the publish step first.
#endif
#if Copy(ClientFileVersion, 1, Len(MyAppVersion)) != MyAppVersion
  #error PublishDir holds a stale client: Novara.dll version differs from MyAppVersion. Empty Novara_Publish and re-publish from scratch - never build on the previous release tree.
#endif



#if !FileExists(PublishDir + "\Novara.pri")
  #error PublishDir has no Novara.pri - copy it from the Release win-x64 output before compiling.
#endif

[Setup]
AppId={{9E7B2C41-8F3A-4D6B-9C1E-5A2B4D6F8E10}
AppName={#MyAppName}
AppVersion={#MyAppDisplayVersion}
AppVerName={#MyAppName} {#MyAppDisplayVersion}
AppPublisher={#MyAppPublisher}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}.0
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=no
AllowNoIcons=yes
OutputDir=..\..\Novara Release
OutputBaseFilename=Novara_Setup_{#MyAppDisplayVersion}
SetupIconFile=..\Assets\128.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
WizardImageFile=wizard.bmp
WizardSmallImageFile=wizardsmall.bmp

AppMutex=Local\Novara.SingleInstance

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

MinVersion=10.0.19041

SetupLogging=yes

[Languages]

Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]


chinesesimplified.DeleteDataFailed=数据目录未能完全删除：%n%1%n%n可能有程序仍在使用其中的文件（例如同步服务端、正在运行的 Novara 或杀毒软件扫描）。请关闭这些程序后手动删除该目录。
english.DeleteDataFailed=The data directory could not be fully removed:%n%1%n%nA file inside it may still be in use (the sync server, a running Novara, or an antivirus scan). Close those programs and delete the directory by hand.
chinesesimplified.CreateDesktopIcon=创建桌面快捷方式(&D)
english.CreateDesktopIcon=Create a &desktop shortcut
chinesesimplified.AdditionalTasks=附加任务:
english.AdditionalTasks=Additional tasks:
chinesesimplified.ViewHelp=查看 Novara 使用教程
english.ViewHelp=Open the Novara help centre
chinesesimplified.UninstallShortcut=卸载 Novara
english.UninstallShortcut=Uninstall Novara
chinesesimplified.UninstallDataCaption=是否同时删除所有数据
english.UninstallDataCaption=Delete all data as well?
chinesesimplified.UninstallDataIntro=数据保存在本机 %LocalAppData%\Novara（data.novadb）。%n%n保留数据：重装后自动恢复；如需备份请先打开 Novara，在设置页导出。%n删除全部：将永久删除日记、待办、备忘等全部数据，不可恢复。
english.UninstallDataIntro=Your data lives in %LocalAppData%\Novara (data.novadb).%n%nKeep data: it is picked up again after a reinstall. To back it up first, open Novara and export it from Settings.%nDelete all: permanently removes every diary, to-do, memo and other entry. This cannot be undone.
chinesesimplified.KeepData=保留数据（推荐）
english.KeepData=Keep my data (recommended)
chinesesimplified.DeleteData=删除全部数据（需输入 RESET 确认）
english.DeleteData=Delete all data (type RESET to confirm)
chinesesimplified.Ok=确定
english.Ok=OK
chinesesimplified.Cancel=取消
english.Cancel=Cancel
chinesesimplified.ResetMismatch=输入错误，未删除数据。请输入 RESET 确认，或选择「保留数据」。
english.ResetMismatch=Nothing was deleted. Type RESET to confirm, or choose Keep my data.

[Tasks]

Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalTasks}"

[Files]

Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs





Source: "{#PublishDir}\Host\*"; DestDir: "{app}\Host"; Flags: ignoreversion recursesubdirs createallsubdirs

Source: "{#PublishDir}\NovaraMCP.exe"; DestDir: "{app}"; Flags: ignoreversion














Source: "{#PublishDir}\Sync\*"; DestDir: "{app}\Sync"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Sync\1-建空间.cmd"; DestDir: "{app}\Sync"; Flags: ignoreversion
Source: "Sync\2-启动服务端.cmd"; DestDir: "{app}\Sync"; Flags: ignoreversion
Source: "Sync\3-查看空间.cmd"; DestDir: "{app}\Sync"; Flags: ignoreversion

[Run]




Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait runasoriginaluser


Filename: "https://novara.xin/help/"; Description: "{cm:ViewHelp}"; Flags: postinstall nowait skipifsilent shellexec

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallShortcut}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon


[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Novara"; Flags: uninsdeletevalue



Root: HKCU; Subkey: "Software\Classes\DesktopBackground\shell\NovaraOpen"; Flags: uninsdeletekey dontcreatekey
Root: HKCU; Subkey: "Software\Classes\*\shell\NovaraAddPath"; Flags: uninsdeletekey dontcreatekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\NovaraAddPath"; Flags: uninsdeletekey dontcreatekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.md\shell\NovaraImport"; Flags: uninsdeletekey dontcreatekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.markdown\shell\NovaraImport"; Flags: uninsdeletekey dontcreatekey



[UninstallDelete]
Type: filesandordirs; Name: "{app}\Novara.exe.WebView2"


Type: filesandordirs; Name: "{localappdata}\Novara\Webview2"

[Code]














function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/IM StickNoteHost.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Exec('taskkill.exe', '/IM NovaraMCP.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);



  Exec('taskkill.exe', '/IM NovaraSync.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;




procedure SHChangeNotify(wEventId: Longint; uFlags: Longint; dwItem1: Longint; dwItem2: Longint);
  external 'SHChangeNotify@shell32.dll stdcall';

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SHChangeNotify($08000000 , 0, 0, 0);
end;

var
  DeleteDataOnUninstall: Boolean;
  EditReset: TNewEdit;

function SetFileAttributesW(FileName: String; FileAttributes: DWORD): Boolean;
  external 'SetFileAttributesW@kernel32.dll stdcall';


procedure RemoveReadOnlyRecursive(const Dir: String);
var
  FindRec: TFindRec;
  Full: String;
begin
  if FindFirst(Dir + '\*', FindRec) then
  try
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
      begin
        Full := Dir + '\' + FindRec.Name;
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          RemoveReadOnlyRecursive(Full)
        else
          SetFileAttributesW(Full, FILE_ATTRIBUTE_NORMAL);
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;


procedure RadioKeepClick(Sender: TObject);
begin
  EditReset.Enabled := False;
end;

procedure RadioDeleteClick(Sender: TObject);
begin
  EditReset.Enabled := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Form: TSetupForm;
  Lbl: TNewStaticText;
  RadioKeep, RadioDelete: TNewRadioButton;
  OkBtn, CancelBtn: TNewButton;
  ResultCode: Integer;
  DataDir: String;
  DeltaY: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin


    Exec('taskkill.exe', '/IM StickNoteHost.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);


    Exec('taskkill.exe', '/IM NovaraMCP.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);



    Exec('taskkill.exe', '/IM NovaraSync.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    DeleteDataOnUninstall := False;

    if UninstallSilent then
      Exit;





    DeltaY := 0;
    if ActiveLanguage = 'english' then DeltaY := ScaleY(54);

    Form := CreateCustomForm(ScaleX(300), ScaleY(190) + DeltaY, False, False);
    try
      Form.Caption := ExpandConstant('{cm:UninstallDataCaption}');

      Lbl := TNewStaticText.Create(Form);
      Lbl.Parent := Form;
      Lbl.Left := ScaleX(16);
      Lbl.Top := ScaleY(12);
      Lbl.Width := Form.ClientWidth - ScaleX(32);
      Lbl.Height := ScaleY(60) + DeltaY;
      Lbl.AutoSize := False;
      Lbl.WordWrap := True;
      Lbl.Caption := ExpandConstant('{cm:UninstallDataIntro}');

      RadioKeep := TNewRadioButton.Create(Form);
      RadioKeep.Parent := Form;
      RadioKeep.Left := ScaleX(16);
      RadioKeep.Top := ScaleY(78) + DeltaY;
      RadioKeep.Width := Form.ClientWidth - ScaleX(32);
      RadioKeep.Height := ScaleY(26);
      RadioKeep.Caption := ExpandConstant('{cm:KeepData}');
      RadioKeep.Checked := True;
      RadioKeep.OnClick := @RadioKeepClick;

      RadioDelete := TNewRadioButton.Create(Form);
      RadioDelete.Parent := Form;
      RadioDelete.Left := ScaleX(16);
      RadioDelete.Top := ScaleY(104) + DeltaY;
      RadioDelete.Width := Form.ClientWidth - ScaleX(32);
      RadioDelete.Height := ScaleY(26);
      RadioDelete.Caption := ExpandConstant('{cm:DeleteData}');
      RadioDelete.OnClick := @RadioDeleteClick;

      EditReset := TNewEdit.Create(Form);
      EditReset.Parent := Form;
      EditReset.Left := ScaleX(30);
      EditReset.Top := ScaleY(130) + DeltaY;
      EditReset.Width := Form.ClientWidth - ScaleX(60);
      EditReset.Height := ScaleY(22);
      EditReset.Enabled := False;

      OkBtn := TNewButton.Create(Form);
      OkBtn.Parent := Form;
      OkBtn.Caption := ExpandConstant('{cm:Ok}');
      OkBtn.Left := Form.ClientWidth - ScaleX(152);
      OkBtn.Top := Form.ClientHeight - ScaleY(44);
      OkBtn.Width := ScaleX(68);
      OkBtn.Height := ScaleY(26);
      OkBtn.Default := True;
      OkBtn.ModalResult := mrOk;

      CancelBtn := TNewButton.Create(Form);
      CancelBtn.Parent := Form;
      CancelBtn.Caption := ExpandConstant('{cm:Cancel}');
      CancelBtn.Left := Form.ClientWidth - ScaleX(78);
      CancelBtn.Top := Form.ClientHeight - ScaleY(44);
      CancelBtn.Width := ScaleX(68);
      CancelBtn.Height := ScaleY(26);
      CancelBtn.Cancel := True;
      CancelBtn.ModalResult := mrCancel;


      while True do
      begin
        if Form.ShowModal <> mrOk then
        begin
          DeleteDataOnUninstall := False;
          Break;
        end;
        if RadioKeep.Checked then
        begin
          DeleteDataOnUninstall := False;
          Break;
        end;
        if EditReset.Text = 'RESET' then
        begin
          DeleteDataOnUninstall := True;
          Break;
        end;
        MsgBox(ExpandConstant('{cm:ResetMismatch}'), mbError, MB_OK);
      end;
    finally
      Form.Free;
    end;
  end
  else if (CurUninstallStep = usPostUninstall) and DeleteDataOnUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\Novara');
    if DirExists(DataDir) then
    begin
      RemoveReadOnlyRecursive(DataDir);
      DelTree(DataDir, False, True, True);



      if DirExists(DataDir) then
        MsgBox(FmtMessage(CustomMessage('DeleteDataFailed'), [DataDir]), mbError, MB_OK);
    end;
  end;
end;
