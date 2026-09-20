# Yayım paketini (zip) üretir: GitHub Releases'a yüklenecek dosya.
#
# Paket düzeni:
#   PdmVariableStudio-<sürüm>\
#     App\                    uygulama (exe + Core + OpenXml)  — interop YOK
#     AddIn\                  PdmVariableStudio.AddIn.dll       — interop YOK
#     install-app.ps1         uygulamayı kurar, interop'u PDM istemcisinden alır
#     KULLANIM.md             son kullanıcı kılavuzu
#     CHANGELOG.md, LICENSE
#     BENIOKU.txt             üç adımlık özet
#   PdmVariableStudio-<sürüm>.zip
#   PdmVariableStudio-<sürüm>.zip.sha256
#
# EPDM.Interop.epdm.dll BİLEREK pakete girmez: Dassault Systèmes'in dosyası, her PDM
# istemcisinde zaten var (C:\Program Files\SOLIDWORKS PDM\). install-app.ps1 oradan
# kopyalar; eklenti için kullanıcı aynı klasörden alıp vault'a yükler.
#
# Kullanım:
#   powershell -ExecutionPolicy Bypass -File docs\package-release.ps1
#   powershell -ExecutionPolicy Bypass -File docs\package-release.ps1 -Output C:\Temp

param(
    [string]$Output = '',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$appBin = Join-Path $root 'src\PdmVariableStudio.App\bin\Release\net481'
$addInBin = Join-Path $root 'src\PdmVariableStudio.AddIn\bin\Release\net481'

# Sürüm tek kaynaktan: ProductInfo.Version.
$writerSource = Get-Content (Join-Path $root 'src\PdmVariableStudio.Core\Workbook\WorkbookWriter.cs') -Raw
if ($writerSource -notmatch 'public const string Version = "([^"]+)"') {
    Write-Host 'ProductInfo.Version bulunamadı.' -ForegroundColor Red
    exit 1
}
$version = $Matches[1]

if (-not $SkipBuild) {
    Write-Host "Release derleniyor..."
    & dotnet build (Join-Path $root 'PdmVariableStudio.sln') -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Derleme başarısız.' -ForegroundColor Red
        exit 1
    }

    Write-Host "Testler koşuyor..."
    & dotnet test (Join-Path $root 'tests\PdmVariableStudio.Tests') -c Debug --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Testler başarısız; paket üretilmedi.' -ForegroundColor Red
        exit 1
    }
}

if ($Output.Length -eq 0) {
    $Output = Join-Path $root 'artifacts'
}

$packageName = "PdmVariableStudio-$version"
$stage = Join-Path $Output $packageName
$zip = Join-Path $Output "$packageName.zip"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'App') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'AddIn') | Out-Null

$appFiles = @(
    'PdmVariableStudio.exe',
    'PdmVariableStudio.Core.dll',
    'DocumentFormat.OpenXml.dll',
    'DocumentFormat.OpenXml.Framework.dll'
)

foreach ($name in $appFiles) {
    $path = Join-Path $appBin $name
    if (-not (Test-Path $path)) {
        Write-Host "Eksik: $path" -ForegroundColor Red
        exit 1
    }
    Copy-Item $path (Join-Path $stage 'App')
}

$addInPath = Join-Path $addInBin 'PdmVariableStudio.AddIn.dll'
if (-not (Test-Path $addInPath)) {
    Write-Host "Eksik: $addInPath" -ForegroundColor Red
    exit 1
}
Copy-Item $addInPath (Join-Path $stage 'AddIn')

Copy-Item (Join-Path $PSScriptRoot 'install-app.ps1') $stage
Copy-Item (Join-Path $PSScriptRoot 'KULLANIM.md') $stage
Copy-Item (Join-Path $root 'CHANGELOG.md') $stage
Copy-Item (Join-Path $root 'LICENSE') $stage

$readme = @"
PDM Variable Studio $version
============================

Uc adim:

1. Uygulama: yonetici PowerShell'de bu klasorde
       powershell -ExecutionPolicy Bypass -File install-app.ps1

2. Eklenti: PDM Administration -> vault -> Add-ins -> sag tik -> New Add-in
   Su IKI dosyayi birlikte secin:
       AddIn\PdmVariableStudio.AddIn.dll
       C:\Program Files\SOLIDWORKS PDM\EPDM.Interop.epdm.dll
   (ikincisi pakette YOK; PDM istemcinizden gelir)

3. Tum PDM Explorer pencerelerini kapatip acin.

Ayrinti ve sorun giderme: KULLANIM.md
Gereksinim: SOLIDWORKS PDM Professional 2022 (30.0) veya ustu, .NET Framework 4.8.1
"@
Set-Content -Path (Join-Path $stage 'BENIOKU.txt') -Value $readme -Encoding UTF8

Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $packageName.zip" -Encoding ASCII

Write-Host ''
Write-Host "Paket: $zip" -ForegroundColor Green
Write-Host "SHA-256: $hash"
Write-Host ("Boyut: {0:N0} KB" -f ((Get-Item $zip).Length / 1KB))
