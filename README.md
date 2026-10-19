# FirmaPDF

Aplicaciones locales para **Windows 10/11 x64** y **Android 8 o posterior** que firman PDF con certificado digital y permiten elegir la posición de su apariencia visible.

**Windows 0.3.4 en C#/.NET; Android 0.2.2. Versiones de pruebas.** Implementa firma con certificado y comprobación criptográfica, no una certificación jurídica del documento. El usuario confirmó que Android firma correctamente con su certificado en la versión anterior; las nuevas funciones y la versión Windows necesitan su comprobación manual con FNMT. Conviene empezar con un PDF de prueba y comprobar el resultado en Adobe Reader y en el sistema al que se enviará.

El nombre anterior era FirmaVisible. Se mantienen el identificador Android y la carpeta de preferencias Windows para conservar los diseños guardados. Los paquetes de versiones anteriores conservan sus nombres originales.

## Descargar y utilizar

La preversión publicada se descarga desde [Releases](https://github.com/eduardocvk/firmar_certificado/releases). Incluye el APK, el ZIP de Windows y sus sumas SHA-256. Solo se publica cuando las comprobaciones del mismo commit terminan correctamente.

También puedes abrir una ejecución correcta en [Actions → Compilar y comprobar](https://github.com/eduardocvk/firmar_certificado/actions/workflows/build.yml). Sus artefactos contienen:

| Artefacto | Uso |
|---|---|
| `FirmaPDF-Windows-NET` | Extrae el ZIP y conserva toda la carpeta `FirmaPDF`; ejecuta `FirmaPDF.exe`. Incluye .NET, sin Java. No requiere permisos de administrador. |
| `FirmaPDF-Android-pruebas` | Extrae el APK y ábrelo en Android. Si Android lo solicita, permite instalar aplicaciones desde la aplicación con la que hayas abierto el APK. |

Los artefactos de Actions requieren iniciar sesión en GitHub y caducan según su política de conservación. El APK es una **compilación de depuración para pruebas**, firmada con una clave de desarrollo, distinta del certificado usado para firmar documentos. Entre compilaciones de CI puede ser necesario desinstalar la anterior para instalar la nueva. No es una distribución de producción ni está publicada en Google Play.

**SmartScreen:** la descarga de Windows está sin firma de editor y puede bloquearse. C# no elimina ese aviso. La vía preparada para distribución confiable es Microsoft Store mediante MSIX, pendiente de cuenta verificada y aprobación. Consulta [distribución y firma de código](windows/DISTRIBUCION.md). No se modifica la seguridad de Windows.

### Windows

1. Abre el PDF. La aplicación trabaja sobre una copia temporal del archivo abierto.
2. Pulsa **Certificados de Windows** para usar un certificado instalado con su clave privada, o **Importar .p12 / .pfx** para abrir una copia protegida con contraseña. Un archivo `.cer` sin clave privada no sirve para firmar.
3. Selecciona la página. Arrastra sobre ella para crear el recuadro; arrastra su interior para moverlo y su esquina inferior derecha para cambiar su tamaño.
4. Personaliza texto e imagen, ajusta el texto entre 4 y 18 pt, guarda tu diseño y activa o desactiva **Sello de tiempo**. El diseño y esa preferencia permanecen en el perfil de Windows, sin certificado ni contraseña.
5. En **Opciones de firma** puedes desactivar la confirmación y el aviso de éxito. La última identidad utilizada se preselecciona al iniciar si está instalada y disponible en Windows; **Olvidar** elimina ese recuerdo.
6. Pulsa **Firmar y guardar copia**, elige un nombre nuevo y autoriza la identidad y el sello.
7. Abre la copia firmada en Adobe Reader y consulta el panel de firmas.

Para abrir un documento desde el Explorador: en FirmaPDF pulsa **Menú del botón derecho… → Activar menú** una vez. Después utiliza **botón derecho en el PDF → Firmar con FirmaPDF**; en Windows 11, dentro de **Mostrar más opciones**. No cambia tu lector habitual; puedes quitar el menú desde la misma ventana.

### Android

1. Abre un PDF desde la aplicación, o utiliza **Compartir / Abrir con → FirmaPDF** desde otra aplicación.
2. En **Certificado**, elige una identidad del almacén de Android o abre tu `.p12/.pfx` con su contraseña. La importación directa solo dura la sesión; no guarda el certificado en disco. Si ya tienes instalado el certificado utilizado por AutoFirma, comprueba si aparece en el selector de Android. Si no aparece, puedes abrir tu copia `.p12/.pfx`.
3. Pulsa **Personalizar firma** para guardar texto e imagen. Usa `{nombre}`, `{fecha}` y `{emisor}` para completarlos con el certificado elegido. Elige PNG/JPEG, ajusta el texto entre 4 y 18 pt y utiliza fondo transparente con marco opcional. El diseño se conserva en este móvil.
4. Activa **Sello de tiempo** para solicitar RFC3161 a DigiCert (requiere Internet); desactívalo para firmar sin conexión. Solo se envía una huella de la firma. Si el servicio falla, no se guarda una firma sin el sello solicitado.
5. Elige la página y dibuja el recuadro. Puedes moverlo, ajustar su esquina, ampliar con un pellizco y desplazar con dos dedos. **Ajustar** restablece el zoom.
6. Pulsa **Firmar y guardar copia** y confirma la identidad y el uso del sello.
7. Guarda el archivo con el selector de Android o compártelo. El menú **•••** permite guardar o compartir el último resultado y comprobar el PDF abierto.

La firma tiene fondo transparente; el marco es opcional y la imagen ocupa la mitad del ancho útil cuando hay texto. El tamaño del texto se guarda con el diseño (8 pt por defecto); se reduce si hace falta para caber. Un fondo blanco que forme parte de la imagen no se elimina: utiliza PNG con transparencia.

La previsualización de texto es orientativa; el recuadro se coloca en las coordenadas seleccionadas. El PDF final contiene nombre del certificado y fecha con zona horaria. El mínimo de recuadro es 100 × 42 puntos PDF. Android limita la apertura a 100 MiB y el certificado importado a 4 MiB. Windows y Android admiten imagen personalizada y texto de hasta 600 caracteres. La imagen se limita a 1200 píxeles de lado y se normaliza sin metadatos. Si el texto no cabe de forma legible, se pide ampliar el recuadro o acortarlo; no se trunca silenciosamente.

Algunos visores móviles omiten campos de firma. FirmaPDF prepara una copia exclusivamente de visualización para mostrar sus apariencias sin cambiar el original firmado. Para revisar el archivo exportado y sus firmas, utiliza un visor que las soporte, como Adobe Reader.

## Qué firma genera y qué comprueba

- Firma PDF con `/SubFilter /ETSI.CAdES.detached`, CMS separado y atributo `SigningCertificateV2` (SHA-256), diseñada para el perfil **PAdES Baseline B**. No se afirma certificación de conformidad por un laboratorio externo.
- RSA de al menos 2048 bits o EC de al menos 256 bits, con SHA-256. Comprueba fechas del certificado y uso de clave para firma antes de utilizarlo.
- La apariencia forma parte de la revisión firmada; no es una imagen pegada después de firmar.
- Guarda incrementalmente, comprueba las firmas criptográficas existentes y verifica el resultado antes de publicarlo como copia terminada. Puede añadir otra firma a PDF firmados ordinarios sin cambios posteriores.
- No sobrescribe el original ni un archivo de salida existente en el motor de firma.
- Rechaza PDF cifrados, formularios XFA, documentos certificados con DocMDP y campos con bloqueo de firma. Rechaza añadir firmas si existen cambios posteriores a la última firma.

La comprobación de la aplicación verifica la **integridad de las revisiones firmadas y sus firmas CMS**. **No comprueba la cadena hasta una raíz de confianza, la revocación mediante OCSP/CRL, listas de confianza europeas ni la cualificación eIDAS. Windows y Android incorporan opcionalmente un sello RFC3161; no se añaden datos LT/LTA.** La fecha visible procede del reloj del dispositivo, incluso con sello: la hora acreditada por la TSA queda en el token y en la confirmación final. El estado de confianza debe comprobarse en un validador que disponga de la política y servicios correspondientes. Un certificado caducado en el futuro requiere evidencias temporales adecuadas para demostrar las circunstancias históricas de la firma.

Usar un certificado FNMT en un archivo `.p12/.pfx` no convierte por sí solo la firma en cualificada. La categoría jurídica depende también del certificado, el dispositivo de creación y el proceso. La aceptación de un trámite depende de sus requisitos. Véanse [eIDAS](https://eur-lex.europa.eu/eli/reg/2014/910/) y [documentación europea de PAdES/DSS](https://ec.europa.eu/digital-building-blocks/DSS/webapp-demo/doc/dss-documentation.html).

## Privacidad

No hay servidor propio, cuentas, analítica ni subida de PDF, claves o contraseñas. Ambas apps utilizan Internet únicamente para el sello opcional de DigiCert. Android desactiva las copias de seguridad. El sello envía la huella SHA-256 de la firma y el nonce, nunca el PDF, el certificado del usuario, claves ni contraseñas. El servicio conoce la IP de conexión. El endpoint oficial usa HTTP (en Android limitado por configuración de red a `timestamp.digicert.com`); el token se autentica mediante su firma y cadena a DigiCert G4 incorporada. Se comprueban nonce, huella, EKU y cadena, sin revocación ni declaración de sello cualificado. [Detalles](docs/ANDROID_0_2.md). En Windows se utilizan handles de claves del almacén del sistema; en Android, handles del `KeyChain` cuando se escoge esa vía. El sistema decide las protecciones de cada clave: importar un certificado no garantiza almacenamiento en hardware.

Los certificados abiertos directamente permanecen en memoria durante la sesión y se pueden olvidar desde la interfaz. Las contraseñas se limpian de los buffers controlados por la app después de importar. Esto no garantiza el borrado de todas las copias internas de las bibliotecas o de la máquina virtual. Los PDF abiertos se copian a una carpeta temporal; Android conserva resultados firmados en la caché para permitir compartirlos. Puedes eliminar la caché desde los ajustes de Android. Guarda la copia final antes de cerrar la sesión. No subas tu certificado al repositorio ni lo incluyas en incidencias.

## Desarrollo y compilación

Windows: SDK .NET 10. Android y motor Java: JDK 17. Para Android: SDK 35 y Build Tools 35.0.0, con `ANDROID_HOME` configurado. No hace falta Flutter. El wrapper descarga Gradle 8.13.

```powershell
# Windows: probar el motor C# y crear la aplicación con .NET incluido
dotnet run --project windows/FirmaVisible.Tests -c Release -- artifacts/windows-tests
.\scripts\Build-Windows.ps1

# Android: APK de pruebas, APK de instrumentación y lint
.\gradlew.bat -PwithAndroid=true :android:assembleDebug :android:assembleAndroidTest :android:lintDebug

# Android: con un emulador o dispositivo conectado
.\gradlew.bat -PwithAndroid=true :android:connectedDebugAndroidTest

# Comprobación CMS independiente con OpenSSL (PowerShell 7)
.\scripts\verify-openssl.ps1
```

En Linux/macOS utiliza `bash ./gradlew`. Android no se incluye en Gradle hasta pasar `-PwithAndroid=true`. Windows se compila con .NET de forma independiente. Resultados: `artifacts/windows/FirmaPDF/` y `android/build/outputs/apk/debug/android-debug.apk`.

El flujo de CI compila Windows y Android, ejecuta las pruebas del motor, verifica CMS con OpenSSL y prueba la firma y su apariencia con el renderizador nativo en un emulador Android. Una compilación correcta no sustituye la prueba de extremo a extremo con FNMT, Adobe y el destinatario real.

## Arquitectura y siguientes pasos

- `core`: identidades, importación PKCS#12, CMS y geometría normalizada.
- `pdf`: motor PDF y pruebas de integración en JVM.
- `windows`: aplicación C#/.NET 10 y WPF, renderizador nativo Windows.Data.Pdf, iText y claves del almacén de Windows o PKCS#12. [Compilación y licencias](windows/README.md).
- `android`: interfaz nativa, renderizador PDF de Android, KeyChain y selector de documentos. El motor PDF se genera desde la misma fuente, cambiando exclusivamente el namespace de PDFBox.
- [Estado y validación](docs/VALIDACION.md), [decisiones técnicas](docs/ARQUITECTURA.md).

Antes de distribuir una versión de producción: completar pruebas reales FNMT y de interacción en dispositivos, revisar y actualizar el motor PDF de Android, evaluar prestador/sello cualificado si el trámite lo exige, validación PKI/OCSP/CRL y evidencias LT, endurecer el tratamiento de PDF no confiables y configurar una clave estable para firmar las entregas Android. Una integración de firma remota cualificada sería un proyecto adicional con un prestador cualificado.

## Licencias

Windows C# (`windows/`): **AGPL-3.0-or-later**, con iText Community y sus avisos. Código Java y Android: Apache-2.0. Bouncy Castle: licencia permisiva; .NET: licencias incluidas. La licencia Apache de la raíz no sustituye la licencia AGPL del módulo Windows. Consulta [THIRD_PARTY.md](THIRD_PARTY.md) y [windows/LICENSE.txt](windows/LICENSE.txt).
