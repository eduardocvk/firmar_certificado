# FirmaVisible

Aplicaciones locales para **Windows 10/11 x64** y **Android 8 o posterior** que firman PDF con certificado digital y permiten elegir la posición de su apariencia visible.

**Versión 0.1.0 de pruebas.** Implementa firma con certificado y comprobación criptográfica, no una certificación jurídica del documento. No se ha probado con el certificado FNMT real del propietario del proyecto. Conviene empezar con un PDF de prueba y comprobar el resultado en Adobe Reader y en el sistema al que se enviará.

## Descargar y utilizar

La preversión publicada se descarga desde [Releases](https://github.com/eduardocvk/firmar_certificado/releases). Incluye el APK, el ZIP de Windows y sus sumas SHA-256. Solo se publica cuando las comprobaciones del mismo commit terminan correctamente.

También puedes abrir una ejecución correcta en [Actions → Compilar y comprobar](https://github.com/eduardocvk/firmar_certificado/actions/workflows/build.yml). Sus artefactos contienen:

| Artefacto | Uso |
|---|---|
| `FirmaVisible-Windows` | Extrae el ZIP descargado y el ZIP interior; conserva toda la carpeta `FirmaVisible` y ejecuta `FirmaVisible.exe`. Incluye Java. No requiere permisos de administrador. |
| `FirmaVisible-Android-pruebas` | Extrae el APK y ábrelo en Android. Si Android lo solicita, permite instalar aplicaciones desde la aplicación con la que hayas abierto el APK. |

Los artefactos de Actions requieren iniciar sesión en GitHub y caducan según su política de conservación. El APK es una **compilación de depuración para pruebas**, firmada con una clave de desarrollo, distinta del certificado usado para firmar documentos. Entre compilaciones de CI puede ser necesario desinstalar la anterior para instalar la nueva. No es una distribución de producción ni está publicada en Google Play.

### Windows

1. Abre el PDF. La aplicación trabaja sobre una copia temporal del archivo abierto.
2. Pulsa **Certificados de Windows** para usar un certificado instalado con su clave privada, o **Importar .p12 / .pfx** para abrir una copia protegida con contraseña. Un archivo `.cer` sin clave privada no sirve para firmar.
3. Selecciona la página. Arrastra sobre ella para crear el recuadro; arrastra su interior para moverlo y su esquina inferior derecha para cambiar su tamaño.
4. Pulsa **Firmar y guardar copia**, elige un nombre nuevo y autoriza el uso del certificado.
5. Abre la copia firmada en Adobe Reader y consulta el panel de firmas.

### Android

1. Abre un PDF desde la aplicación, o utiliza **Compartir / Abrir con → FirmaVisible** desde otra aplicación.
2. En **Certificado**, elige una identidad del almacén de Android o abre tu `.p12/.pfx` con su contraseña. La importación directa solo dura la sesión; no guarda el certificado en disco. Si ya tienes instalado el certificado utilizado por AutoFirma, comprueba si aparece en el selector de Android. Si no aparece, puedes abrir tu copia `.p12/.pfx`.
3. Elige la página y dibuja el recuadro. Puedes moverlo, ajustar su esquina, ampliar con un pellizco y desplazar con dos dedos. **Ajustar** restablece el zoom.
4. Pulsa **Firmar y guardar copia** y confirma la identidad.
5. Guarda el archivo con el selector de Android o compártelo. Los botones **Guardar última copia** y **Compartir última copia** recuperan el último resultado durante esa sesión.

La previsualización de texto es orientativa; el recuadro se coloca en las coordenadas seleccionadas. El PDF final contiene nombre del certificado y fecha con zona horaria. El mínimo de recuadro es 100 × 42 puntos PDF. Android limita la apertura a 100 MiB y el certificado importado a 4 MiB. Por ahora no se incluye imagen manuscrita personalizada.

Algunos visores móviles omiten campos de firma. FirmaVisible prepara una copia exclusivamente de visualización para mostrar sus apariencias sin cambiar el original firmado. Para revisar el archivo exportado y sus firmas, utiliza un visor que las soporte, como Adobe Reader.

## Qué firma genera y qué comprueba

- Firma PDF con `/SubFilter /ETSI.CAdES.detached`, CMS separado y atributo `SigningCertificateV2` (SHA-256), diseñada para el perfil **PAdES Baseline B**. No se afirma certificación de conformidad por un laboratorio externo.
- RSA de al menos 2048 bits o EC de al menos 256 bits, con SHA-256. Comprueba fechas del certificado y uso de clave para firma antes de utilizarlo.
- La apariencia forma parte de la revisión firmada; no es una imagen pegada después de firmar.
- Guarda incrementalmente, comprueba las firmas criptográficas existentes y verifica el resultado antes de publicarlo como copia terminada. Puede añadir otra firma a PDF firmados ordinarios sin cambios posteriores.
- No sobrescribe el original ni un archivo de salida existente en el motor de firma.
- Rechaza PDF cifrados, formularios XFA, documentos certificados con DocMDP y campos con bloqueo de firma. Rechaza añadir firmas si existen cambios posteriores a la última firma.

La comprobación de la aplicación verifica la **integridad de las revisiones firmadas y sus firmas CMS**. **No comprueba la cadena hasta una raíz de confianza, la revocación mediante OCSP/CRL, listas de confianza europeas ni la cualificación eIDAS. No añade sello de tiempo ni datos LT/LTA.** La fecha visible procede del reloj del dispositivo. El estado de confianza debe comprobarse en un validador que disponga de la política y servicios correspondientes. Un certificado caducado en el futuro requiere evidencias temporales adecuadas para demostrar las circunstancias históricas de la firma.

Usar un certificado FNMT en un archivo `.p12/.pfx` no convierte por sí solo la firma en cualificada. La categoría jurídica depende también del certificado, el dispositivo de creación y el proceso. La aceptación de un trámite depende de sus requisitos. Véanse [eIDAS](https://eur-lex.europa.eu/eli/reg/2014/910/) y [documentación europea de PAdES/DSS](https://ec.europa.eu/digital-building-blocks/DSS/webapp-demo/doc/dss-documentation.html).

## Privacidad

No hay servidor, cuentas, analítica ni subida de PDF, claves o contraseñas. Android no solicita permiso de Internet y desactiva las copias de seguridad de la aplicación. En Windows se utilizan handles de claves del almacén del sistema; en Android, handles del `KeyChain` cuando se escoge esa vía. El sistema decide las protecciones de cada clave: importar un certificado no garantiza almacenamiento en hardware.

Los certificados abiertos directamente permanecen en memoria durante la sesión y se pueden olvidar desde la interfaz. Las contraseñas se limpian de los buffers controlados por la app después de importar. Esto no garantiza el borrado de todas las copias internas de las bibliotecas o de la máquina virtual. Los PDF abiertos se copian a una carpeta temporal; Android conserva resultados firmados en la caché para permitir compartirlos. Puedes eliminar la caché desde los ajustes de Android. Guarda la copia final antes de cerrar la sesión. No subas tu certificado al repositorio ni lo incluyas en incidencias.

## Desarrollo y compilación

Requisitos: JDK 17. Para Android: SDK 35 y Build Tools 35.0.0, con `ANDROID_HOME` configurado. No hace falta Flutter. El wrapper descarga Gradle 8.13.

```powershell
# Windows: probar el motor y crear la aplicación portable con Java incluido
.\gradlew.bat :pdf:test :desktop:packageWindows

# Android: APK de pruebas, APK de instrumentación y lint
.\gradlew.bat -PwithAndroid=true :android:assembleDebug :android:assembleAndroidTest :android:lintDebug

# Android: con un emulador o dispositivo conectado
.\gradlew.bat -PwithAndroid=true :android:connectedDebugAndroidTest

# Comprobación CMS independiente con OpenSSL (PowerShell 7)
.\scripts\verify-openssl.ps1
```

En Linux/macOS utiliza `bash ./gradlew`. Si solo desarrollas el motor o Windows y no tienes SDK, Android no se incluye hasta pasar `-PwithAndroid=true`. Resultados: `desktop/build/windows/FirmaVisible/` y `android/build/outputs/apk/debug/android-debug.apk`.

El flujo de CI compila Windows y Android, ejecuta las pruebas del motor, verifica CMS con OpenSSL y prueba la firma y su apariencia con el renderizador nativo en un emulador Android. Una compilación correcta no sustituye la prueba de extremo a extremo con FNMT, Adobe y el destinatario real.

## Arquitectura y siguientes pasos

- `core`: identidades, importación PKCS#12, CMS y geometría normalizada.
- `pdf`: motor PDF y pruebas de integración en JVM.
- `desktop`: interfaz Swing y acceso Windows-MY. Se eligió Java para compartir el motor criptográfico sin un puente Flutter adicional.
- `android`: interfaz nativa, renderizador PDF de Android, KeyChain y selector de documentos. El motor PDF se genera desde la misma fuente, cambiando exclusivamente el namespace de PDFBox.
- [Estado y validación](docs/VALIDACION.md), [decisiones técnicas](docs/ARQUITECTURA.md).

Antes de distribuir una versión de producción: completar pruebas reales FNMT y de interacción en dispositivos, revisar y actualizar el motor PDF de Android, añadir sello de tiempo de un prestador elegido, validación PKI/OCSP/CRL y evidencias LT, endurecer el tratamiento de PDF no confiables y configurar una clave estable para firmar las entregas Android. Una integración de firma remota cualificada sería un proyecto adicional con un prestador cualificado.

## Licencias

Código del proyecto: Apache-2.0. PDFBox y su port Android: Apache-2.0; Bouncy Castle: licencia propia permisiva; runtime Eclipse Temurin: sus licencias incluidas en la distribución. Consulta [THIRD_PARTY.md](THIRD_PARTY.md).
