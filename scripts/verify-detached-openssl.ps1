param([string]$Artifacts='artifacts/windows-tests')
$ErrorActionPreference='Stop'
$tool=Get-Command openssl -ErrorAction SilentlyContinue
$openssl=if($tool){$tool.Source}else{Join-Path $env:ProgramFiles 'Git/usr/bin/openssl.exe'}
if(-not(Test-Path -LiteralPath $openssl)){throw 'No se encuentra OpenSSL para la verificación independiente.'}
$folder=Join-Path $Artifacts 'other-files'
$original=Join-Path $folder 'Documento á.xml'
$signature=$original+'.p7s'
$certificate=Join-Path $folder 'test-signer.pem'
$output=Join-Path $folder 'openssl-verified.xml'
& $openssl cms -verify -cades -binary -inform DER -in $signature -content $original -CAfile $certificate -purpose any -out $output
if($LASTEXITCODE -ne 0){throw 'OpenSSL rechaza la firma separada CAdES.'}
if((Get-FileHash -LiteralPath $output).Hash -ne (Get-FileHash -LiteralPath $original).Hash){throw 'OpenSSL ha verificado un contenido diferente.'}
& $openssl cms -verify -cades -binary -inform DER -in $signature -content (Join-Path $folder 'otro.xml') -CAfile $certificate -purpose any -out (Join-Path $folder 'openssl-rejected.xml') 2>$null
if($LASTEXITCODE -eq 0){throw 'OpenSSL acepta otro archivo como contenido firmado.'}
& $openssl cms -verify -cades -binary -inform DER -in (Join-Path $folder 'alterada.p7s') -content $original -CAfile $certificate -purpose any -out (Join-Path $folder 'openssl-rejected.xml') 2>$null
if($LASTEXITCODE -eq 0){throw 'OpenSSL acepta una referencia ESSCertIDv2 incorrecta.'}
Write-Output 'OpenSSL: CAdES separada correcta; contenido distinto y referencia de certificado incorrecta rechazados.'
$global:LASTEXITCODE=0
