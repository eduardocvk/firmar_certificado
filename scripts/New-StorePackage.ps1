param(
    [Parameter(Mandatory)][string]$IdentityName,
    [Parameter(Mandatory)][string]$Publisher,
    [Parameter(Mandatory)][string]$PublisherDisplayName,
    [string]$Version = '0.3.0.0',
    [string]$PublishDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $PublishDirectory) { $PublishDirectory = Join-Path $repoRoot 'artifacts/windows/FirmaVisible' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts/store' }
if ($IdentityName -notmatch '^[a-zA-Z0-9.-]{3,50}$') { throw 'Copia Package/Identity/Name de Partner Center.' }
if ($Publisher -notmatch '^CN=') { throw 'Copia Package/Identity/Publisher completo de Partner Center.' }
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'La versión MSIX debe tener cuatro números.' }
if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory 'FirmaVisible.exe'))) { throw 'Compila primero la aplicación con Build-Windows.ps1.' }
$tool = Get-Command makeappx.exe -ErrorAction SilentlyContinue
if ($tool) { $toolPath = $tool.Source }
else {
    $sdk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $toolPath = Get-ChildItem -LiteralPath $sdk -Directory | Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'x64/makeappx.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $toolPath) { throw 'Instala las herramientas MSIX del Windows SDK.' }
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$staging = Join-Path $OutputDirectory ('staging-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
Copy-Item -LiteralPath $PublishDirectory -Destination (Join-Path $staging 'App') -Recurse
Copy-Item -LiteralPath (Join-Path $repoRoot 'windows/Store/Assets') -Destination (Join-Path $staging 'Assets') -Recurse
[xml]$manifest = Get-Content -LiteralPath (Join-Path $repoRoot 'windows/Store/AppxManifest.xml') -Raw
$manifest.Package.Identity.Name = $IdentityName
$manifest.Package.Identity.Publisher = $Publisher
$manifest.Package.Identity.Version = $Version
$manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
$manifest.Save((Join-Path $staging 'AppxManifest.xml'))
$output = Join-Path $OutputDirectory "FirmaVisible-$Version.msix"
& $toolPath pack /d $staging /p $output /o
if ($LASTEXITCODE -ne 0) { throw 'No se pudo empaquetar o validar el MSIX.' }
Write-Output "MSIX para enviar a Microsoft Store: $output"
Write-Output 'Este MSIX está sin firmar: se envía a Partner Center; no es una entrega lista para instalar directamente.'
