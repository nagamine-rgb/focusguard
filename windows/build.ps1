$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $scriptDir "src"
$outExe = Join-Path $scriptDir "FocusGuard.exe"

$fwDir = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$csc = Join-Path $fwDir "csc.exe"

if (-not (Test-Path $csc)) {
    throw "csc.exe not found at $csc"
}

$sources = Get-ChildItem -Path $srcDir -Filter "*.cs" | ForEach-Object { $_.FullName }

Write-Host "Building FocusGuard.exe..." -ForegroundColor Cyan

& $csc `
    /nologo `
    /target:winexe `
    /optimize+ `
    /utf8output `
    /codepage:65001 `
    /out:$outExe `
    /r:"$fwDir\System.dll" `
    /r:"$fwDir\System.Core.dll" `
    /r:"$fwDir\System.Drawing.dll" `
    /r:"$fwDir\System.Windows.Forms.dll" `
    /r:"$fwDir\System.Net.Http.dll" `
    /r:"$fwDir\System.Web.Extensions.dll" `
    $sources

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

Write-Host "Build succeeded: $outExe" -ForegroundColor Green
