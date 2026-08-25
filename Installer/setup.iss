; ============================================================
; Novara 正式安装脚本（开发规范 5.1 / 5.1.1 定稿，2026-08-05）
; 编译：C:\Inno Setup 6\ISCC.exe setup.iss
; 前置：先执行 dotnet publish 产出松散发布文件夹（必须含 Novara.pri，
;       见开发规范 5.1 踩坑记录），路径见下方 #define PublishDir
; ============================================================

#define MyAppName "Novara"
#define MyAppVersion "5.0.0"
#define MyAppPublisher "Novara"
#define MyAppExeName "Novara.exe"
; E4-39: relative to this script (Installer\..\.. = the Desktop folder where Novara_Publish lives),
; no build-machine absolute path that breaks when the checkout moves.
#define PublishDir "..\..\Novara_Publish"

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
OutputDir=.
OutputBaseFilename=Novara_Setup_{#MyAppVersion}
SetupIconFile=..\Assets\128.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
WizardImageFile=wizard.bmp
WizardSmallImageFile=wizardsmall.bmp
; 程序单实例互斥：安装/卸载时若 Novara 正在运行则提示关闭
AppMutex=Local\Novara.SingleInstance
; 64 位应用（x64 发布）
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; 最低系统版本 = TargetPlatformMinVersion 10.0.19041（Win10 2004，与程序 TFM 一致）
MinVersion=10.0.19041
; 安装日志（便于排查安装问题）
SetupLogging=yes

[Languages]
; 中文简体 + English 双语（安装向导默认按系统语言，可手动切换；卸载器跟随安装语言）
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; 桌面快捷方式复选框（默认勾选：不加 unchecked flag）
Name: "desktopicon"; Description: "创建桌面快捷方式(&D)"; GroupDescription: "附加任务:"

[Files]
; 发布产物全部文件（含 Novara.pri，缺它启动闪退——见开发规范 5.1）
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; E5-11: desktop sticky-note host - deployed next to Novara.exe (FindHostExe release layout #1);
; Assets\128.ico is already pulled in by the recursesubdirs copy above.
Source: "{#PublishDir}\StickNoteHost.exe"; DestDir: "{app}"; Flags: ignoreversion
; 5.0 MCP: stdio MCP server 前端 - 与 Novara.exe 同目录（FindNovaraMcpExe release layout #1）
Source: "{#PublishDir}\NovaraMCP.exe"; DestDir: "{app}"; Flags: ignoreversion

[Run]
; 安装完成后勾选打开官网使用教程（novara.xin，默认勾选；官网未就绪期间可临时改回本地 txt）
Filename: "https://novara.xin"; Description: "查看 Novara 使用教程"; Flags: postinstall nowait skipifsilent shellexec

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

; 卸载时删除开机自启注册表项（2.5 写入的 HKCU\...\Run\Novara，防止卸载后开机报错）
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Novara"; Flags: uninsdeletevalue

; WebView2 runtime data (Novara.exe.WebView2\) is generated next to the exe at runtime and is NOT
; in the install manifest - without this the uninstaller would leave the whole folder behind.
[UninstallDelete]
Type: filesandordirs; Name: "{app}\Novara.exe.WebView2"

[Code]
// ============================================================
//  卸载数据选择（5.1.1 定稿，2026-08-05 重写）
//  官方 Inno 卸载事件只有 5 个专用函数（InitializeUninstall /
//  InitializeUninstallProgressForm / DeinitializeUninstall /
//  CurUninstallStepChanged / UninstallNeedRestart），不存在 un. 前缀
//  机制（6.6.1/6.7.3 实测 + 官方帮助文档确认），卸载向导也无法添加
//  自定义页——数据选择改为 CurUninstallStepChanged + CreateCustomForm
//  模态弹窗：usUninstall（卸载开始前）弹「保留/删除」单选，
//  选删除需输入 RESET 二次确认；usPostUninstall（卸载完成后）
//  按选择先去只读属性再删除数据目录。
// ============================================================

// E5-11: kill a running StickNoteHost before install/upgrade so its exe is not file-locked
// (AppMutex above only covers the main app's single-instance mutex).
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/IM StickNoteHost.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // 5.0 MCP: kill a running NovaraMCP.exe (stdio MCP front-end) so its exe is not file-locked during install
  Exec('taskkill.exe', '/IM NovaraMCP.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

// Icon-cache refresh: overlay-upgrade users keep an old exe icon in Explorer's iconcache_*.db
// (keyed by the install path), so shortcuts/taskbar can show the old logo even though the exe is new.
// Notify the shell after install so the new icon is picked up without a logoff.
procedure SHChangeNotify(wEventId: Longint; uFlags: Longint; dwItem1: Longint; dwItem2: Longint);
  external 'SHChangeNotify@shell32.dll stdcall';

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SHChangeNotify($08000000 {SHCNE_ASSOCCHANGED}, 0, 0, 0);
end;

var
  DeleteDataOnUninstall: Boolean;
  EditReset: TNewEdit; // 卸载数据选择弹窗的 RESET 输入框（Radio 单选事件需要全局访问）

function SetFileAttributesW(FileName: String; FileAttributes: DWORD): Boolean;
  external 'SetFileAttributesW@kernel32.dll stdcall';

// 递归去除目录下所有文件的只读属性（data.novadb 带 Hidden|ReadOnly，不去属性删不掉）
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

// 卸载数据选择弹窗：单选联动（选删除启用 RESET 输入框）
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
  ResultCode: Integer; // E5-11: taskkill exit code (ignored)
begin
  if CurUninstallStep = usUninstall then
  begin
    // E5-11: kill the desktop sticky-note host first - an uninstall while StickNoteHost is running
    // would leave a tray-resident process whose files are locked / orphaned.
    Exec('taskkill.exe', '/IM StickNoteHost.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // 默认保留（5.1.1：卸载确认 → 数据选择，默认保留）
    DeleteDataOnUninstall := False;
    // 静默卸载（/SILENT /VERYSILENT）：跳过交互弹窗直接保留数据——弹窗在静默模式下会阻塞自动化卸载（2026-08-05 实测卡住）
    if UninstallSilent then
      Exit;

    Form := CreateCustomForm(ScaleX(300), ScaleY(190), False, False); // 6.6.0+ 签名：宽高必须构造时指定；紧凑尺寸防高 DPI 放大后过大
    try
      Form.Caption := '是否同时删除所有数据';

      Lbl := TNewStaticText.Create(Form);
      Lbl.Parent := Form;
      Lbl.Left := ScaleX(16);
      Lbl.Top := ScaleY(12);
      Lbl.Width := Form.ClientWidth - ScaleX(32);
      Lbl.Height := ScaleY(60); // 多行文字必须给足高度，否则 WordWrap 只显示一行（文字被遮）
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
      RadioKeep.Height := ScaleY(26); // 显式高度：防高 DPI 下文字截断
      RadioKeep.Caption := '保留数据（推荐）';
      RadioKeep.Checked := True;
      RadioKeep.OnClick := @RadioKeepClick;

      RadioDelete := TNewRadioButton.Create(Form);
      RadioDelete.Parent := Form;
      RadioDelete.Left := ScaleX(16);
      RadioDelete.Top := ScaleY(104);
      RadioDelete.Width := Form.ClientWidth - ScaleX(32);
      RadioDelete.Height := ScaleY(26); // 显式高度：防高 DPI 下文字截断
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

      // 循环校验：选删除必须输入 RESET；取消/保留直接结束
      while True do
      begin
        if Form.ShowModal <> mrOk then
        begin
          DeleteDataOnUninstall := False; // 取消 → 保留
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
    if DirExists(ExpandConstant('{localappdata}\Novara')) then
    begin
      RemoveReadOnlyRecursive(ExpandConstant('{localappdata}\Novara'));
      DelTree(ExpandConstant('{localappdata}\Novara'), False, True, True);
    end;
  end;
end;
