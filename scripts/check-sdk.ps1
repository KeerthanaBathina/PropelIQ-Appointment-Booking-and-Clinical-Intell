<#
.SYNOPSIS
    Validates that the .NET 8 SDK is installed on the current machine.

.DESCRIPTION
    Checks for the presence of the .NET 8 SDK. If not found, outputs installation
    guidance with the official download URL. Exits with code 0 on success, 1 on failure.

.EXAMPLE
    .\check-sdk.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$requiredMajor = 8
$dotnetExe = Get-Command dotnet -ErrorAction SilentlyContinue

if (-not $dotnetExe) {
    $dotnetExe = Get-Command "C:\Program Files\dotnet\dotnet.exe" -ErrorAction SilentlyContinue
}

if (-not $dotnetExe) {
    Write-Error "[FAIL] The dotnet CLI was not found in PATH."
    Write-Host ""
    Write-Host "Install the .NET $requiredMajor SDK from:"
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/$requiredMajor.0"
    exit 1
}

$sdks = & $dotnetExe.Source --list-sdks 2>&1
$net8Sdks = $sdks | Where-Object { $_ -match "^$requiredMajor\." }

if (-not $net8Sdks) {
    Write-Error "[FAIL] .NET $requiredMajor SDK is not installed."
    Write-Host ""
    Write-Host "Installed SDKs detected:"
    $sdks | ForEach-Object { Write-Host "  $_" }
    Write-Host ""
    Write-Host "Install the .NET $requiredMajor SDK from:"
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/$requiredMajor.0"
    exit 1
}

Write-Host "[PASS] .NET $requiredMajor SDK detected:"
$net8Sdks | ForEach-Object { Write-Host "  $_" }
exit 0
