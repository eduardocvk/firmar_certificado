# Dependencias principales

| Componente | Versión | Licencia y fuente |
|---|---|---|
| Apache PDFBox | 2.0.36 | [Apache-2.0](https://pdfbox.apache.org/) |
| PdfBox-Android | 2.0.27.0 | [Apache-2.0](https://github.com/TomRoush/PdfBox-Android) |
| Bouncy Castle bcpkix / bcprov / bcutil | 1.86 | [Bouncy Castle licence](https://www.bouncycastle.org/licence.html) |
| AndroidX Core | 1.15.0 | [Apache-2.0](https://developer.android.com/jetpack/androidx) |
| Eclipse Temurin, runtime Windows | 17 | [GPLv2 con Classpath Exception y licencias de terceros](https://adoptium.net/about) incluidas en el runtime generado |

Las dependencias conservan sus avisos en los JAR y en el runtime de Windows. Se incluyen copias de los avisos de las bibliotecas Android en `android/src/main/assets/licenses`. La configuración de empaquetado excluye metadatos duplicados del JAR, no cambia las licencias.
