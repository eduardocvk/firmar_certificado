Versión de Windows reescrita en **C#/.NET 10 y WPF**. Incluye el runtime .NET y no necesita Java.

- Abrir PDF, elegir certificado del almacén de Windows o archivo `.p12/.pfx` y colocar la firma visible.
- Firma CAdES en PDF mediante iText Community, SHA-256, clave del sistema sin exportación y guardado incremental.
- Conserva y verifica firmas anteriores y comprueba el resultado antes de guardar la copia.
- Visor nativo de Windows; pruebas de rotación, CropBox, RSA, EC y segunda firma.

Extraer `FirmaVisible-Windows-NET.zip` y ejecutar `FirmaVisible.exe` conservando su carpeta completa.

**Esta descarga sigue sin firma de editor Authenticode.** No se dispone todavía de un certificado de firma de código ni de una cuenta verificada para publicar en Store. Por ello **SmartScreen puede bloquear este ZIP descargado**. La migración a .NET no se presenta como una solución a ese control.

Para probar como desarrollo local, se incluye `scripts/Build-Windows.ps1`. Para publicación sin el aviso de descarga, están preparados un manifiesto MSIX y `scripts/New-StorePackage.ps1`: requieren los datos de identidad de Partner Center y la aprobación de Microsoft. No se ha desactivado SmartScreen ni instalado certificados de confianza.

Consulta [cómo obtener la identidad o publicar en Store](https://github.com/eduardocvk/firmar_certificado/blob/main/windows/DISTRIBUCION.md).

Preversión: sin sello de tiempo ni comprobación PKI/OCSP/CRL. Falta probar con el certificado FNMT real y Adobe Reader. Código Windows bajo AGPL-3.0-or-later por su integración con iText Community; fuente completa en el repositorio.

La aplicación Android conserva su versión anterior y su descarga independiente.
