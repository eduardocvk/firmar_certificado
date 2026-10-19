Primera preversión de FirmaVisible para Windows x64 y Android 8 o posterior.

- Firma local de PDF con certificado del sistema o archivo `.p12/.pfx`.
- Selección de página y recuadro visible con nombre del certificado y fecha del dispositivo.
- Comprobación de integridad criptográfica antes de guardar la copia terminada.
- Windows: descargar y extraer `FirmaVisible-Windows.zip`; ejecutar `FirmaVisible.exe` manteniendo toda su carpeta. Incluye Java.
- Android: instalar `FirmaVisible-Android.apk`. Es un APK de depuración para pruebas; no usarlo como una entrega de producción. La clave que firma la aplicación es distinta de tu certificado FNMT. Las futuras compilaciones pueden requerir desinstalar la anterior.

**Límites:** sin sello de tiempo, sin validación de confianza/revocación y sin evidencias LT. La firma visible no acredita por sí sola validez jurídica. Las pruebas utilizan certificados autofirmados; quedan pendientes las pruebas con tu FNMT real, Adobe y el destinatario previsto. No se ha presentado esta versión como certificada ni endurecida para PDF hostiles.

Empieza con un documento de prueba y consulta las [instrucciones y limitaciones](https://github.com/eduardocvk/firmar_certificado#readme). No subas tu certificado ni su contraseña al repositorio.
