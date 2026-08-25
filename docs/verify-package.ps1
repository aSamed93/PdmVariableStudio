# Eklenti paketinin ve uygulama çıktısının eksiksiz olduğunu denetler.
#
# PDM eklentisi Administration aracına DÜZ bir DLL listesi olarak yüklenir. Eksik bırakılan
# bir DLL, eklenti yüklenirken tip çözümlemesini düşürür ve PDM bunu yanıltıcı bir
# "not a multi-threaded COM-server" iletisiyle gösterir — hata mesajı eksik dosyayı SÖYLEMEZ.
#
# Kullanım:
#   powershell -File docs/verify-package.ps1
#   powershell -File docs/verify-package.ps1 -Configuration Debug

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# --- vault'a yüklenen paket: yalnızca iki dosya ---
# Bu liste bilinçli olarak kısa. Yeni bir DLL eklemek istiyorsanız önce durun: eklentiye
# eklenen her bağımlılık vault'a yüklenmesi ve her sürümde yeniden yüklenmesi gereken bir
# dosya daha demek. Asıl uygulama zaten ayrı süreçte ve orada böyle bir kısıt yok.
$addInRequired = @(
    'PdmVariableStudio.AddIn.dll',
    'EPDM.Interop.epdm.dll'
)

# --- diske kurulan uygulama ---
$appRequired = @(
    'PdmVariableStudio.exe',
    'PdmVariableStudio.Core.dll',
    'EPDM.Interop.epdm.dll',
    'DocumentFormat.OpenXml.dll',
    'DocumentFormat.OpenXml.Framework.dll'
)

function Test-Output {
    param(
        [string]$Title,
        [string]$Directory,
        [string[]]$Required,
        [switch]$WarnOnExtra
    )

    Write-Host ''
    Write-Host "=== $Title ===" -ForegroundColor Cyan
    Write-Host $Directory -ForegroundColor DarkGray

    if (-not (Test-Path $Directory)) {
        Write-Host '  Derleme çıktısı yok.' -ForegroundColor Red
        return $false
    }

    $missing = @()
    $total = 0

    foreach ($name in $Required) {
        $path = Join-Path $Directory $name
        if (Test-Path $path) {
            $bytes = (Get-Item $path).Length
            $total += $bytes
            Write-Host ("  [VAR]  {0,-38} {1,8:N0} KB" -f $name, ($bytes / 1KB)) -ForegroundColor Green
        }
        else {
            Write-Host ("  [YOK]  {0}" -f $name) -ForegroundColor Red
            $missing += $name
        }
    }

    Write-Host ("         {0,-38} {1,8:N0} KB toplam" -f '', ($total / 1KB)) -ForegroundColor DarkGray

    if ($WarnOnExtra) {
        $actual = Get-ChildItem $Directory -File |
            Where-Object { $_.Extension -in '.dll', '.exe' } |
            ForEach-Object { $_.Name }

        $unexpected = $actual | Where-Object { $Required -notcontains $_ }

        if ($unexpected.Count -gt 0) {
            Write-Host ''
            Write-Host '  UYARI: listede olmayan dosya bulundu. Eklentiye yeni bağımlılık mı girdi?' -ForegroundColor Yellow
            Write-Host '  Eklenti paketi iki dosyada kalmalı; yeni kod App projesine ait.' -ForegroundColor Yellow
            $unexpected | ForEach-Object { Write-Host ("    [FAZLA] {0}" -f $_) -ForegroundColor Yellow }
        }
    }

    if ($missing.Count -gt 0) {
        Write-Host ("  EKSİK: {0} dosya." -f $missing.Count) -ForegroundColor Red
        return $false
    }

    return $true
}

$addInOk = Test-Output `
    -Title "EKLENTI PAKETI (vault'a yüklenir)" `
    -Directory (Join-Path $root "src\PdmVariableStudio.AddIn\bin\$Configuration\net481") `
    -Required $addInRequired `
    -WarnOnExtra

$appOk = Test-Output `
    -Title 'UYGULAMA (diske kurulur)' `
    -Directory (Join-Path $root "src\PdmVariableStudio.App\bin\$Configuration\net481") `
    -Required $appRequired

Write-Host ''

if (-not $addInOk -or -not $appOk) {
    Write-Host 'Eksik dosya var. Vault''a YÜKLEMEYİN.' -ForegroundColor Red
    Write-Host "Önce: dotnet build PdmVariableStudio.sln -c $Configuration"
    exit 1
}

Write-Host 'Her iki çıktı da eksiksiz.' -ForegroundColor Green
Write-Host ''
Write-Host 'Sıradaki adımlar:'
Write-Host '  1. Uygulamayı kurun:  powershell -File docs\install-app.ps1'
Write-Host '  2. Eklentinin İKİ DLL''ini Administration > Add-ins ile vault''a yükleyin'
Write-Host '  3. Tüm PDM Explorer ve Administration pencerelerini kapatıp açın'
exit 0
