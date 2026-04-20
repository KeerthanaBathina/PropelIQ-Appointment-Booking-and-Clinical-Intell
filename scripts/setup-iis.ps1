<#
.SYNOPSIS
    Configures IIS 10 on Windows Server 2022 to host the UPACIP React SPA.

.DESCRIPTION
    1. Enables required IIS Windows Features (Web-Server, Static Content, HTTP Redirect).
    2. Installs the IIS URL Rewrite Module 2.1 if not already present.
    3. Creates a dedicated "No Managed Code" application pool.
    4. Creates the "upacip-frontend" IIS site with HTTPS binding on port 443.
    5. Adds an HTTP binding on port 80 with a global 301 redirect to HTTPS.
    6. Disables TLS 1.0 and TLS 1.1 via SCHANNEL registry to enforce TLS 1.2+ (AC-3).
    7. Sets the SitePath directory and grants IIS_IUSRS read access.

.PARAMETER SitePath
    Physical path for the IIS site (frontend static files). Default: C:\inetpub\wwwroot\upacip

.PARAMETER SiteName
    IIS website name. Default: upacip-frontend

.PARAMETER AppPoolName
    IIS application pool name. Default: upacip-frontend-pool

.PARAMETER Domain
    Hostname for the HTTPS binding. Leave empty ("") to bind to all hostnames (*).
    Default: "" (all hostnames)

.EXAMPLE
    # Run as Administrator from the repository root
    .\scripts\setup-iis.ps1
    .\scripts\setup-iis.ps1 -Domain "upacip.example.com"
#>
[CmdletBinding()]
param(
    [string] $SitePath    = 'C:\inetpub\wwwroot\upacip',
    [string] $SiteName    = 'upacip-frontend',
    [string] $AppPoolName = 'upacip-frontend-pool',
    [string] $Domain      = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------- Require Administrator ----------
$currentPrincipal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'This script must be run as Administrator.'
}

# ============================================================
# Step 1 — Enable IIS Windows Features
# ============================================================
Write-Host '[1/7] Enabling IIS Windows Features...' -ForegroundColor Cyan

$features = @(
    'Web-Server',           # IIS core
    'Web-Static-Content',   # Serve static files (HTML, JS, CSS, images)
    'Web-Default-Doc',      # Default document (index.html)
    'Web-Http-Redirect',    # HTTP -> HTTPS global redirect
    'Web-Http-Errors',      # Custom error pages
    'Web-Http-Logging',     # Access logs
    'Web-Mgmt-Console'      # IIS Manager
)

foreach ($feature in $features) {
    $state = (Get-WindowsFeature -Name $feature).InstallState
    if ($state -ne 'Installed') {
        Write-Host "  Installing $feature..."
        Install-WindowsFeature -Name $feature -IncludeManagementTools | Out-Null
    } else {
        Write-Host "  $feature already installed."
    }
}

# ============================================================
# Step 2 — Install IIS URL Rewrite Module 2.1
# ============================================================
Write-Host '[2/7] Checking IIS URL Rewrite Module...' -ForegroundColor Cyan

