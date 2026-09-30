; PDM Variable Studio — Inno Setup 6 kurulum betiği.
;
; Derleme: docs\build-installer.ps1 (sürümü ProductInfo.Version'dan okuyup /DAppVersion ile
; geçirir). Elle: ISCC.exe /DAppVersion=1.3.0 installer\PdmVariableStudio.iss
;
; Kurulum ne yapar:
;   1. .NET Framework 4.8.1 yoksa Microsoft'un web yükleyicisini indirip çalıştırır.
;   2. SOLIDWORKS PDM istemcisinin kurulu olduğunu doğrular (EPDM.Interop.epdm.dll).
;      Yoksa kurulum durur: uygulama onsuz çalışamaz ve interop'u biz dağıtmayız.
;   3. Uygulamayı {app} altına kopyalar; interop'u PDM istemcisinden ALIR (pakette yok).
;   4. HKLM\SOFTWARE\PdmVariableStudio\InstallPath yazar (eklenti uygulamayı buradan bulur).
;   5. {app}\AddIn\ altına eklenti DLL'i + interop kopyasını koyar: kullanıcı bu iki dosyayı
;      Administration ile vault'a yükler. Bu adım otomatikleştirilemez (PDM yönetici işi).
;   6. Sihirbazın dilini (tr/en) HKLM\SOFTWARE\PdmVariableStudio\Language'a yazar: uygulama
;      ve eklenti bu makinede o dilde açılır (kullanıcı uygulamada değiştirebilir, HKCU).
;      İki dil için ayrı kurulum YOK — eklenti vault'ta tektir ve her istemci kendi dilini
;      seçer; iki ayrı paket, aynı vault'u kullanan farklı dilli istemcileri bölerdi.
;
; Kaldırma %LOCALAPPDATA%\PdmVariableStudio\ altına DOKUNMAZ: işlem geçmişi silinirse
; yapılmış bir değişiklik geri alınamaz hâle gelir.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName "PDM Variable Studio"
#define AppPublisher "Abdussamed Tarlak"
#define AppUrl "https://github.com/aSamed93/PdmVariableStudio"
#define AppExe "PdmVariableStudio.exe"
#define InteropDll "EPDM.Interop.epdm.dll"
#define AppBin "..\src\PdmVariableStudio.App\bin\Release\net481"
#define AddInBin "..\src\PdmVariableStudio.AddIn\bin\Release\net481"

; .NET Framework 4.8.1: NDP\v4\Full\Release >= 533320. Web yükleyici fwlink'i
; (LinkId=2203304 -> NDP481-Web.exe) 2026-09-21'de doğrulandı.
#define NetRelease 533320
#define NetWebInstallerUrl "https://go.microsoft.com/fwlink/?LinkId=2203304"

