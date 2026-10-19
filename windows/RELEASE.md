FirmaPDF Windows 0.3.1: nuevo nombre, con los diseños guardados de FirmaVisible conservados.

Windows 0.3 en C#/.NET y WPF: diseño renovado, apariencia personalizada y sello de tiempo.

- Abrir PDF y elegir certificado del almacén de Windows o archivo `.p12/.pfx`.
- **Personalizar texto e imagen** con las variables `{nombre}`, `{fecha}` y `{emisor}`. PNG/JPEG, transparencia, proporción conservada y fondo con marco o transparente.
- El diseño se guarda en `%LOCALAPPDATA%\FirmaVisible\settings.json` para próximas sesiones, sin certificado, contraseña ni clave privada.
- **Sello de tiempo** RFC3161 de DigiCert, opcional y con Internet. Envía solo la huella SHA-256 de la firma y el nonce; nunca el PDF ni tus claves. Si falla, no se guarda una copia sin el sello solicitado.
- Hora acreditada en la confirmación final y en el panel de firmas del visor. La fecha impresa sigue siendo la del ordenador.
- Vista previa, zoom/ajuste, colocar/mover/redimensionar firma, conservación de firmas anteriores y botón para abrir la última copia.

Descarga y extrae `FirmaPDF-Windows-NET.zip`. Conserva toda la carpeta y abre `FirmaPDF.exe`. Incluye .NET y no necesita Java. Para desarrollo local: `scripts/Build-Windows.ps1`.

**Esta descarga sigue sin firma Authenticode de editor. SmartScreen puede bloquearla.** La vía preparada para distribución desde Microsoft Store sigue pendiente de cuenta verificada y aprobación. No se desactiva SmartScreen ni se instalan certificados de confianza. [Pasos de distribución](https://github.com/eduardocvk/firmar_certificado/blob/main/windows/DISTRIBUCION.md).

El sello comprueba nonce, impronta, firma/EKU y cadena hasta DigiCert G4 incorporada. No se comprueba revocación ni cualificación del servicio y no se incorporan evidencias LT/LTA. El endpoint oficial de DigiCert usa HTTP; la autenticidad se verifica sobre el token firmado. Los documentos y las claves permanecen en el equipo.

Preversión bajo AGPL-3.0-or-later por iText Community; fuente completa en el repositorio y licencias incluidas. Pruebas con identidades sintéticas, verificación independiente de CMS y sello real con OpenSSL y renderizador nativo de Windows. Pendiente comprobación manual de esta versión con el FNMT real, Adobe Reader y el destinatario previsto.
