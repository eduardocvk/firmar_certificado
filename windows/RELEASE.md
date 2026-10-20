FirmaPDF Windows 0.4.0: pestañas, firmas guardadas y firma de todos los PDF en un paso.

- Rueda de configuración junto al título. Allí se gestionan las firmas de texto e imagen, las opciones de firma, el sello, el menú del botón derecho y las licencias.
- Varios diseños con nombre (hasta 20), con selección independiente por PDF en «03 Tu firma visible». La firma anterior se conserva como «Mi firma».
- Botón azul «Poner firma». También puedes dibujar directamente sobre el PDF: cada pestaña recuerda su recuadro, página y diseño.
- Apertura de varios PDF desde el selector o arrastrándolos a cualquier zona de la ventana. Un PDF ya abierto activa su pestaña. Los abiertos desde otra ejecución de esta misma copia llegan a la ventana existente.
- «Firmar» procesa todos los pendientes, con una sola confirmación opcional y un solo aviso final. Se guardan junto al original como nombre_firmado.pdf, sin selector de destino.
- Si falta un recuadro o existe una copia de destino, se avisa antes de empezar. Se verifican todas las firmas antes de publicar las copias; un fallo de firma o sello durante esa preparación descarta los temporales.
- Si Windows impide publicar un archivo después de haber publicado otros, se conservan las copias ya guardadas y se informa de cuáles terminaron. Nunca se sobrescriben archivos existentes.
- Las pestañas ya firmadas se marcan con ✓ y se omiten al firmar nuevos documentos. Los originales siempre se conservan.

FirmaPDF Windows 0.3.4: ventana maximizada, último certificado y avisos configurables.

- Corregidos los límites de ventana que dejaban espacio negro al maximizar. Windows gestiona el tamaño de la ventana maximizada.
- Se recuerda el certificado de la última firma completada y se preselecciona al iniciar si está disponible y vigente en el almacén de Windows. Solo se guarda su huella SHA-256; «Olvidar» elimina esa referencia.
- Los .p12/.pfx siguen requiriendo abrirlos con contraseña cuando no están instalados en Windows. No se guarda su clave ni contraseña.
- Opciones independientes para confirmar antes de firmar y mostrar el aviso de éxito. Puedes desactivarlas en «Opciones de firma» o marcar «No volver a mostrar» en los propios avisos.
- El archivo y la hora del resultado permanecen visibles en la ventana, con botón para abrir la copia. Los errores siguen avisándose y el destino se sigue eligiendo al guardar.
- El diseño, tamaño del texto, sello y las nuevas preferencias se conservan al actualizar.

FirmaPDF Windows 0.3.3: abrir PDF desde el menú del botón derecho.

- «Firmar con FirmaPDF» abre el PDF seleccionado para elegir certificado y colocar la firma.
- Activar y quitar desde «Menú del botón derecho…» en FirmaPDF. Sin administrador y sin cambiar el lector habitual.
- En Windows 11: botón derecho → Mostrar más opciones → Firmar con FirmaPDF.
- Funciona con espacios y acentos. Conserva la carpeta; si la mueves, vuelve a activar el menú desde la nueva ubicación.
- La apertura trabaja en una copia y no firma automáticamente.

FirmaPDF Windows 0.3.2: fondo transparente y tamaño del texto configurable.

- Texto de 4 a 18 pt, con vista previa; 8 pt por defecto y reducción para caber sin recortar.
- Imagen con más espacio: la mitad del ancho útil cuando hay texto.
- Sin fondo ni marco por defecto. Marco opcional sin relleno.
- Migración del diseño anterior: conserva texto, imagen y sello; elimina el fondo y guarda el tamaño elegido.
- La transparencia de la imagen depende del archivo: usa PNG con canal alfa si tiene fondo blanco.

Windows 0.3 en C#/.NET y WPF: diseño renovado, apariencia personalizada y sello de tiempo.

- Abrir PDF y elegir certificado del almacén de Windows o archivo `.p12/.pfx`.
- **Personalizar texto e imagen** con las variables `{nombre}`, `{fecha}` y `{emisor}`. PNG/JPEG, transparencia, proporción conservada y fondo transparente y marco opcional.
- El diseño se guarda en `%LOCALAPPDATA%\FirmaVisible\settings.json` para próximas sesiones, sin certificado, contraseña ni clave privada.
- **Sello de tiempo** RFC3161 de DigiCert, opcional y con Internet. Envía solo la huella SHA-256 de la firma y el nonce; nunca el PDF ni tus claves. Si falla, no se guarda una copia sin el sello solicitado.
- Hora acreditada en la confirmación final y en el panel de firmas del visor. La fecha impresa sigue siendo la del ordenador.
- Vista previa, zoom/ajuste, colocar/mover/redimensionar firma, conservación de firmas anteriores y botón para abrir la última copia.

Descarga y extrae `FirmaPDF-Windows-NET.zip`. Conserva toda la carpeta y abre `FirmaPDF.exe`. Incluye .NET y no necesita Java. Para desarrollo local: `scripts/Build-Windows.ps1`.

**Esta descarga sigue sin firma Authenticode de editor. SmartScreen puede bloquearla.** La vía preparada para distribución desde Microsoft Store sigue pendiente de cuenta verificada y aprobación. No se desactiva SmartScreen ni se instalan certificados de confianza. [Pasos de distribución](https://github.com/eduardocvk/firmar_certificado/blob/main/windows/DISTRIBUCION.md).

El sello comprueba nonce, impronta, firma/EKU y cadena hasta DigiCert G4 incorporada. No se comprueba revocación ni cualificación del servicio y no se incorporan evidencias LT/LTA. El endpoint oficial de DigiCert usa HTTP; la autenticidad se verifica sobre el token firmado. Los documentos y las claves permanecen en el equipo.

Preversión bajo AGPL-3.0-or-later por iText Community; fuente completa en el repositorio y licencias incluidas. Pruebas con identidades sintéticas, verificación independiente de CMS y sello real con OpenSSL y renderizador nativo de Windows. Pendiente comprobación manual de esta versión con el FNMT real, Adobe Reader y el destinatario previsto.