[Setup]
AppId={{F97BBFC0-E5E2-4942-8BCB-AE0A6C3F696E}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts
OutputBaseFilename=PdmVariableStudio-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; PDM istemcisi 64-bit; uygulama AnyCPU. Program Files (64-bit) altına kurulur.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
; Çalışan uygulama varsa Restart Manager ile kapatılması istenir.
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
MinVersion=10.0

[Languages]
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
tr.PdmMissing=SOLIDWORKS PDM istemcisi bu bilgisayarda bulunamadı:%n%n%1%n%nPDM Variable Studio yalnızca PDM Professional istemcisi (2022 / 30.0 veya üstü) kurulu bilgisayarlarda çalışır. Önce PDM istemcisini kurun.
en.PdmMissing=SOLIDWORKS PDM client was not found on this computer:%n%n%1%n%nPDM Variable Studio requires the PDM Professional client (2022 / 30.0 or later). Install the PDM client first.
tr.NetDownloading=.NET Framework 4.8.1 indiriliyor…
en.NetDownloading=Downloading .NET Framework 4.8.1…
tr.NetInstalling=.NET Framework 4.8.1 kuruluyor (birkaç dakika sürebilir)…
en.NetInstalling=Installing .NET Framework 4.8.1 (this may take a few minutes)…
tr.NetFailed=.NET Framework 4.8.1 kurulamadı (kod %1).%n%nElle kurup kurulumu yeniden çalıştırın:%n%2
en.NetFailed=.NET Framework 4.8.1 could not be installed (code %1).%n%nInstall it manually and run setup again:%n%2
tr.NetDownloadFailed=.NET Framework 4.8.1 indirilemedi. İnternet bağlantısını kontrol edin ya da elle kurun:%n%1
en.NetDownloadFailed=.NET Framework 4.8.1 could not be downloaded. Check the connection or install it manually:%n%1
tr.AddInHint=Eklenti dosyaları (vault'a yükleyin)
en.AddInHint=Add-in files (upload to vault)
tr.Guide=Kullanım Kılavuzu
en.Guide=User Guide
tr.FinishedAddIn=Uygulama kuruldu.%n%nSon adım PDM yöneticisinindir ve bir kez yapılır: PDM Administration → vault → Add-ins → New Add-in ile şu klasördeki İKİ dosyayı birlikte seçin:%n%n%1%n%nArdından tüm PDM Explorer pencerelerini kapatıp açın.
en.FinishedAddIn=The application is installed.%n%nThe last step is for the PDM administrator, once: PDM Administration → vault → Add-ins → New Add-in, select BOTH files in this folder together:%n%n%1%n%nThen close and reopen all PDM Explorer windows.

[Files]
; Uygulama — interop bilerek listede YOK, aşağıda PDM istemcisinden alınıyor.
Source: "{#AppBin}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppBin}\PdmVariableStudio.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppBin}\DocumentFormat.OpenXml.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppBin}\DocumentFormat.OpenXml.Framework.dll"; DestDir: "{app}"; Flags: ignoreversion
; Interop: hedef makinedeki PDM istemcisinden. Yolu InitializeSetup doğruluyor.
Source: "{code:PdmInteropPath}"; DestDir: "{app}"; Flags: external ignoreversion
; Eklenti paketi: kullanıcı bu klasördeki iki dosyayı vault'a yükler.
Source: "{#AddInBin}\PdmVariableStudio.AddIn.dll"; DestDir: "{app}\AddIn"; Flags: ignoreversion
Source: "{code:PdmInteropPath}"; DestDir: "{app}\AddIn"; Flags: external ignoreversion
Source: "AddIn-BENIOKU.txt"; DestDir: "{app}\AddIn"; DestName: "BENIOKU.txt"; Languages: tr; Flags: ignoreversion
Source: "AddIn-README.txt"; DestDir: "{app}\AddIn"; DestName: "README.txt"; Languages: en; Flags: ignoreversion
; Belgeler — kılavuz kısayolu PDF'i açar (.md her bilgisayarda okunaklı açılmıyor).
Source: "..\docs\PdmVariableStudio-Kurulum-ve-Kullanim.pdf"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\PdmVariableStudio-Installation-and-User-Guide.pdf"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\KULLANIM.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\USAGE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Registry]
; Eklenti (AddIn/AppLocator.cs) uygulamayı önce buradan arar.
Root: HKLM; Subkey: "SOFTWARE\PdmVariableStudio"; Flags: uninsdeletekeyifempty
Root: HKLM; Subkey: "SOFTWARE\PdmVariableStudio"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletevalue
; Makine varsayılan dili = sihirbazın dili ("tr" / "en"; [Languages] adları bilerek bu kodlar).
; Uygulama (App/LanguagePreference.cs) ve eklenti (AddIn/AddInText.cs) okur; kullanıcının
; uygulamada seçtiği dil (HKCU) bunu geçer.
Root: HKLM; Subkey: "SOFTWARE\PdmVariableStudio"; ValueType: string; ValueName: "Language"; ValueData: "{language}"; Flags: uninsdeletevalue

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
; Kılavuz kısayolu sihirbazın dilindeki PDF'i açar; iki PDF de kurulur (makineyi farklı
; dilde kullanan başka bir kullanıcı için).
Name: "{group}\{cm:Guide}"; Filename: "{app}\PdmVariableStudio-Kurulum-ve-Kullanim.pdf"; Languages: tr
Name: "{group}\{cm:Guide}"; Filename: "{app}\PdmVariableStudio-Installation-and-User-Guide.pdf"; Languages: en
Name: "{group}\{cm:AddInHint}"; Filename: "{app}\AddIn"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"

[UninstallDelete]
; PDM istemcisinden 'external' olarak kopyalanan interop dosyaları: kaldırmada geride kalmasın.
Type: files; Name: "{app}\{#InteropDll}"
Type: files; Name: "{app}\AddIn\{#InteropDll}"

[Run]
Filename: "{win}\explorer.exe"; Parameters: """{app}\AddIn"""; Description: "{cm:AddInHint}"; Flags: postinstall nowait skipifsilent

[Code]
var
  InteropPath: string;
  NetInstallerPath: string;
  DownloadPage: TDownloadWizardPage;

function PdmInteropPath(Param: string): string;
begin
  Result := InteropPath;
end;

{ PDM istemcisi 64-bit ve Program Files altına kurulur; yine de her iki Program Files
  denenir. Kayıt defterinde güvenilir bir kurulum yolu anahtarı bulunamadığı için dosyanın
  kendisine bakılıyor — zaten ihtiyaç duyulan tam olarak o dosya. }
function FindPdmInterop(var Path: string): Boolean;
var
  Candidates: array of string;
  I: Integer;
begin
  SetArrayLength(Candidates, 2);
  Candidates[0] := ExpandConstant('{commonpf64}\SOLIDWORKS PDM\{#InteropDll}');
  Candidates[1] := ExpandConstant('{commonpf32}\SOLIDWORKS PDM\{#InteropDll}');

  for I := 0 to GetArrayLength(Candidates) - 1 do
  begin
    if FileExists(Candidates[I]) then
    begin
      Path := Candidates[I];
      Result := True;
      Exit;
    end;
  end;

  Path := Candidates[0];
  Result := False;
end;

function IsNet481Installed: Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release)
    and (Release >= {#NetRelease});
end;

function InitializeSetup: Boolean;
begin
  Result := FindPdmInterop(InteropPath);
  if not Result then
    MsgBox(FmtMessage(CustomMessage('PdmMissing'), [InteropPath]), mbError, MB_OK);
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

{ .NET 4.8.1 eksikse dosya kopyalamadan ÖNCE indir + kur. Başarısızlık kurulumu durdurur:
  uygulama .NET olmadan açılışta hata verir ve bunu kullanıcı bizden daha zor teşhis eder. }
function PrepareToInstall(var NeedsRestart: Boolean): string;
var
  ResultCode: Integer;
begin
  Result := '';
  if IsNet481Installed then
    Exit;

  NetInstallerPath := ExpandConstant('{tmp}\NDP481-Web.exe');

  DownloadPage.Clear;
  DownloadPage.Add('{#NetWebInstallerUrl}', 'NDP481-Web.exe', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.SetText(CustomMessage('NetDownloading'), '');
      DownloadPage.Download;
    except
      Result := FmtMessage(CustomMessage('NetDownloadFailed'), ['{#NetWebInstallerUrl}']);
      Exit;
    end;

    DownloadPage.SetText(CustomMessage('NetInstalling'), '');
    DownloadPage.SetProgress(0, 0);

    if not Exec(NetInstallerPath, '/passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
      ResultCode := -1;

    { 0 = tamam, 1641 / 3010 = tamam ama yeniden başlatma gerek. }
    if (ResultCode = 1641) or (ResultCode = 3010) then
      NeedsRestart := True
    else if ResultCode <> 0 then
      Result := FmtMessage(CustomMessage('NetFailed'), [IntToStr(ResultCode), '{#NetWebInstallerUrl}']);
  finally
    DownloadPage.Hide;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not WizardSilent then
    MsgBox(FmtMessage(CustomMessage('FinishedAddIn'), [ExpandConstant('{app}\AddIn')]), mbInformation, MB_OK);
end;