Import-Module WebAdministration -ErrorAction SilentlyContinue
$rewriteModule = Get-WebGlobalModule -Name 'RewriteModule' -ErrorAction SilentlyContinue
if (-not $rewriteModule) {
    Write-Host '  Downloading URL Rewrite Module 2.1...'
    $rewriteMsi = "$env:TEMP\rewrite_amd64_en-US.msi"
    # Official Microsoft Web Platform Installer URL for URL Rewrite 2.1
    $rewriteUrl = 'https://download.microsoft.com/download/1/2/8/128E2E22-C1B9-44A4-BE2A-5859ED1D4592/rewrite_amd64_en-US.msi'
    Invoke-WebRequest -Uri $rewriteUrl -OutFile $rewriteMsi -UseBasicParsing
    Start-Process -FilePath 'msiexec.exe' -ArgumentList "/quiet /i `"$rewriteMsi`"" -Wait
    Remove-Item $rewriteMsi -Force -ErrorAction SilentlyContinue
    Write-Host '  URL Rewrite Module installed.'
} else {
    Write-Host '  URL Rewrite Module already installed.'
}

# ============================================================
# Step 3 — Create Application Pool (No Managed Code)
# ============================================================
Write-Host '[3/7] Configuring Application Pool...' -ForegroundColor Cyan

Import-Module WebAdministration

if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
    New-WebAppPool -Name $AppPoolName | Out-Null
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name 'managedRuntimeVersion' -Value ''  # No Managed Code
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name 'processModel.idleTimeout' -Value ([TimeSpan]::Zero)
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name 'startMode' -Value 'AlwaysRunning'
    Write-Host "  App pool '$AppPoolName' created (No Managed Code, AlwaysRunning)."
} else {
    Write-Host "  App pool '$AppPoolName' already exists."
}

# ============================================================
# Step 4 — Create IIS Site and HTTPS Binding
# ============================================================
Write-Host '[4/7] Creating IIS site...' -ForegroundColor Cyan

# Ensure the physical path exists and IIS_IUSRS can read it
if (-not (Test-Path $SitePath)) {
    New-Item -ItemType Directory -Path $SitePath -Force | Out-Null
    Write-Host "  Created $SitePath"
}
$acl = Get-Acl $SitePath
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    'IIS_IUSRS', 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
$acl.SetAccessRule($rule)
Set-Acl -Path $SitePath -AclObject $acl

$existingSite = Get-Website -Name $SiteName -ErrorAction SilentlyContinue

if (-not $existingSite) {
    # HTTPS binding — certificate will be bound by setup-certificates.ps1
    $hostHeader = if ($Domain) { $Domain } else { '' }
    New-Website `
        -Name          $SiteName `
        -PhysicalPath  $SitePath `
        -ApplicationPool $AppPoolName `
        -Port          443 `
        -HostHeader    $hostHeader `
        -Ssl | Out-Null

    Write-Host "  Site '$SiteName' created on port 443."
} else {
    Write-Host "  Site '$SiteName' already exists."
}

# ============================================================
# Step 5 — Add HTTP Binding with 301 Redirect to HTTPS
# ============================================================
Write-Host '[5/7] Configuring HTTP -> HTTPS redirect...' -ForegroundColor Cyan

$httpBinding = Get-WebBinding -Name $SiteName -Protocol 'http' -ErrorAction SilentlyContinue
if (-not $httpBinding) {
    $hostHeader = if ($Domain) { $Domain } else { '' }
    New-WebBinding -Name $SiteName -Protocol 'http' -Port 80 -HostHeader $hostHeader | Out-Null
    Write-Host '  HTTP binding on port 80 added.'
}

# Add URL Rewrite rule for HTTP -> HTTPS redirect (301 Permanent)
# This rule lives in the site's web.config at the root.
$redirectRuleXml = @'
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <rule name="HTTP to HTTPS Redirect" enabled="true" stopProcessing="true">
          <match url="(.*)" />
          <conditions>
            <add input="{HTTPS}" pattern="^OFF$" />
          </conditions>
          <action type="Redirect" url="https://{HTTP_HOST}/{R:1}"
                  redirectType="Permanent" />
        </rule>
      </rules>
    </rewrite>
  </system.webServer>
</configuration>
'@

# Only write the redirect web.config if the site is serving a static root
# The main SPA web.config (with URL Rewrite for SPA routing) is deployed by deploy-frontend.ps1
$redirectConfigPath = Join-Path $SitePath 'web.config'
if (-not (Test-Path $redirectConfigPath)) {
    $redirectRuleXml | Set-Content -Path $redirectConfigPath -Encoding UTF8
    Write-Host '  HTTP-to-HTTPS redirect rule written to web.config.'
} else {
    Write-Host '  web.config already exists - skipping HTTP redirect rule (deploy-frontend.ps1 manages it).'
}

# ============================================================
# Step 6 — Disable TLS 1.0 and TLS 1.1 (AC-3: TLS 1.2+ only)
# ============================================================
Write-Host '[6/7] Disabling TLS 1.0 and TLS 1.1 via SCHANNEL registry...' -ForegroundColor Cyan

$protocolBase = 'HKLM:\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols'

$disabledProtocols = @('TLS 1.0', 'TLS 1.1')
foreach ($proto in $disabledProtocols) {
    foreach ($subKey in @('Server', 'Client')) {
        $regPath = "$protocolBase\$proto\$subKey"
        if (-not (Test-Path $regPath)) {
            New-Item -Path $regPath -Force | Out-Null
        }
        Set-ItemProperty -Path $regPath -Name 'Enabled'            -Value 0 -Type DWord
        Set-ItemProperty -Path $regPath -Name 'DisabledByDefault'  -Value 1 -Type DWord
    }
    Write-Host "  $proto disabled."
}

# Ensure TLS 1.2 is explicitly enabled (default on Server 2022, but set for certainty)
foreach ($subKey in @('Server', 'Client')) {
    $regPath = "$protocolBase\TLS 1.2\$subKey"
    if (-not (Test-Path $regPath)) {
        New-Item -Path $regPath -Force | Out-Null
    }
    Set-ItemProperty -Path $regPath -Name 'Enabled'           -Value 1 -Type DWord
    Set-ItemProperty -Path $regPath -Name 'DisabledByDefault' -Value 0 -Type DWord
}
Write-Host '  TLS 1.2 explicitly enabled.'

# ============================================================
# Step 7 — Start the IIS site
# ============================================================
Write-Host '[7/7] Starting IIS site...' -ForegroundColor Cyan

Start-Website -Name $SiteName
$site = Get-Website -Name $SiteName
Write-Host "  Site '$SiteName' state: $($site.State)" -ForegroundColor Green

Write-Host ''
Write-Host 'IIS setup complete.' -ForegroundColor Green
Write-Host "  Frontend site : https://$( if ($Domain) { $Domain } else { 'localhost' } )/"
Write-Host "  Physical path : $SitePath"
Write-Host ''
Write-Host 'Next step: run scripts\setup-certificates.ps1 to bind a TLS certificate.'
Write-Host 'NOTE: A reboot or iisreset may be required for TLS registry changes to take effect.'
