# Dependencias principales

| Componente | Versión | Licencia y fuente |
|---|---|---|
| Apache PDFBox | 2.0.36 | [Apache-2.0](https://pdfbox.apache.org/) |
| PdfBox-Android | 2.0.27.0 | [Apache-2.0](https://github.com/TomRoush/PdfBox-Android) |
| Bouncy Castle bcpkix / bcprov / bcutil | 1.86 | [Bouncy Castle licence](https://www.bouncycastle.org/licence.html) |
| AndroidX Core | 1.15.0 | [Apache-2.0](https://developer.android.com/jetpack/androidx) |
| AndroidX ExifInterface | 1.4.2 | [Apache-2.0](https://developer.android.com/jetpack/androidx/releases/exifinterface) |
| Noto Sans, Android/PDFBox | regular | [SIL Open Font License 1.1](pdf/src/main/resources/fonts/OFL.txt) |
| .NET, runtime Windows | 10 | [MIT y avisos del runtime](https://github.com/dotnet/runtime) |
| iText Community, Windows | 9.8.0 | [AGPLv3 y condiciones adicionales](windows/ITEXT-LICENSE.md) |
| BouncyCastle.Cryptography, Windows | 2.7.0 | [Licencia permisiva](windows/licenses/BouncyCastle.html) |

Las dependencias conservan sus avisos en los JAR y en el runtime de Windows. Se incluyen copias de los avisos de las bibliotecas Android en `android/src/main/assets/licenses`. La configuración de empaquetado excluye metadatos duplicados del JAR, no cambia las licencias.

El módulo Windows es AGPL-3.0-or-later; conserva su fuente pública y los avisos de iText. Sus licencias se incluyen en la carpeta distribuida. Android y Java conservan Apache-2.0. Véase [windows/THIRD_PARTY.txt](windows/THIRD_PARTY.txt).
