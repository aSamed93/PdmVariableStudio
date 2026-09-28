# Inno Setup ile kurulum dosyasını üretir: artifacts\PdmVariableStudio-Setup-<sürüm>.exe
#
# Gereksinim: Inno Setup 6 (https://jrsoftware.org/isinfo.php). ISCC.exe olağan
# konumlarda aranır; başka yerdeyse -Iscc ile verin.
#
# Kullanım:
#   powershell -ExecutionPolicy Bypass -File docs\build-installer.ps1
#   powershell -ExecutionPolicy Bypass -File docs\build-installer.ps1 -SkipBuild
#
# Sürüm tek kaynaktan (ProductInfo.Version) okunur ve /DAppVersion ile betiğe geçirilir.
# EPDM.Interop.epdm.dll kuruluma GİRMEZ; kurulum onu hedef makinedeki PDM istemcisinden alır.

param(
    [string]$Iscc = '',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$script = Join-Path $root 'installer\PdmVariableStudio.iss'
$output = Join-Path $root 'artifacts'

if ($Iscc.Length -eq 0) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    $Iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $Iscc) {
        $cmd = Get-Command iscc -ErrorAction SilentlyContinue
        if ($cmd) { $Iscc = $cmd.Source }
    }
}

if (-not $Iscc -or -not (Test-Path $Iscc)) {
    Write-Host 'Inno Setup 6 (ISCC.exe) bulunamadı.' -ForegroundColor Red
    Write-Host 'Kurmak için: winget install JRSoftware.InnoSetup   ya da   https://jrsoftware.org/isdl.php'
    exit 1
}

$writerSource = Get-Content (Join-Path $root 'src\PdmVariableStudio.Core\Workbook\WorkbookWriter.cs') -Raw
if ($writerSource -notmatch 'public const string Version = "([^"]+)"') {
    Write-Host 'ProductInfo.Version bulunamadı.' -ForegroundColor Red
    exit 1
}
$version = $Matches[1]

if (-not $SkipBuild) {
    Write-Host 'Release derleniyor...'
    & dotnet build (Join-Path $root 'PdmVariableStudio.sln') -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Derleme başarısız.' -ForegroundColor Red
        exit 1
    }

    Write-Host 'Testler koşuyor...'
    & dotnet test (Join-Path $root 'tests\PdmVariableStudio.Tests') -c Debug --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Testler başarısız; kurulum üretilmedi.' -ForegroundColor Red
        exit 1
    }
}

New-Item -ItemType Directory -Force -Path $output | Out-Null

Write-Host "Kurulum derleniyor (sürüm $version)..."
& $Iscc /Q "/DAppVersion=$version" "/O$output" $script
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Inno Setup derlemesi başarısız.' -ForegroundColor Red
    exit 1
}

$setup = Join-Path $output "PdmVariableStudio-Setup-$version.exe"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$setup.sha256" -Value "$hash  $(Split-Path -Leaf $setup)" -Encoding ASCII

Write-Host ''
Write-Host "Kurulum: $setup" -ForegroundColor Green
Write-Host "SHA-256: $hash"
Write-Host ("Boyut: {0:N0} KB" -f ((Get-Item $setup).Length / 1KB))
