param([string]$Artifacts = 'pdf/build/test-artifacts')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($Artifacts)) { $Artifacts = Join-Path $repoRoot $Artifacts }
$tool = Get-Command openssl -ErrorAction SilentlyContinue
if ($tool) { $openssl = $tool.Source }
else { $openssl = Join-Path $env:ProgramFiles 'Git/mingw64/bin/openssl.exe' }
if (-not (Test-Path -LiteralPath $openssl)) { throw 'Se necesita OpenSSL.' }
$root = Join-Path $repoRoot 'core/src/main/resources/tsa/DigiCertTrustedRootG4.cer'
$ca = Join-Path $Artifacts 'tsa-root.pem'
$token = Join-Path $Artifacts 'timestamp.der'
$signature = Join-Path $Artifacts 'signature-value.bin'
$chain = Join-Path $Artifacts 'tsa-chain.pem'
if (-not (Test-Path -LiteralPath $token)) { throw 'Ejecuta :pdf:test con FIRMAVISIBLE_LIVE_TSA=true para obtener el sello real de prueba.' }
& $openssl x509 -inform DER -in $root -out $ca
if ($LASTEXITCODE -ne 0) { throw 'No se pudo convertir la raíz de la TSA.' }
& $openssl ts -verify -token_in -in $token -data $signature -CAfile $ca -untrusted $chain
if ($LASTEXITCODE -ne 0) { throw 'OpenSSL no verifica el sello de tiempo real.' }
$altered = Join-Path $Artifacts 'signature-value-tampered.bin'
$bytes = [IO.File]::ReadAllBytes($signature)
$bytes[0] = $bytes[0] -bxor 1
[IO.File]::WriteAllBytes($altered, $bytes)
& $openssl ts -verify -token_in -in $token -data $altered -CAfile $ca -untrusted $chain 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) { throw 'OpenSSL ha aceptado un sello unido a otra firma.' }
$global:LASTEXITCODE = 0
Write-Output 'Sello RFC3161 de DigiCert verificado por OpenSSL; alteración rechazada. Sin comprobar revocación.'
