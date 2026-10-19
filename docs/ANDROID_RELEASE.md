Android 0.2: diseño renovado, firma visible personalizada y sello de tiempo.

- Interfaz con tarjetas, vista del PDF y adaptación a vertical/horizontal.
- **Personalizar firma**: texto propio, variables `{nombre}`, `{fecha}`, `{emisor}`, imagen PNG/JPEG y fondo con marco o transparente.
- El diseño se conserva localmente para próximas firmas. No guarda certificado ni contraseña. La imagen se ajusta sin deformarse y se retiran sus metadatos.
- **Sello de tiempo**: RFC3161 de DigiCert. Envía solo una huella SHA-256 de la firma; nunca el PDF, la clave privada ni el archivo de certificado. Requiere Internet. Se puede desactivar para firmar sin conexión. Si se pide sello y falla, no se guarda una copia sin él.
- Hora acreditada visible en la confirmación y en el panel de firmas de un visor compatible. `{fecha}` y la fecha de la apariencia siguen siendo la hora declarada del móvil.

Se comprueban nonce, huella, firma de la TSA y cadena a la raíz DigiCert G4 incorporada. No se añade validación de revocación, evidencias LT/LTA ni se declara sello cualificado. El endpoint RFC3161 oficial de DigiCert usa HTTP; solo ese dominio tiene permiso de tráfico claro. La autenticidad se verifica sobre el token firmado.

APK **de pruebas**, firmado con clave de depuración. Si Android impide actualizar por diferencia de firma de aplicación, puede ser necesario desinstalar la versión anterior. Esto elimina sus datos locales. No afecta a los PDF guardados fuera de la app; conserva las copias y tu certificado antes de hacerlo. La clave del APK es distinta del certificado FNMT de los PDF.

Pruebas: CMS, sello real y alteración con verificación independiente mediante OpenSSL; instrumentación Android con imagen, texto, cuatro rotaciones, persistencia y capturas de interfaz. La comprobación real FNMT/Adobe de esta nueva versión corresponde al usuario.
