# Decisiones de arquitectura

## Firma local y aplicaciones nativas

Windows usa Swing y Java 17; Android usa APIs Java nativas, minSdk 26. Comparten la construcción CMS, las comprobaciones del certificado y las coordenadas del recuadro. La clave del almacén de Windows o Android se utiliza mediante su interfaz de firma; no se exporta. La importación PKCS#12 utiliza una instancia explícita del proveedor Bouncy Castle, sin sustituir globalmente proveedores del sistema Android.

Se descarta un backend custodio de certificados en esta versión. La app no pide permiso de Internet en Android. El usuario autoriza cada firma desde una confirmación, además de los diálogos que requiera el proveedor de claves.

## PDFBox en las dos plataformas

JVM usa Apache PDFBox 2.0.36. Android usa `com.tom-roush:pdfbox-android:2.0.27.0`, port basado en una versión anterior de PDFBox. La tarea `generatePdfEngine` cambia únicamente imports `org.apache.pdfbox` → `com.tom_roush.pdfbox`, de modo que el algoritmo permanece en una fuente. El renderizado de Android usa `android.graphics.pdf.PdfRenderer`.

La API clásica de PdfRenderer omite apariencias de ciertos campos de formulario, incluidas firmas. Para mostrarlos, `PreviewDocument` genera una copia desechable que aplana los campos de formulario. **Esa copia solo se renderiza: nunca se firma, verifica o exporta.** Todas las operaciones de firma, comprobación y exportación utilizan el PDF firmado original. Se rechazan formularios XFA. La prueba de instrumentación comprueba que generar la vista previa no cambia ningún byte del original. Algunos visores externos también omiten campos de firma; para consultar firmas debe utilizarse un visor compatible, como Adobe Reader. Véanse las [opciones de renderizado de formularios de Android](https://developer.android.com/reference/android/graphics/pdf/RenderParams).

El port Android retorna de `addSignature` antes de registrar la anotación en la página cuando se proporciona la apariencia después de esa llamada. El motor compartido registra explícitamente el widget, evitando duplicarlo en PDFBox de escritorio. La instrumentación comprueba también que la anotación aparezca en el PDF guardado.

**Limitación de mantenimiento:** el port Android va por detrás de PDFBox de escritorio. La coincidencia de API no significa coincidencia de correcciones de seguridad. Antes de producción hay que evaluar un port mantenido con parches o un SDK alternativo, revisar sus licencias y añadir pruebas de documentos adversarios. No se presenta esta preversión como un lector endurecido para documentos hostiles.

## Flujo de firma

1. Trabajar sobre una copia de entrada que también se utiliza en la vista previa.
2. Comprobar restricciones del documento y que las firmas previas tengan integridad criptográfica y cubran hasta su última revisión sin bytes posteriores.
3. Comprobar vigencia, uso de clave y tamaño de clave del certificado seleccionado.
4. Convertir coordenadas normalizadas del visor a coordenadas PDF teniendo en cuenta CropBox y rotación 0/90/180/270.
5. Crear un campo de firma con apariencia y `/ETSI.CAdES.detached`.
6. Construir CMS separado con certificado(s), SHA-256 y `SigningCertificateV2`; la fecha declarada se guarda en `/M` del PDF, sin atributo CMS `signingTime`.
7. Guardar incrementalmente en un archivo temporal.
8. Volver a abrir, verificar todas las firmas CMS y confirmar que la última cubre hasta EOF.
9. Mover al destino sin reemplazar un archivo existente. En Android, exportar la copia verificada con Storage Access Framework o compartir por FileProvider de solo lectura.

La apariencia tiene un BBox en coordenadas visuales y una matriz que compensa la rotación de página. Las pruebas renderizan el resultado con CropBox de origen distinto de cero para evitar errores que quedan ocultos en PDF A4 simples.

## Distribución

Windows: `jpackage --type app-image`, runtime incluido. Android: APK debug para pruebas. Para actualizaciones de producción se necesitará una clave de firma estable, respaldada fuera del repositorio y custodiada por el propietario. Esa clave firma la **aplicación** y es distinta del certificado FNMT que firma los **PDF**.

## Evolución a B-T y LT

El sello de tiempo requiere seleccionar una TSA, gestionar su autenticación y verificar su respuesta (nonce, algoritmo, imprint, firma y confianza). El nivel LT requiere recopilar y validar las evidencias de revocación y cadena necesarias e incorporarlas al PDF según el estándar. No debe simularse LT adjuntando datos sin comprobarlos. La validación de confianza debería utilizar una política explícita y, preferiblemente, una implementación consolidada como DSS. Hasta entonces la interfaz informa exclusivamente de integridad criptográfica.
