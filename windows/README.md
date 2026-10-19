# FirmaPDF para Windows — C#/.NET y WPF

Versión 0.3. Motor e interfaz completamente .NET: no se ejecuta Java ni se invoca el antiguo firmador Java desde un proceso auxiliar. Visor `Windows.Data.Pdf`, claves del usuario mediante las APIs criptográficas de .NET e iText Community 9.8 para firma incremental de PDF.

## Compilar y probar

Requiere Windows x64 y SDK .NET 10. Desde la raíz:

```powershell
dotnet run --project windows/FirmaVisible.Tests/FirmaVisible.Tests.csproj -c Release -- artifacts/windows-tests
.\scripts\verify-openssl.ps1 -Artifacts artifacts/windows-tests
.\scripts\Build-Windows.ps1
```

Ejecutable local: `artifacts/windows/FirmaPDF/FirmaPDF.exe`. La carpeta contiene el runtime .NET y todas las dependencias. No requiere instalación de Java ni de .NET en el equipo de destino. Conservar toda la carpeta. Los archivos `packages.lock.json` fijan las dependencias; el script de publicación usa restauración bloqueada.

La suite usa identidades efímeras autofirmadas «NO REAL», comprueba RSA y EC, CMS con `SignedCms` de .NET, las cuatro rotaciones con CropBox desplazado, original intacto, segunda firma incremental, certificado caducado, contraseña incorrecta, alteración y revisiones posteriores. El visor nativo de Windows renderiza cada resultado y se comprueba la ubicación visible. También genera una captura de la interfaz WPF sin abrir una ventana interactiva.

## Operaciones

1. Abrir PDF: la vista previa y la firma utilizan una copia local del mismo documento.
2. Elegir certificado del almacén personal de Windows o abrir PKCS#12 con contraseña. La importación utiliza `EphemeralKeySet` y dura la sesión.
3. Personalizar texto e imagen con fondo transparente, marco opcional y texto de 4 a 18 pt, incluyendo `{nombre}`, `{fecha}` y `{emisor}`. El diseño se conserva en `%LOCALAPPDATA%\FirmaVisible\settings.json` junto con la preferencia de sello, sin certificado ni contraseña.
4. Activar o desactivar el sello de tiempo. Elegir página y dibujar, mover o redimensionar el recuadro. Ajustar zoom si hace falta.
5. Confirmar identidad y uso del sello; guardar una copia con nombre nuevo. **Abrir última copia firmada** utiliza el visor configurado en Windows.

El código no exporta las claves del almacén para firmar. Las consultas de cadena que se usan para incorporar certificados públicos desactivan descargas y revocación, por lo que **no se presentan como validación de confianza**. La copia especial para renderizar formularios se aplana exclusivamente para visualización: nunca se firma, comprueba ni exporta.

## Firma y límites

`ETSI.CAdES.detached`, SHA-256, certificado firmante y cadena disponible, `SigningCertificateV2`, apariencia incorporada y guardado incremental. Se comprueban todas las firmas antes y después de añadir otra. Se rechazan PDF cifrados, XFA, DocMDP y campos con bloqueo, o documentos con bytes posteriores a la última firma.

Sello opcional RFC3161 de DigiCert: se comprueban nonce, huella, firma/EKU y cadena con raíz DigiCert G4 incorporada. Solo se envía la huella del valor de firma, nunca PDF, certificados del usuario ni claves. Sin comprobación de revocación, cualificación o evidencias LT. El endpoint oficial es HTTP y se verifica criptográficamente el token; no se confía en el transporte. Si falla el sello solicitado, no se produce un resultado degradado. La hora acreditada se muestra al terminar y en el PDF. La fecha visible procede del dispositivo. Pendiente validación manual con FNMT real, Adobe Reader y el destinatario previsto.

## Firma del programa y distribución

La firma de los PDF y la identidad del editor del ejecutable son independientes. La compilación actual no tiene Authenticode y puede ser bloqueada por Windows al descargarla. Se ha preparado MSIX para Store y un script para certificados Code Signing, pero faltan la identidad verificada y la revisión/publicación externa. [Pasos y alternativas](DISTRIBUCION.md).

## Licencia

El código y la aplicación de `windows/` se ofrecen bajo **AGPL-3.0-or-later**, al integrar iText Community. Fuente completa y licencia incluidas; se conservan Producer y avisos de iText en la salida. Consulta `LICENSE.txt`, `ITEXT-LICENSE.md`, `THIRD_PARTY.txt` y `licenses/`. Los componentes Java/Android del repositorio son independientes y conservan Apache-2.0.

Para probar el sello real con una identidad sintética, define FIRMAVISIBLE_LIVE_TSA=true antes de ejecutar las pruebas y después ejecuta scripts/verify-timestamp.ps1 -Artifacts artifacts/windows-tests. No se comprueba la revocación. El CI ejecuta esta comprobación y OpenSSL exige que se rechace una alteración.
