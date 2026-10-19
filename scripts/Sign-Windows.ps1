param(
    [Parameter(Mandatory)][string]$Thumbprint,
    [Parameter(Mandatory)][uri]$TimestampUrl,
    [string]$Directory
)
$ErrorActionPreference = 'Stop'
if (-not $Directory) { $Directory = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/windows/FirmaVisible' }
$Thumbprint = $Thumbprint -replace '\s', ''
if ($Thumbprint -notmatch '^[a-fA-F0-9]{40}$') { throw 'La huella debe ser SHA-1 del certificado, con 40 caracteres hexadecimales.' }
$cert = Get-Item -LiteralPath "Cert:\CurrentUser\My\$Thumbprint" -ErrorAction Stop
if (-not $cert.HasPrivateKey) { throw 'El certificado no tiene clave privada.' }
$eku = @($cert.EnhancedKeyUsageList | ForEach-Object { $_.ObjectId.Value })
if ($eku -notcontains '1.3.6.1.5.5.7.3.3') { throw 'Se necesita un certificado de firma de código (Code Signing). No se utilizará un certificado de firma de PDF.' }
if ((Get-Date) -lt $cert.NotBefore -or (Get-Date) -gt $cert.NotAfter) { throw 'El certificado no está vigente.' }
$chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
try {
    $chain.ChainPolicy.RevocationMode = [Security.Cryptography.X509Certificates.X509RevocationMode]::Online
    if (-not $chain.Build($cert)) { throw 'Windows no valida la confianza del certificado de firma de código.' }
} finally { $chain.Dispose() }
$tool = Get-Command signtool.exe -ErrorAction SilentlyContinue
if ($tool) { $toolPath = $tool.Source }
else {
    $sdk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $toolPath = Get-ChildItem -LiteralPath $sdk -Directory | Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'x64/signtool.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $toolPath) { throw 'Instala las herramientas de firma del Windows SDK.' }
}
$files = Get-ChildItem -LiteralPath $Directory -File | Where-Object { $_.Name -match '^FirmaVisible(?:\.Core)?\.(exe|dll)$' }
if (-not $files) { throw 'No se encuentran los binarios de FirmaVisible.' }
foreach ($file in $files) {
    & $toolPath sign /sha1 $Thumbprint /s My /fd SHA256 /tr $TimestampUrl.AbsoluteUri /td SHA256 $file.FullName
    if ($LASTEXITCODE -ne 0) { throw "No se pudo firmar $($file.Name)." }
    $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
    if ($signature.Status -ne 'Valid' -or -not $signature.TimeStamperCertificate) { throw "Firma o sello de tiempo no verificables: $($file.Name)." }
    Write-Output "$($file.Name): firmado por $($signature.SignerCertificate.Subject)"
}
