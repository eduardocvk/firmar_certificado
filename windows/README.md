# FirmaPDF para Windows — C#/.NET y WPF

Versión 0.3. Motor e interfaz completamente .NET: no se ejecuta Java ni se invoca el antiguo firmador Java desde un proceso auxiliar. Visor `Windows.Data.Pdf`, claves del usuario mediante las APIs criptográficas de .NET e iText Community 9.8 para firma incremental de PDF.

## Compilar y probar

Requiere Windows x64 y SDK .NET 10. Desde la raíz:

```powershell
dotnet run --project windows/FirmaVisible.Tests/FirmaVisible.Tests.csproj -c Release -- artifacts/windows-tests
.\scripts\verify-openssl.ps1 -Artifacts artifacts/windows-tests
.\scripts\Build-Windows.ps1
```

Ejecutable local: `artifacts/windows/FirmaPDF/FirmaPDF.exe`. La carpeta contiene el runtime .NET y todas las dependencias. No requiere instalación de Java ni de .NET en el equipo de destino. Conservar toda la carpeta. Los archivos `packages.lock.json` fijan las dependencias; el script de publicación usa restauración bloqueada.

La suite usa identidades efímeras autofirmadas «NO REAL», comprueba RSA y EC, CMS con `SignedCms` de .NET, las cuatro rotaciones con CropBox desplazado, original intacto, segunda firma incremental, certificado caducado, contraseña incorrecta, alteración y revisiones posteriores. El visor nativo de Windows renderiza cada resultado y se comprueba la ubicación visible. También genera una captura de la interfaz WPF sin abrir una ventana interactiva.

## Configuración y firmas guardadas

La rueda junto al título abre **Configuración**. Permite crear hasta 20 diseños, personalizar texto e imagen, renombrarlos o eliminarlos. El diseño anterior se migra como «Mi firma». Las preferencias conservan el certificado recordado y los avisos. También se incluyen el sello, la integración de Explorer y el acceso al código y licencias.

En **03 Tu firma visible** se elige un diseño por documento. **Poner firma** crea un recuadro inicial; también se puede dibujar directamente. Cambiar de página o pestaña conserva el recuadro de la página elegida.

## Pestañas y firma por lotes

El selector permite varios PDF y la ventana acepta archivos arrastrados desde Explorer. Los duplicados se detectan por ruta completa sin distinguir mayúsculas. Se puede cerrar cada pestaña sin afectar a las demás. Las aperturas de la misma copia de la aplicación se transmiten a la ventana existente mediante una tubería local restringida al usuario de Windows.

**Firmar** procesa todos los pendientes. Se requiere un recuadro de al menos 100 × 42 puntos por documento. Se usa el mismo certificado y sello elegido, con un diseño y posición independientes por PDF. El destino automático es `nombre_firmado.pdf` en la carpeta del original. No se sobrescribe ningún archivo y se mantienen los originales.

Antes de iniciar se comprueban recuadros y colisiones. El motor firma y verifica todos los documentos en temporales de sus carpetas de destino; un fallo en esa preparación elimina esos temporales sin publicar copias. Al publicar se utiliza movimiento sin reemplazo. Si aparece un conflicto durante la publicación, se informa de las copias ya guardadas y se conservan; las restantes no se publican. No se afirma atomicidad de varias carpetas frente a cambios externos o errores de disco.

## Abrir desde el botón derecho

En **Configuración → Menú del botón derecho… → Activar menú** se registra «Firmar con FirmaPDF» para los PDF del usuario actual, sin administrador ni cambio del lector habitual. En Windows 11 está en «Mostrar más opciones». Se puede retirar desde esa misma ventana. Si cambia la ubicación de la carpeta, actívalo desde la nueva copia. Los comandos `--register-context-menu` y `--unregister-context-menu` siguen disponibles, y `--open` admite varias rutas PDF. La futura distribución por Store requiere su asociación de archivos en el paquete; el registro actual corresponde al ZIP portable.

## Certificado recordado y avisos

Tras una firma correcta se guarda únicamente la huella SHA-256 del certificado. Al iniciar, se recupera del almacén de Windows si conserva clave privada, está vigente y permite firmar. Si falta, no se sustituye por otro; selecciona uno manualmente. Para un .p12/.pfx que no está en el almacén, sigue siendo necesario abrir el archivo con su contraseña. **Olvidar** elimina la referencia recordada.

En **Configuración → Opciones de firma** puedes desmarcar **Confirmar antes de firmar** y **Mostrar aviso al terminar**, o usar las casillas «No volver a mostrar» de los propios diálogos. Las opciones son independientes y se guardan. El resultado, el archivo y su hora permanecen visibles en la ventana; los errores siguen mostrándose. El destino automático usa el sufijo _firmado y nunca reemplaza archivos existentes.

## Firma y límites

`ETSI.CAdES.detached`, SHA-256, certificado firmante y cadena disponible, `SigningCertificateV2`, apariencia incorporada y guardado incremental. Se comprueban todas las firmas antes y después de añadir otra. Se rechazan PDF cifrados, XFA, DocMDP y campos con bloqueo, o documentos con bytes posteriores a la última firma.

Sello opcional RFC3161 de DigiCert: se comprueban nonce, huella, firma/EKU y cadena con raíz DigiCert G4 incorporada. Solo se envía la huella del valor de firma, nunca PDF, certificados del usuario ni claves. Sin comprobación de revocación, cualificación o evidencias LT. El endpoint oficial es HTTP y se verifica criptográficamente el token; no se confía en el transporte. Si falla el sello solicitado, no se produce un resultado degradado. La hora acreditada se muestra al terminar y en el PDF. La fecha visible procede del dispositivo. Pendiente validación manual con FNMT real, Adobe Reader y el destinatario previsto.

## Firma del programa y distribución

La firma de los PDF y la identidad del editor del ejecutable son independientes. La compilación actual no tiene Authenticode y puede ser bloqueada por Windows al descargarla. Se ha preparado MSIX para Store y un script para certificados Code Signing, pero faltan la identidad verificada y la revisión/publicación externa. [Pasos y alternativas](DISTRIBUCION.md).

## Licencia

El código y la aplicación de `windows/` se ofrecen bajo **AGPL-3.0-or-later**, al integrar iText Community. Fuente completa y licencia incluidas; se conservan Producer y avisos de iText en la salida. Consulta `LICENSE.txt`, `ITEXT-LICENSE.md`, `THIRD_PARTY.txt` y `licenses/`. Los componentes Java/Android del repositorio son independientes y conservan Apache-2.0.

Para probar el sello real con una identidad sintética, define FIRMAVISIBLE_LIVE_TSA=true antes de ejecutar las pruebas y después ejecuta scripts/verify-timestamp.ps1 -Artifacts artifacts/windows-tests. No se comprueba la revocación. El CI ejecuta esta comprobación y OpenSSL exige que se rechace una alteración.

La selección múltiple del menú de Explorer utiliza el modelo Document y sus límites de Windows. Para lotes mayores, usa el selector múltiple o arrastra los PDF a la ventana. Referencia: https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers#employing-the-verb-selection-model
