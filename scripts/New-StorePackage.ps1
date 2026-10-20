param(
    [string]$IdentityName,
    [string]$Publisher,
    [string]$PublisherDisplayName,
    [string]$Version,
    [string]$PublishDirectory,
    [string]$OutputDirectory,
    [string]$MakeAppxPath
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
[xml]$manifest = Get-Content -LiteralPath (Join-Path $repoRoot 'windows/Store/AppxManifest.xml') -Raw
if (-not $IdentityName) { $IdentityName = $manifest.Package.Identity.Name }
if (-not $Publisher) { $Publisher = $manifest.Package.Identity.Publisher }
if (-not $PublisherDisplayName) { $PublisherDisplayName = $manifest.Package.Properties.PublisherDisplayName }
if (-not $Version) { $Version = $manifest.Package.Identity.Version }
if (-not $PublishDirectory) { $PublishDirectory = Join-Path $repoRoot 'artifacts/windows/FirmaPDF' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts/store' }
if ($IdentityName -notmatch '^[a-zA-Z0-9.-]{3,50}$') { throw 'Copia Package/Identity/Name de Partner Center.' }
if ($Publisher -notmatch '^CN=') { throw 'Copia Package/Identity/Publisher completo de Partner Center.' }
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'La versión MSIX debe tener cuatro números.' }
$parts = $Version.Split('.') | ForEach-Object { [long]$_ }
if ($parts[0] -lt 1 -or ($parts | Where-Object { $_ -gt 65535 }).Count -gt 0 -or $parts[3] -ne 0) { throw 'Para Store, la versión debe empezar por 1 o más y terminar en 0; cada componente admite hasta 65535.' }
if ([string]::IsNullOrWhiteSpace($PublisherDisplayName)) { throw 'Copia Package/Properties/PublisherDisplayName de Partner Center.' }
if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory 'FirmaPDF.exe'))) { throw 'Compila primero la aplicación con Build-Windows.ps1.' }
$tool = Get-Command makeappx.exe -ErrorAction SilentlyContinue
if ($MakeAppxPath) { $toolPath = (Resolve-Path -LiteralPath $MakeAppxPath).Path }
elseif ($tool) { $toolPath = $tool.Source }
else {
    $sdk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $toolPath = Get-ChildItem -LiteralPath $sdk -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'x64/makeappx.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $toolPath) { throw 'Instala las herramientas MSIX del Windows SDK.' }
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$staging = Join-Path $OutputDirectory ('staging-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
Copy-Item -LiteralPath $PublishDirectory -Destination (Join-Path $staging 'App') -Recurse
Copy-Item -LiteralPath (Join-Path $repoRoot 'windows/Store/Assets') -Destination (Join-Path $staging 'Assets') -Recurse
$manifest.Package.Identity.Name = $IdentityName
$manifest.Package.Identity.Publisher = $Publisher
$manifest.Package.Identity.Version = $Version
$manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
$manifest.Save((Join-Path $staging 'AppxManifest.xml'))
$output = Join-Path $OutputDirectory "FirmaPDF-$Version.msix"
$log = Join-Path $OutputDirectory "makeappx-$Version.log"
& $toolPath pack /d $staging /p $output /o *> $log
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $log -Tail 24; throw 'No se pudo empaquetar o validar el MSIX.' }
$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$output.sha256" -Value "$hash  $([IO.Path]::GetFileName($output))" -Encoding ascii
Write-Output "MSIX para enviar a Microsoft Store: $output"
Write-Output 'Este MSIX está sin firmar: se envía a Partner Center; no es una entrega lista para instalar directamente.'
