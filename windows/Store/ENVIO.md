# Primera entrega de FirmaPDF a Microsoft Store

## Archivos e identidad

- Archivo de entrega actual: `artifacts/store/FirmaPDF-1.0.1.0.msix` (sustituye a la preparación `1.0.0.0`).
- Identidad: `EduardoCallejo.FirmaPDF`.
- Publisher: `CN=D317ECBE-86A1-4610-9063-9C16F1256EC9`.
- Nombre público del editor: `Eduardo Callejo`.
- Arquitectura: x64. Windows 10, compilación 19041, o posterior; Windows 11.
- Versión actual del paquete Store: `1.0.1.0`. La compilación interna de la aplicación continúa en la rama de desarrollo `0.4.0`.

El paquete de subida no tiene firma de editor. Se envía a Partner Center, no se instala haciendo doble clic. Microsoft lo firma después de superar su certificación. La copia local de desarrollo y el ZIP de GitHub no adquieren esa firma por disponer de esta identidad.

## Completar la entrega en Partner Center

1. Abre **Aplicaciones y juegos → FirmaPDF → Iniciar envío**.
2. En **Paquetes**, carga el MSIX indicado y espera la validación del portal.
3. En **Precios y disponibilidad**, selecciona gratuito o el precio que decidas y los mercados que quieras habilitar. El MSIX no fija el precio: esta opción se gestiona en Partner Center.
4. En **Propiedades**, selecciona la categoría Productividad. Web y soporte: `https://github.com/eduardocvk/firmar_certificado` y `https://github.com/eduardocvk/firmar_certificado/issues`.
5. URL de privacidad: `https://github.com/eduardocvk/firmar_certificado/blob/main/windows/PRIVACY.md`.
6. Completa el cuestionario de edades según las funciones reales de esta herramienta de documentos; no declares funciones sociales, anuncios ni compras que no existen.
7. Añade la descripción, características y capturas reales de la aplicación en español. Las pruebas generan `store-screenshot-signing.png` y `store-screenshot-result.png`, de 1600 × 1000 píxeles, con documentos e identidad ficticios. No contienen datos privados del usuario. El logotipo puede obtenerse del paquete; no es necesario crear imágenes promocionales adicionales para esta primera entrega.
8. En **Opciones de envío**, explica `runFullTrust` con el texto que sigue. Revisa lo que marque pendiente el portal antes de pulsar **Enviar para certificación**.

## Descripción propuesta

FirmaPDF permite firmar PDF en tu ordenador con un certificado digital y colocar una firma visible donde elijas. Abre varios documentos en pestañas, dibuja el recuadro en cada uno y guarda sus copias firmadas en una sola operación.

Personaliza varios diseños con texto e imagen, utiliza un fondo transparente y ajusta automáticamente el texto al espacio disponible. Puedes usar certificados del almacén de Windows o abrir un archivo .p12/.pfx compatible. Incluye un sello de tiempo opcional que requiere Internet.

Los documentos y las claves se procesan localmente. Las copias se guardan junto al original con el sufijo _firmado, sin sobrescribir los originales ni archivos existentes. La comprobación integrada informa sobre la integridad criptográfica; no sustituye la validación de confianza y revocación del certificado ni certifica la cualificación de la firma.

Aplicación de código abierto bajo AGPL-3.0-or-later. Consulta el código fuente y las licencias desde Configuración.

## Si se ofrece por 5 euros

El registro de desarrollador gratuito no obliga a distribuir la aplicación gratis. La venta y la instalación se realizan en Microsoft Store una vez aprobada y publicada; preparar un MSIX o reservar el nombre no la publica automáticamente.

En **Precios y disponibilidad → Precio base**, elige un nivel cercano a 4,99 EUR y revisa el precio por mercado. Para fijar exactamente 5 EUR en España, crea una anulación del precio para ese mercado y utiliza el precio libre en moneda local dentro del rango que permita el portal. Los precios, impuestos y liquidaciones pueden variar por mercado. Completa en la cuenta los perfiles de cobro y fiscales que Microsoft solicite. Esta preparación no ha cambiado precios ni enviado una publicación a tu cuenta.

La AGPL-3.0-or-later permite cobrar por distribuir copias, manteniendo las obligaciones de licencia y acceso al código fuente; los destinatarios conservan sus derechos de modificación y redistribución. La descarga pública gratuita y las herramientas alternativas existentes deben tenerse en cuenta al fijar el precio. No se añade DRM propio ni se cambia la licencia por vender a través de Store.

Fuentes: [precios MSIX](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/schedule-pricing-changes), [configuración de cobros y datos fiscales](https://learn.microsoft.com/en-us/partner-center/partner-center-account-setup), [texto AGPL-3.0, apartado 4](https://opensource.org/license/agpl-3-0).

## Justificación de runFullTrust y notas para certificación

FirmaPDF is a self-contained .NET/WPF desktop application for signing local PDF files. Full trust is required to access files explicitly selected by the user, render PDFs with Windows APIs, use Windows certificate-store private keys through cryptographic providers, and produce incremental digital PDF signatures. It does not request administrator privileges, install drivers, export the user's Windows private keys, or modify security settings. The packaged version declares its PDF file association in the manifest instead of writing a portable executable path to the shell registry.

Reviewers can open a non-sensitive PDF, select a test certificate with a private key from the current-user Windows certificate store or a test .pfx file, draw a signature rectangle, and click Firmar. Disable Sello de tiempo in Settings to test without a network connection. If enabled, the optional DigiCert timestamp service receives a hash of the signature value and a nonce, not the PDF or the user's certificate/private key. No developer account or login is required inside FirmaPDF. Use synthetic certificates for testing, not a customer's private identity.

## Validación y límites de esta entrega

- MakeAppx comprueba el manifiesto, la estructura, los recursos y genera el paquete con la identidad indicada.
- Las pruebas de la aplicación incluyen firma criptográfica, renderizado nativo, rotación de página, lotes, preferencias y apertura por argumentos.
- Esta preparación no equivale a una certificación de Microsoft ni a un resultado de Windows App Certification Kit. El WACK y la prueba de instalación/activación dentro de MSIX no se han ejecutado en este ordenador. No se habilita el modo desarrollador ni se instala un certificado de confianza para eludir esa limitación.
- La asociación declarada permite **Abrir con → FirmaPDF**; el verbo de edición del PDF depende de la asociación y de cómo lo presente Windows. No es una extensión COM del menú moderno de Windows 11. La versión portable conserva su menú clásico independiente.

Fuentes: [envío de aplicaciones](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/create-app-submission), [firma y requisitos de MSIX](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements), [identidad del producto](https://learn.microsoft.com/en-us/windows/apps/publish/view-app-identity-details).
