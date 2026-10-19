param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts/windows/FirmaVisible' }
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) { $dotnetPath = $dotnet.Source }
else {
    $dotnetPath = Join-Path $repoRoot '.tools/dotnet/dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnetPath)) { throw 'Instala el SDK .NET 10 desde https://dotnet.microsoft.com/download/dotnet/10.0 y vuelve a ejecutar este script.' }
}
$project = Join-Path $repoRoot 'windows/FirmaVisible.App/FirmaVisible.App.csproj'
Push-Location (Join-Path $repoRoot 'windows')
try {
    & $dotnetPath publish $project -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -p:RestoreLockedMode=true -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar la aplicación.' }
} finally { Pop-Location }
Copy-Item -LiteralPath (Join-Path $repoRoot 'windows/LEEME.txt') -Destination (Join-Path $OutputDirectory 'LEEME.txt') -Force
Write-Output "Aplicación compilada localmente: $(Join-Path $OutputDirectory 'FirmaVisible.exe')"
Write-Output 'No se ha añadido una firma de editor ni modificado la seguridad de Windows.'
