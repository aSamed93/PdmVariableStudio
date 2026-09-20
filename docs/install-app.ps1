# PDM Variable Studio uygulamasını istemciye kurar.
#
# Eklenti (vault'a yüklenen 2 DLL) yalnızca bu uygulamayı BAŞLATIR. Uygulamanın kendisi
# vault'ta değil, diskte durur — bu sayede güncelleme için Explorer kapatmak gerekmez.
#
# Betik iki şey yapar:
#   1. Uygulama dosyalarını hedef klasöre kopyalar
#   2. HKLM\SOFTWARE\PdmVariableStudio\InstallPath değerini yazar (eklenti buradan bulur)
#
# Kullanım (yönetici PowerShell):
#   powershell -ExecutionPolicy Bypass -File install-app.ps1
#   powershell -ExecutionPolicy Bypass -File install-app.ps1 -Destination "\\sunucu\pdm\VariableStudio"
#   powershell -ExecutionPolicy Bypass -File install-app.ps1 -RegisterOnly -Destination "\\sunucu\pdm\VariableStudio"
#
# Kaynak klasör kendiliğinden bulunur:
#   - yayım paketinde (zip): betiğin yanındaki App\ klasörü
#   - depoda: src\PdmVariableStudio.App\bin\<Configuration>\net481
#   - ya da -Source ile açıkça verilir
#
# EPDM.Interop.epdm.dll yayım paketinde YOKTUR (Dassault Systèmes'in dosyası; yeniden
# dağıtılmaz). Kaynakta yoksa bu makinedeki PDM istemci kurulumundan kopyalanır. PDM
# istemcisi olmayan bir makinede uygulama zaten çalışamaz.
#
# Ağ paylaşımına kurulum: bir kez -Destination ile kopyalayın, sonra her istemcide
# -RegisterOnly ile yalnızca kayıt defteri değerini yazın.

