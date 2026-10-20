# Política de privacidad de FirmaPDF para Windows

Actualizada: 20 de octubre de 2026. Editor: Eduardo Callejo.

FirmaPDF permite firmar archivos PDF con un certificado digital en el ordenador del usuario. Esta política describe la versión Windows del proyecto público [FirmaPDF](https://github.com/eduardocvk/firmar_certificado).

También puede generar firmas separadas `.p7s` para otros archivos. Esas firmas contienen el certificado público y datos de firma; se comparten junto al original para poder comprobarlo. No se modifica el archivo original.

## Documentos y certificados

Los documentos se abren, representan, firman y comprueban localmente. FirmaPDF no los envía a un servidor del desarrollador. Las copias firmadas contienen la firma digital, el certificado público del firmante y, si se configura, la apariencia visible y el sello de tiempo. Quien reciba esas copias puede consultar sus datos de firma y certificado público.

La clave privada se utiliza mediante los mecanismos criptográficos de Windows o a partir del archivo .p12/.pfx que el usuario abre. FirmaPDF no envía esa clave ni la contraseña del certificado. Tampoco guarda la contraseña en sus preferencias. Las identidades cargadas se mantienen en memoria durante la sesión; las del almacén de Windows permanecen gestionadas por Windows.

## Preferencias y archivos locales

Se guardan localmente los diseños de firma, sus textos e imágenes, las opciones elegidas y la huella pública del último certificado utilizado. Estos diseños pueden contener datos personales que el propio usuario haya escrito. No se guarda una copia del certificado privado en las preferencias.

La aplicación crea archivos temporales para representar y preparar los PDF. Intenta eliminarlos al cerrar la sesión o al terminar una operación fallida. Un cierre inesperado o un fallo del sistema puede dejar temporales en el ordenador. Los archivos originales y las copias que el usuario decide guardar permanecen en su ubicación. El borrado de archivos no equivale a un borrado seguro del soporte.

## Sello de tiempo opcional

Si se activa el sello de tiempo, FirmaPDF conecta con el servicio de DigiCert para solicitar un token RFC 3161. Envía la huella criptográfica del valor de firma, un número aleatorio y los parámetros técnicos de la petición. No transmite el PDF completo, el certificado del usuario, su clave privada ni su contraseña.

Como en toda conexión de red, el proveedor y la infraestructura de red pueden conocer la dirección IP y los datos técnicos de conexión. DigiCert puede procesarlos conforme a [su política de privacidad](https://www.digicert.com/privacy-policy). La aplicación comprueba criptográficamente el token recibido. El sello requiere Internet; puede desactivarse en Configuración para firmar sin esa conexión.

## Publicidad, seguimiento y servicios externos

Al pulsar **Comprobar certificado** o **Comprobar firmas**, Windows construye la cadena de certificados y puede consultar servidores de las autoridades emisoras para obtener certificados intermedios, listas de revocación (CRL) o respuestas OCSP. Estas consultas pueden enviar datos públicos del certificado, por ejemplo su emisor y número de serie, y exponen datos técnicos de conexión como la dirección IP. No envían los documentos, contraseñas ni claves privadas. Windows puede utilizar resultados de su caché. La comprobación se realiza a fecha actual; no implica validación histórica o cualificación de la firma. Firmar sin sello no activa por sí mismo esas consultas de revocación: se solicitan con los botones de comprobación.

FirmaPDF no incorpora publicidad, cuentas de usuario propias ni herramientas de analítica o telemetría del desarrollador. Los enlaces de ayuda, código fuente, licencias y privacidad se abren en el navegador cuando el usuario los elige; esos sitios aplican sus propias políticas.

La instalación, actualizaciones, diagnósticos y otros servicios ofrecidos por Microsoft Store o Windows son independientes de la aplicación y están sujetos a las opciones y políticas de Microsoft.

## Control y contacto

Los diseños pueden editarse o eliminarse desde Configuración, conservando al menos uno. «Olvidar» elimina la referencia al último certificado dentro de FirmaPDF, sin borrar certificados del almacén de Windows. Se pueden eliminar las preferencias locales para restablecer la configuración; las copias firmadas que ya se hayan creado no se modifican.

Para comunicar problemas de privacidad, utiliza [las incidencias del proyecto](https://github.com/eduardocvk/firmar_certificado/issues). Ese canal es público: no adjuntes documentos personales, archivos de certificado, claves privadas ni contraseñas.
