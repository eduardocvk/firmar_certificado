# Android 0.2: diseño, apariencia y hora

El propietario confirmó que la versión Android anterior firma y que su firma es válida en su comprobación. Esta versión conserva ese motor y añade personalización y sellado de tiempo. Las nuevas funciones también deben revisarse con su certificado y visor habituales.

## Uso

1. Abre el PDF y elige el certificado Android o `.p12/.pfx`.
2. En **Personalizar firma**, escribe el texto y añade una imagen. Las variables `{nombre}`, `{fecha}` y `{emisor}` se sustituyen al firmar. El diseño se guarda en el almacenamiento privado del móvil, sin certificado ni contraseña.
3. El fondo es transparente; el marco es opcional. Ajusta el texto entre 4 y 18 pt (8 pt por defecto); se reduce si hace falta para caber. La imagen mantiene proporciones y transparencia; puede ocupar toda la apariencia dejando el texto vacío. La fuente Noto Sans se incorpora al PDF, incluidos acentos y caracteres latinos. Los caracteres no soportados provocan un error, sin sustituciones silenciosas.
4. Coloca el recuadro en la página. La vista previa utiliza la misma fuente y algoritmo de distribución; el texto exacto de fecha se fija al firmar. Si un diseño no cabe de forma legible, amplía el recuadro o reduce el texto.
5. Activa o desactiva **Sello de tiempo** y pulsa **Firmar y guardar copia**. La confirmación informa de la identidad y del uso del servicio externo.
6. Guarda o comparte. El menú **•••** recupera la última copia y permite comprobar firmas del PDF abierto.

La app conserva un diseño por móvil. Una imagen importada se limita a 1200 píxeles, se corrige su orientación y se convierte a PNG sin los metadatos originales. Se admiten entradas de hasta 16 MB y salidas de hasta 4 MB. No se copia el archivo del certificado dentro del diseño.

## Dos fechas distintas

La fecha visible y `/M` declaran la hora del dispositivo. El sello RFC3161 acredita que el valor de la firma existía a la hora emitida por la TSA. Esa hora aparece en la confirmación final y en el token incorporado al CMS. No se reemplaza la apariencia después de firmar, porque invalidaría la firma.

La app usa el [endpoint oficial de DigiCert](https://knowledge.digicert.com/general-information/rfc3161-compliant-time-stamp-authority-server), `http://timestamp.digicert.com`. Solo se envían la huella SHA-256 del valor de firma, el nonce y parámetros RFC3161. No se envían el PDF ni los certificados del usuario, claves o contraseñas. El prestador ve la IP y los datos de la consulta.

El endpoint publicado usa HTTP. Android permite tráfico claro únicamente para ese dominio, sin subdominios ni redirecciones. La autenticidad se establece comprobando la firma del token y su cadena hasta DigiCert Trusted Root G4 incorporada; no se confía en el transporte ni en una raíz entregada por el servidor.

Se comprueban estado, nonce, algoritmo e impronta SHA-256, identificador de certificado, firma, uso exclusivo de sellado de tiempo y vigencia a la fecha del token. La cadena PKIX se construye con los certificados del token y la raíz pública incorporada. La hora emitida debe estar a menos de 10 minutos del reloj del dispositivo: si difiere, se pide revisar el reloj.

Si se solicita sello y la conexión o comprobación falla, no se produce una copia sin sello ni se sobrescribe una copia anterior. Se puede volver a intentar o desactivar la opción explícitamente para firmar sin conexión.

**Límites:** no se comprueban OCSP/CRL, listas de confianza europeas ni cualificación del servicio; no se incorporan evidencias LT/LTA ni se afirma que el sello sea cualificado. La presencia de un sello no garantiza por sí sola validez indefinida. El botón de comprobación verifica integridad CMS y enlace/firma de los sellos incluidos; no afirma confianza de cualquier TSA de documentos externos. Consulta el estado completo en Adobe o en el destinatario previsto.

## Verificación

La suite JVM cubre texto/imagen, cuatro rotaciones y CropBox, original intacto, imagen transparente, límites de texto, token con nonce o huella incorrectos, autoridad ajena y fallo de conexión sin resultado degradado. La instrumentación utiliza el renderizador Android con apariencias personalizadas, PKIX para una TSA sintética de prueba, normalización de imágenes, persistencia del diseño y la interfaz en ambas orientaciones.

Para probar el servicio real con identidad sintética y extraer evidencias públicas:

```powershell
$env:FIRMAVISIBLE_LIVE_TSA='true'
.\gradlew.bat :pdf:test
.\scripts\verify-openssl.ps1
.\scripts\verify-timestamp.ps1
```

OpenSSL verifica de forma independiente tanto las firmas CMS como el token real de DigiCert, su unión al valor de firma y la cadena a la raíz incorporada. También se exige que rechace la alteración. No se usa el certificado FNMT del usuario en estas pruebas.
