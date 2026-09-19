#define MyAppName "Novara"
#define MyAppVersion "8.0.0"
#define MyAppPublisher "Novara"
#define MyAppExeName "Novara.exe"


#define PublishDir "..\..\Novara_Publish"








#define ClientFileVersion GetVersionNumbersString(PublishDir + "\Novara.dll")
#if ClientFileVersion == ""
  #error PublishDir has no Novara.dll - run the publish step (see 工程设计 5.5) first.
#endif
#if Copy(ClientFileVersion, 1, Len(MyAppVersion)) != MyAppVersion
  #error PublishDir holds a stale client: Novara.dll version differs from MyAppVersion. Empty Novara_Publish and re-publish from scratch - never build on the previous release tree.
#endif

[Setup]
AppId={{9E7B2C41-8F3A-4D6B-9C1E-5A2B4D6F8E10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=no
AllowNoIcons=yes
OutputDir=..\..\Novara Release
OutputBaseFilename=Novara_Setup_{#MyAppVersion}
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

Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]

Name: "desktopicon"; Description: "创建桌面快捷方式(&D)"; GroupDescription: "附加任务:"

[Files]

Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs





Source: "{#PublishDir}\Host\*"; DestDir: "{app}\Host"; Flags: ignoreversion recursesubdirs createallsubdirs

Source: "{#PublishDir}\NovaraMCP.exe"; DestDir: "{app}"; Flags: ignoreversion










Source: "{#PublishDir}\Sync\*"; DestDir: "{app}\Sync"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Sync\1-建空间.cmd"; DestDir: "{app}\Sync"; Flags: ignoreversion
Source: "Sync\2-启动服务端.cmd"; DestDir: "{app}\Sync"; Flags: ignoreversion

[Run]

Filename: "https://novara.xin"; Description: "查看 Novara 使用教程"; Flags: postinstall nowait skipifsilent shellexec

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
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
begin
  if CurUninstallStep = usUninstall then
  begin


    Exec('taskkill.exe', '/IM StickNoteHost.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);


    Exec('taskkill.exe', '/IM NovaraMCP.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);



    Exec('taskkill.exe', '/IM NovaraSync.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    DeleteDataOnUninstall := False;

    if UninstallSilent then
      Exit;

    Form := CreateCustomForm(ScaleX(300), ScaleY(190), False, False);
    try
      Form.Caption := '是否同时删除所有数据';

      Lbl := TNewStaticText.Create(Form);
      Lbl.Parent := Form;
      Lbl.Left := ScaleX(16);
      Lbl.Top := ScaleY(12);
      Lbl.Width := Form.ClientWidth - ScaleX(32);
      Lbl.Height := ScaleY(60);
      Lbl.AutoSize := False;
      Lbl.WordWrap := True;
      Lbl.Caption := '数据保存在本机 %LocalAppData%\Novara（data.novadb）。'#13#10#13#10 +
        '保留数据：重装后自动恢复；如需备份请先打开 Novara，在设置页导出。'#13#10 +
        '删除全部：将永久删除日记、待办、备忘等全部数据，不可恢复。';

      RadioKeep := TNewRadioButton.Create(Form);
      RadioKeep.Parent := Form;
      RadioKeep.Left := ScaleX(16);
      RadioKeep.Top := ScaleY(78);
      RadioKeep.Width := Form.ClientWidth - ScaleX(32);
      RadioKeep.Height := ScaleY(26);
      RadioKeep.Caption := '保留数据（推荐）';
      RadioKeep.Checked := True;
      RadioKeep.OnClick := @RadioKeepClick;

      RadioDelete := TNewRadioButton.Create(Form);
      RadioDelete.Parent := Form;
      RadioDelete.Left := ScaleX(16);
      RadioDelete.Top := ScaleY(104);
      RadioDelete.Width := Form.ClientWidth - ScaleX(32);
      RadioDelete.Height := ScaleY(26);
      RadioDelete.Caption := '删除全部数据（需输入 RESET 确认）';
      RadioDelete.OnClick := @RadioDeleteClick;

      EditReset := TNewEdit.Create(Form);
      EditReset.Parent := Form;
      EditReset.Left := ScaleX(30);
      EditReset.Top := ScaleY(130);
      EditReset.Width := Form.ClientWidth - ScaleX(60);
      EditReset.Height := ScaleY(22);
      EditReset.Enabled := False;

      OkBtn := TNewButton.Create(Form);
      OkBtn.Parent := Form;
      OkBtn.Caption := '确定';
      OkBtn.Left := Form.ClientWidth - ScaleX(152);
      OkBtn.Top := Form.ClientHeight - ScaleY(44);
      OkBtn.Width := ScaleX(68);
      OkBtn.Height := ScaleY(26);
      OkBtn.Default := True;
      OkBtn.ModalResult := mrOk;

      CancelBtn := TNewButton.Create(Form);
      CancelBtn.Parent := Form;
      CancelBtn.Caption := '取消';
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
        MsgBox('输入错误，未删除数据。请输入 RESET 确认，或选择「保留数据」。', mbError, MB_OK);
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
        MsgBox('数据目录未能完全删除：'#13#10 + DataDir + #13#10#13#10 +
          '可能有程序仍在使用其中的文件（例如同步服务端、正在运行的 Novara 或杀毒软件扫描）。'#13#10 +
          '请关闭这些程序后手动删除该目录。', mbError, MB_OK);
    end;
  end;
end;
