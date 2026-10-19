$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repoRoot 'pdf/build/test-artifacts'
$openssl = Get-Command openssl -ErrorAction SilentlyContinue
if (-not $openssl) {
    $gitOpenSSL = 'C:\Program Files\Git\mingw64\bin\openssl.exe'
    if (-not (Test-Path -LiteralPath $gitOpenSSL)) { throw 'No se encuentra OpenSSL.' }
    $opensslPath = $gitOpenSSL
} else { $opensslPath = $openssl.Source }
foreach ($rotation in @(0, 90, 180, 270)) {
    $pdfPath = Join-Path $artifactRoot "signed-$rotation.pdf"
    $data = [IO.File]::ReadAllBytes($pdfPath)
    $text = [Text.Encoding]::GetEncoding(28591).GetString($data)
    $match = [regex]::Match($text, '/ByteRange\s*\[\s*0\s+(\d+)\s+(\d+)\s+(\d+)\s*\]')
    if (-not $match.Success) { throw 'ByteRange no encontrado.' }
    $a = [int]$match.Groups[1].Value
    $b = [int]$match.Groups[2].Value
    $c = [int]$match.Groups[3].Value
    $content = [byte[]]::new($a + $c)
    [Array]::Copy($data, 0, $content, 0, $a)
    [Array]::Copy($data, $b, $content, $a, $c)
    $hex = $text.Substring($a + 1, $b - $a - 2) -replace '\s', ''
    $cms = [Convert]::FromHexString($hex)
    $cmsPath = Join-Path $artifactRoot "signature-$rotation.der"
    $contentPath = Join-Path $artifactRoot "content-$rotation.bin"
    [IO.File]::WriteAllBytes($cmsPath, $cms)
    [IO.File]::WriteAllBytes($contentPath, $content)
    & $opensslPath cms -verify -binary -inform DER -in $cmsPath -content $contentPath -noverify -out NUL
    if ($LASTEXITCODE -ne 0) { throw "OpenSSL no verifica la firma de la página girada $rotation." }
    # Also prove that a changed signed byte is rejected by an independent implementation.
    $content[0] = $content[0] -bxor 1
    [IO.File]::WriteAllBytes($contentPath, $content)
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $opensslPath cms -verify -binary -inform DER -in $cmsPath -content $contentPath -noverify -out NUL 2>$null
    $failedAsExpected = $LASTEXITCODE -ne 0
    $ErrorActionPreference = $previousPreference
    if (-not $failedAsExpected) { throw 'OpenSSL no ha rechazado el contenido alterado.' }
    Write-Output "Rotación ${rotation}: firma correcta y alteración rechazada (sin validar confianza PKI)."
}
$global:LASTEXITCODE = 0