param(
    [string]$Destination = (Join-Path ${env:ProgramFiles} 'PDM Variable Studio'),

    # Kopyalanacak dosyaların kaynağı. Boşsa yukarıdaki sırayla bulunur.
    [string]$Source = '',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    # Yalnızca kayıt defteri değerini yaz; dosya kopyalama.
    [switch]$RegisterOnly,

    # Kayıt defterine HKCU'ya yaz (yönetici hakkı olmayan kullanıcı kurulumu).
    [switch]$CurrentUser
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$executableName = 'PdmVariableStudio.exe'
$interopName = 'EPDM.Interop.epdm.dll'
$pdmClientDirectory = Join-Path ${env:ProgramFiles} 'SOLIDWORKS PDM'

if ($Source.Length -gt 0) {
    $source = $Source
}
elseif (Test-Path (Join-Path $PSScriptRoot 'App')) {
    # Yayım paketi: install-app.ps1 zip kökünde, uygulama App\ altında.
    $source = Join-Path $PSScriptRoot 'App'
}
else {
    $source = Join-Path $root "src\PdmVariableStudio.App\bin\$Configuration\net481"
}

if (-not $RegisterOnly) {
    if (-not (Test-Path $source)) {
        Write-Host "Kaynak klasör yok: $source" -ForegroundColor Red
        Write-Host "Depodan kuruyorsanız önce: dotnet build PdmVariableStudio.sln -c $Configuration"
        exit 1
    }

    if (-not (Test-Path (Join-Path $source $executableName))) {
        Write-Host "$executableName bulunamadı: $source" -ForegroundColor Red
        exit 1
    }

    # Program Files ve benzeri korumalı konumlar yönetici hakkı ister. Kopyalama yarıda
    # kalırsa klasörde YALNIZCA .exe kalabiliyor ve o durumda uygulama, eksik derlemeyi
    # ararken hiçbir şey loglayamadan ölüyor. Bu yüzden baştan kontrol ediliyor.
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $isElevated = ([Security.Principal.WindowsPrincipal]$identity).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)

    $needsElevation = $Destination.StartsWith($env:ProgramFiles, 'OrdinalIgnoreCase') -or
                      $Destination.StartsWith(${env:ProgramFiles(x86)}, 'OrdinalIgnoreCase')

    if ($needsElevation -and -not $isElevated) {
        Write-Host ''
        Write-Host 'Bu konuma yazmak için yönetici hakkı gerekiyor:' -ForegroundColor Red
        Write-Host "  $Destination" -ForegroundColor Red
        Write-Host ''
        Write-Host 'Seçenekler:' -ForegroundColor Yellow
        Write-Host '  1. PowerShell''i YÖNETİCİ olarak açıp bu betiği yeniden çalıştırın' -ForegroundColor Yellow
        Write-Host '  2. ya da yönetici hakkı gerektirmeyen bir konuma kurun:' -ForegroundColor Yellow
        Write-Host ("     powershell -ExecutionPolicy Bypass -File install-app.ps1 -CurrentUser -Destination `"{0}\PDM Variable Studio`"" -f $env:LOCALAPPDATA) -ForegroundColor Yellow
        Write-Host ''
        Write-Host 'UYARI: .exe dosyasını tek başına elle kopyalamayın. Uygulama yanındaki' -ForegroundColor Yellow
        Write-Host 'DLL''lere ihtiyaç duyar ve eksik kurulumda sessizce kapanır.' -ForegroundColor Yellow
        exit 1
    }

    if (-not (Test-Path $Destination)) {
        New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    }

    # Çalışan bir örnek varsa kopyalama kilitlenir; önce uyaralım.
    $running = Get-Process -Name 'PdmVariableStudio' -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host 'PDM Variable Studio çalışıyor. Kapatıp tekrar deneyin.' -ForegroundColor Red
        exit 1
    }

    Write-Host "Kopyalanıyor: $source"
    Write-Host "         ->  $Destination"
    Copy-Item -Path (Join-Path $source '*') -Destination $Destination -Recurse -Force

    # Interop yayım paketinde yok; PDM istemcisinden alınır. Bu dosya olmadan uygulama
    # açılışta tip çözümlemesinde düşer ve günlüğe bile yazamaz.
    if (-not (Test-Path (Join-Path $Destination $interopName))) {
        $interopSource = Join-Path $pdmClientDirectory $interopName
        if (-not (Test-Path $interopSource)) {
            Write-Host ''
            Write-Host "$interopName bulunamadı: $interopSource" -ForegroundColor Red
            Write-Host 'Bu makinede SOLIDWORKS PDM istemcisi kurulu görünmüyor; uygulama onsuz çalışamaz.' -ForegroundColor Red
            exit 1
        }

        Copy-Item -Path $interopSource -Destination $Destination -Force
        Write-Host "  $interopName PDM istemcisinden kopyalandı." -ForegroundColor Green
    }

    $copied = Get-ChildItem $Destination -File | Measure-Object
    Write-Host ("  {0} dosya kopyalandı." -f $copied.Count) -ForegroundColor Green
}

# --- kayıt defteri ---
# Eklenti bu değeri okuyup uygulamayı bulur. Yazılamazsa eklenti Program Files altına
# bakmaya devam eder; oraya kurulduysa kayıt defteri zaten gerekmez.
$hive = if ($CurrentUser) { 'HKCU:' } else { 'HKLM:' }
$keyPath = "$hive\SOFTWARE\PdmVariableStudio"

try {
    if (-not (Test-Path $keyPath)) {
        New-Item -Path $keyPath -Force | Out-Null
    }

    Set-ItemProperty -Path $keyPath -Name 'InstallPath' -Value $Destination -Type String
    Write-Host ''
    Write-Host "Kayıt defteri: $keyPath\InstallPath = $Destination" -ForegroundColor Green
}
catch [System.UnauthorizedAccessException] {
    Write-Host ''
    Write-Host 'Kayıt defterine yazılamadı (yönetici hakkı gerekiyor).' -ForegroundColor Yellow
    Write-Host 'Seçenekler:' -ForegroundColor Yellow
    Write-Host '  - PowerShell''i yönetici olarak açıp tekrar çalıştırın' -ForegroundColor Yellow
    Write-Host '  - ya da -CurrentUser ile kullanıcı kapsamında kaydedin' -ForegroundColor Yellow
    Write-Host "  - ya da uygulamayı $env:ProgramFiles\PDM Variable Studio altına kurun (kayıt gerekmez)" -ForegroundColor Yellow
    exit 1
}

Write-Host ''
Write-Host 'Kurulum tamam.' -ForegroundColor Green
Write-Host ''
Write-Host 'Sıradaki adım: eklentinin iki DLL''ini vault''a yükleyin (henüz yapılmadıysa):'
Write-Host '  PdmVariableStudio.AddIn.dll  +  EPDM.Interop.epdm.dll'
Write-Host "  (interop: $pdmClientDirectory)"
Write-Host 'Ayrıntı: KULLANIM.md -> Kurulum.'
Write-Host ''
Write-Host 'Uygulamayı eklentisiz denemek için doğrudan çalıştırabilirsiniz:'
Write-Host ("  {0}" -f (Join-Path $Destination $executableName))
