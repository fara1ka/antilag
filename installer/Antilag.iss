; Antilag kurulum dosyası (Inno Setup 6)
; Derleme: build.ps1 (sürüm numarası src\CS2Hazirla\CS2Hazirla.csproj içindeki <Version>'dan gelir).
; Elle: ISCC.exe /DAppVersion=<sürüm> installer\Antilag.iss  ->  dist\Antilag-Setup.exe
;
; Tasarım: yönetici izni GEREKTİRMEZ (kullanıcı klasörüne kurar), açılışta çalışan hiçbir şey eklemez,
; kaldırınca dosyaları, kısayolları ve .NET önbelleğini siler. Ayarları silmeden önce sorar.
; Uygulama içi güncelleme: Antilag yeni kurulum dosyasını indirir, imzasını denetler, yanına "<dosya>.update" notunu bırakır ve
; Windows kabuğu aracılığıyla (yönetici hakları olmadan) başlatır. Not varsa kurulum hiç pencere açmaz (kendini sessiz yeniden başlatır),
; uygulamanın kapanmasını bekler, sormadan kurar ve uygulamayı yeniden açar. Not yoksa her şey eskisi gibidir.
; Ayar/kayıt klasörü eski adıyla (%APPDATA%\CS2Hazirla) kalır: bekleyen "geri yükleme" kaydı kaybolmasın diye.
; AppId eski sürümlerle aynıdır: kurulum, eski "Clear" kurulumunun üstüne gelir ve onu Antilag'a çevirir.

#ifndef AppVersion
  #error Sürüm numarası /DAppVersion=<sürüm> ile verilmeli (build.ps1 bunu csproj'dan okuyup verir).
#endif
#define AppName "Antilag"
#define AppExe "Antilag.exe"

[Setup]
AppId={{7C3B2A41-5D8E-4F61-9A07-2E4B6C1D8F35}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=fara1ka
AppPublisherURL=https://github.com/fara1ka/antilag
AppSupportURL=https://github.com/fara1ka/antilag/issues
AppUpdatesURL=https://github.com/fara1ka/antilag/releases
AppCopyright=Telif hakkı (C) 2026 fara1ka. GNU GPL v3 ya da sonrası.
VersionInfoCompany=fara1ka
VersionInfoCopyright=Copyright (C) 2026 fara1ka
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} kurulumu
DefaultDirName={localappdata}\Programs\Antilag
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0.14393
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=Antilag-Setup
SetupIconFile=..\src\CS2Hazirla\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/fast
SolidCompression=no
RestartApplications=no
; Uygulama yönetici olarak çalışır: Windows onu kendiliğinden kapatamaz. Kapanmasını güncelleme akışı (aşağıda) bekler.
CloseApplications=no
SetupMutex=AntilagSetup

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstüne kısayol koy"; GroupDescription: "Kısayollar:"

[InstallDelete]
; Eski adlarla (Clear, CS2 Hazırla) kurulmuş kopyalar ve kısayolları
Type: files; Name: "{localappdata}\Programs\Clear\Clear.exe"
Type: files; Name: "{localappdata}\Programs\Clear\unins000.exe"
Type: files; Name: "{localappdata}\Programs\Clear\unins000.dat"
Type: files; Name: "{localappdata}\Programs\Clear\unins000.msg"
Type: dirifempty; Name: "{localappdata}\Programs\Clear"
Type: files; Name: "{userdesktop}\Clear.lnk"
Type: files; Name: "{userprograms}\Clear.lnk"
Type: files; Name: "{localappdata}\Programs\CS2Hazirla\CS2Hazirla.exe"
Type: dirifempty; Name: "{localappdata}\Programs\CS2Hazirla"
Type: files; Name: "{userdesktop}\CS2 Hazırla.lnk"
Type: files; Name: "{userprograms}\CS2 Hazırla.lnk"
; her sürümün .NET önbellek klasörü ayrı birikir; uygulama kapalıyken eskileri temizle
Type: filesandordirs; Name: "{%TEMP}\.net\Antilag"
Type: filesandordirs; Name: "{%TEMP}\.net\Clear"
Type: filesandordirs; Name: "{%TEMP}\.net\CS2Hazirla"

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
; lisans ve üçüncü taraf bildirimleri programla birlikte gelir (GPL ve .NET çalışma zamanının MIT koşulu)
Source: "..\LICENSE"; DestDir: "{app}\licenses"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "..\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Run]
; Uygulama yönetici izni ister; shellexec ile başlatılınca Windows izin penceresini gösterir.
Filename: "{app}\{#AppExe}"; Description: "{#AppName} uygulamasını şimdi aç"; Flags: postinstall shellexec skipifsilent unchecked
; Güncellemeden sonra uygulama kendiliğinden yeniden açılır (kullanıcı zaten "Güncelle"ye bastı).
Filename: "{app}\{#AppExe}"; Flags: shellexec nowait; Check: ReopenAfterUpdate

[UninstallDelete]
; .NET tek-dosya uygulamasının ilk çalışmada çıkardığı önbellek (yalnızca dosya, süreç değil)
Type: filesandordirs; Name: "{%TEMP}\.net\Antilag"
Type: filesandordirs; Name: "{%TEMP}\.net\Clear"
Type: filesandordirs; Name: "{%TEMP}\.net\CS2Hazirla"
; güncellemenin indirdiği kurulum dosyaları
Type: filesandordirs; Name: "{localappdata}\Antilag\update"
Type: dirifempty; Name: "{localappdata}\Antilag"

[Code]
function DataDir(): String;
begin
  Result := ExpandConstant('{userappdata}\CS2Hazirla');
end;

function GetClassNameW(hWnd: HWND; lpClassName: String; nMaxCount: Integer): Integer;
  external 'GetClassNameW@user32.dll stdcall';

// Bir WPF penceresinin sınıf adı "HwndWrapper" ile başlar. Adı "Antilag" olan bir klasörün Gezgin penceresi aynı başlığı taşır ama uygulama değildir.
function IsAppWindow(const Title: String): Boolean;
var
  h: HWND;
  cls: String;
  n: Integer;
begin
  Result := False;
  h := FindWindowByWindowName(Title);
  if h = 0 then Exit;
  SetLength(cls, 256);
  n := GetClassNameW(h, cls, 256);
  if n > 0 then
  begin
    SetLength(cls, n);
    Result := Pos('HwndWrapper', cls) = 1;
  end;
end;

// Uygulama yönetici olarak açıkken kurulum onu kapatamaz (dosya kullanımda kalır): açık pencereyi arayıp kullanıcıya söyler.
function AppIsOpen(): Boolean;
begin
  Result := IsAppWindow('Antilag') or IsAppWindow('Clear');
end;

function CreateFileW(lpFileName: String; dwDesiredAccess, dwShareMode, lpSecurityAttributes, dwCreationDisposition, dwFlagsAndAttributes, hTemplateFile: Longint): Longint;
  external 'CreateFileW@kernel32.dll stdcall';
function CloseHandle(hObject: Longint): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

// Pencere kapandıktan sonra da işlem bir süre yaşar ve Antilag.exe kilitli kalır: dosya yazmaya açılabiliyorsa işlem gerçekten bitmiştir.
function ExeLocked(): Boolean;
var
  h: Longint;
  path: String;
begin
  Result := False;
  path := ExpandConstant('{localappdata}\Programs\Antilag\Antilag.exe');
  if not FileExists(path) then Exit;
  h := CreateFileW(path, $40000000, 1, 0, 3, $80, 0);    // GENERIC_WRITE, FILE_SHARE_READ, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL
  if h = -1 then
    Result := True
  else
    CloseHandle(h);
end;

type
  TProcessEntry32 = record
    dwSize, cntUsage, th32ProcessID, th32DefaultHeapID, th32ModuleID, cntThreads, th32ParentProcessID: Cardinal;
    pcPriClassBase: Longint;
    dwFlags: Cardinal;
    szExeFile: array[0..259] of Char;
  end;

function CreateToolhelp32Snapshot(dwFlags, th32ProcessID: Cardinal): Longint;
  external 'CreateToolhelp32Snapshot@kernel32.dll stdcall';
function Process32FirstW(hSnapshot: Longint; var lppe: TProcessEntry32): Boolean;
  external 'Process32FirstW@kernel32.dll stdcall';
function Process32NextW(hSnapshot: Longint; var lppe: TProcessEntry32): Boolean;
  external 'Process32NextW@kernel32.dll stdcall';

// Oyun (cs2.exe) çalışıyor mu? Yalnızca işlem listesine bakılır; hiçbir işleme tutamak açılmaz.
function GameIsRunning(): Boolean;
var
  snap: Longint;
  e: TProcessEntry32;
  name: String;
  i: Integer;
  more: Boolean;
begin
  Result := False;
  snap := CreateToolhelp32Snapshot($2, 0);               // TH32CS_SNAPPROCESS
  if snap = -1 then Exit;
  e.dwSize := SizeOf(e);
  more := Process32FirstW(snap, e);
  while more do
  begin
    name := '';
    for i := 0 to 259 do
    begin
      if e.szExeFile[i] = #0 then Break;
      name := name + e.szExeFile[i];
    end;
    if CompareText(name, 'cs2.exe') = 0 then
    begin
      Result := True;
      Break;
    end;
    more := Process32NextW(snap, e);
  end;
  CloseHandle(snap);
end;

var
  UpdateMode: Boolean;

function IsUpdate(): Boolean;
begin
  Result := UpdateMode;
end;

// Güncelleme sonrası uygulama yalnızca oyun açık değilse yeniden açılır (oyunun üstüne izin penceresi çıkmasın).
function ReopenAfterUpdate(): Boolean;
begin
  Result := UpdateMode and not GameIsRunning();
end;

function InstalledVersion(): String;
begin
  if not RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{7C3B2A41-5D8E-4F61-9A07-2E4B6C1D8F35}_is1', 'DisplayVersion', Result) then
    Result := '';
end;

function InitializeSetup(): Boolean;
var
  note, installed, logDir, logArg: String;
  waited, resultCode: Integer;
  a, b: Int64;
  isDowngrade: Boolean;
begin
  Result := True;
  // Uygulama bu kurulumu güncelleme için başlattıysa yanında "<dosya>.update" notu vardır.
  note := ExpandConstant('{srcexe}') + '.update';
  UpdateMode := FileExists(note);

  // daha yeni bir sürümün üstüne eskisini kurmak: uygulamanın güncellemesi bunu hiç yapmaz; elle çalıştırılan eski kurulum önce sorar
  isDowngrade := False;
  installed := InstalledVersion();
  if (installed <> '') and StrToVersion(installed, a) and StrToVersion('{#AppVersion}', b) then
    isDowngrade := ComparePackedVersion(a, b) > 0;

  if UpdateMode then
  begin
    // Uygulamanın kapanmasına bağlı olmayan kararlar önce verilir ve not yerinde bırakılır: açık kalan uygulama kurulumun başlamadığını görür
    // (süre dolunca bunu söyler), boşuna kapanmaz. Oyun açıksa hiçbir şey kurulmaz, hiçbir pencere açılmaz.
    if GameIsRunning() or isDowngrade then
    begin
      Result := False;
      Exit;
    end;
    // Kurulum penceresi açılmaz: güncelleme uygulamanın kendi kartında görünür. Pencereli başlatıldıysa (Windows kabuğu bayrak veremez) aynı kurulum
    // pencere olmadan yeniden başlatılır ve bu kopya hemen biter; not yerinde kalır, sessiz kopya onu üstlenir. Bir saniye sonra başlar ki bu kopya
    // kurulum mutex'ini bırakmış olsun. Başlatılamazsa pencereli kurulum olduğu gibi sürer.
    if not WizardSilent() then
    begin
      // günlük dosyası: kurulum klasörünün üst klasörü (güncelleme klasörü) varsa oraya, yoksa günlük tutulmaz
      logDir := ExpandConstant('{localappdata}\Antilag\update');
      if DirExists(logDir) then logArg := ' /LOG="' + logDir + '\kurulum.log"' else logArg := '';
      if Exec(ExpandConstant('{sys}\cmd.exe'),
              '/c ping -n 2 127.0.0.1 >nul & "' + ExpandConstant('{srcexe}') + '" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-' + logArg,
              '', SW_HIDE, ewNoWait, resultCode) then
      begin
        Result := False;
        Exit;
      end;
    end;
    // Notun silinmesi uygulamaya "kurulum üstlendi, kapanabilirsin" der.
    DeleteFile(note);
    // işlem bitip Antilag.exe serbest kalana kadar (en çok 20 sn) bekle
    waited := 0;
    while (AppIsOpen() or ExeLocked()) and (waited < 20000) do
    begin
      Sleep(250);
      waited := waited + 250;
    end;
    if AppIsOpen() or ExeLocked() then
    begin
      SuppressibleMsgBox('Antilag kapandı ama dosyası hâlâ kullanımda (bir virüs tarayıcısı tutuyor olabilir).' + #13#10 + #13#10 +
             'Birkaç saniye sonra Antilag''ı açıp güncellemeyi yeniden dene.', mbInformation, MB_OK, IDOK);
      // pencere olmadığından kutuyu kimse görmeyebilir: hiçbir şey değişmedi, eski Antilag yeniden açılır (açık bir kopya varsa dokunulmaz)
      if not AppIsOpen() then
        ShellExec('open', ExpandConstant('{localappdata}\Programs\Antilag\Antilag.exe'), '', '', SW_SHOWNORMAL, ewNoWait, resultCode);
      Result := False;
    end;
    Exit;
  end;

  // elle kurulum: pencere ve dosya kilidi birlikte bakılır (adı "Antilag" olan bir klasör penceresi uygulamayı gizleyemez)
  if AppIsOpen() or ExeLocked() then
  begin
    // SuppressibleMsgBox: sessiz kurulumda (/SUPPRESSMSGBOXES) kutu açılmaz, kurulum beklemeden iptal olur
    SuppressibleMsgBox('Antilag şu an açık.' + #13#10 + #13#10 +
           'Önce Antilag penceresindeki "Uygulamayı kapat" düğmesine bas, sonra kurulumu yeniden çalıştır.',
           mbInformation, MB_OK, IDOK);
    Result := False;
    Exit;
  end;
  if isDowngrade then
  begin
    if SuppressibleMsgBox('Bilgisayarda daha yeni bir Antilag (' + installed + ') kurulu.' + #13#10 + #13#10 +
           'Bu kurulum daha eski bir sürümü (' + '{#AppVersion}' + ') kurar. Yine de kurulsun mu?',
           mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDNO then
      Result := False;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if AppIsOpen() then
  begin
    SuppressibleMsgBox('Antilag şu an açık.' + #13#10 + #13#10 +
           'Önce Antilag penceresindeki "Uygulamayı kapat" düğmesine bas, sonra kaldırmayı yeniden dene.',
           mbInformation, MB_OK, IDOK);
    Result := False;
    Exit;
  end;
  if UninstallSilent() then Exit;
  // state.json = "geri yüklenmemiş değişiklikler" kaydı. Silinirse geri yükleme düğmesi çalışmaz.
  if FileExists(DataDir() + '\state.json') then
    Result := MsgBox(
      'Antilag''ın yaptığı değişiklikler (kapatılan programlar, Windows ayarları) hâlâ geri yüklenmemiş görünüyor.' + #13#10 + #13#10 +
      'Şimdi kaldırırsan geri yükleme düğmesini kullanamazsın (kapanan programlar açılmaz, ayarlar kendiliğinden eski haline dönmez). Önce uygulamayı açıp ana ekrandaki geri yükleme düğmesine bas.' + #13#10 + #13#10 +
      'Yine de kaldırılsın mı?',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

// Güncellemede sorulacak bir şey yok: görev ve hazır sayfaları atlanır, bitiş sayfası kendiliğinden kapanır.
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := UpdateMode and ((PageID = wpSelectTasks) or (PageID = wpReady));
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if UpdateMode and (CurPageID = wpFinished) then
    WizardForm.NextButton.OnClick(WizardForm.NextButton);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  if UninstallSilent() then Exit;                 // sessiz kaldırmada ayarlara dokunma
  if not DirExists(DataDir()) then Exit;
  if MsgBox(
    'Ayarların ve günlük dosyaların da silinsin mi?' + #13#10 + #13#10 +
    '(Hayır dersen bunlar kalır; uygulamayı daha sonra yeniden kurarsan ayarların korunur.)',
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    DelTree(DataDir(), True, True, True);
end;
