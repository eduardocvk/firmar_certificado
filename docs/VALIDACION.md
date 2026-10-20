# Validación de la preversión

Las identidades de las pruebas se generan al ejecutarlas, son autofirmadas y dicen expresamente «NO REAL». No contienen datos ni claves de una persona real. Que pasen las pruebas no acredita confianza de una autoridad certificadora.

Windows ejecuta 165 comprobaciones locales (166 al incluir el sello de tiempo real) en C# con iText, verificación independiente de CMS mediante .NET SignedCms y OpenSSL, y renderizado nativo Windows.Data.Pdf. También comprueba lotes de dos PDF, colisiones, fallo de TSA en el segundo documento sin copias publicadas, diseños guardados, pestañas, cierre independiente, filtro de archivos arrastrados y rutas entre instancias con tubería local; además de firma completa sin diálogos, recuerdo de identidad por huella con búsqueda simulada sin instalar certificados de prueba, opciones persistentes, expansión y maximización nativa, apertura de PDF por argumentos, menú de Explorer en registro aislado, conservación del lector predeterminado, fondo transparente sobre una página de color, tamaño de texto en el PDF, migración de preferencias anteriores y el diseño persistente, normalización de imágenes, sellos con nonce/huella/autoridad ajenos y sello alterado, un sello real de DigiCert, la ventana WPF y el editor. Los artefactos están en `artifacts/windows-tests`. El CI valida el MSIX con una identidad ficticia; no acredita publicación ni firma de editor.

El usuario confirmó que la versión Android anterior funciona, firma y obtiene una firma válida. Android 0.2 añade pruebas de apariencia personalizada y RFC3161; la revisión manual de los cambios con su FNMT sigue pendiente. Véase [Android 0.2](ANDROID_0_2.md).

## Pruebas automatizadas

| Prueba | Qué demuestra |
|---|---|
| RSA y EC | CMS verificable con ambos algoritmos admitidos. |
| PKCS#12 y contraseña incorrecta | Importación con clave privada; rechazo de contraseña errónea. |
| Certificado caducado | Rechazo antes de producir un resultado. |
| Rotaciones y CropBox | Recuadro en la posición visual elegida con 0, 90, 180 y 270 grados. |
| Atributos CMS | `SigningCertificateV2` presente; el motor Android omite `signingTime` en CMS. |
| Segunda firma | Conservación del prefijo original y verificación de ambas firmas. |
| Alteración de datos | Rechazo criptográfico de un PDF alterado. |
| Bytes posteriores a firma | Se informa de revisión posterior; no se vuelve a firmar automáticamente. |
| Sobrescritura | El original y un destino existente permanecen intactos. |
| Restricciones | Rechazo de documentos certificados con DocMDP. |
| OpenSSL | Otra implementación verifica el CMS, sin validar confianza, y rechaza contenido alterado. |
| Instrumentación Android | Importa una identidad PKCS#12, firma con el motor Android y renderiza la apariencia con PDFRenderer nativo a través de la copia de visualización en las cuatro rotaciones. Comprueba que esa copia no modifica el original firmado. |

Los informes de cada ejecución están en Actions. Las pruebas de JVM producen ejemplos de PDF y capturas dentro de `pdf/build/test-artifacts`, excluidos de Git.

## Comprobación manual antes de utilizar con documentos reales

1. Abrir un PDF de prueba que no contenga datos personales.
2. Firmar con una identidad FNMT en Windows desde Windows-MY y, por separado, desde su copia PKCS#12.
3. Abrir el resultado en Adobe Reader: comprobar integridad, nombre del firmante, cadena, estado de revocación y mensajes de confianza. Una apariencia visible no demuestra esos estados.
4. Firmar en un Android físico con KeyChain y mediante importación PKCS#12. Probar selección de página, pellizco, movimiento, cambio de tamaño, cancelación de contraseña, giro, guardar y compartir.
5. Repetir con varias páginas, documentos escaneados y una segunda firma.
6. Comprobar una muestra en el servicio receptor previsto. El hecho de que Adobe pueda abrirlo no garantiza que un trámite concreto lo admita.

Estas comprobaciones manuales permanecen pendientes hasta disponer del dispositivo/certificado y de un destinatario concreto. No introducir el certificado ni su contraseña en GitHub, registros o capturas públicas.
