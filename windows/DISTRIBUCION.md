# Cómo resolver «Editor desconocido»

Cambiar de Java a C# no aporta una identidad de editor. La preversión actual de GitHub no tiene firma Authenticode. El propietario ha facilitado su identidad de Partner Center y ya está incorporada al manifiesto Store, pero eso no firma los ejecutables ni sustituye la certificación. **No se ha eliminado el aviso de SmartScreen de la descarga ZIP.** Los campos Company/Authors de .NET son metadatos del programa, no una firma ni una identidad verificada.

## Vía recomendada: Microsoft Store con MSIX

La Store firma los paquetes MSIX después de la certificación. No hace falta comprar ni gestionar un certificado para esta vía. El proceso tiene verificación de identidad y revisión de la aplicación. No equivale a subir el EXE sin firmar a la Store: la entrega debe hacerse como MSIX.

1. Entra por [storedeveloper.microsoft.com](https://storedeveloper.microsoft.com/) para el nuevo registro gratuito y crea una cuenta con el tipo adecuado al uso: particular para proyectos personales no relacionados con tu negocio/profesión, o empresa para los casos indicados por Microsoft. Los dos tipos tienen registro gratuito en el nuevo flujo; otras rutas pueden mostrar el antiguo con pago. Consulta las guías oficiales de [particulares](https://learn.microsoft.com/es-es/windows/apps/publish/whats-new-individual-developer) y [empresas](https://learn.microsoft.com/es-es/windows/apps/publish/whats-new-company-developer).
2. Completa la verificación que te pida Microsoft y reserva el nombre de la aplicación.
3. En Partner Center, abre la identidad de la aplicación. Copia `Package/Identity/Name`, `Package/Identity/Publisher` y `Package/Properties/PublisherDisplayName`. Son datos de empaquetado, no contraseñas ni claves privadas.
4. Con Windows SDK y .NET SDK 10 instalados, compila y crea el paquete desde la raíz del repositorio:

```powershell
.\scripts\Build-Windows.ps1
.\scripts\New-StorePackage.ps1 `
  -IdentityName 'VALOR_EXACTO_DE_PARTNER_CENTER' `
  -Publisher 'CN=VALOR_EXACTO_DE_PARTNER_CENTER' `
  -PublisherDisplayName 'NOMBRE_EXACTO_DE_PARTNER_CENTER'
```

5. Envía el MSIX de `artifacts/store/` a la ficha de Partner Center. Completa descripción, privacidad, capturas, clasificación y los requisitos de Microsoft. La aplicación usa ejecución de escritorio de plena confianza (`runFullTrust`) para el visor y las claves del usuario.
6. Una vez aprobado y publicado, instala desde Microsoft Store. Microsoft firma esa entrega. **El MSIX previo a la Store está sin firmar y no debe presentarse como instalable ni como solución directa a SmartScreen.**

El CI prueba la estructura de empaquetado con una identidad ficticia marcada `ValidationOnly`. No publica ese paquete ni crea cuentas en nombre del usuario.

Desde el 20 de octubre de 2026, el manifiesto contiene los valores reales facilitados por el propietario y el script puede ejecutarse sin repetirlos. [Guía de envío de FirmaPDF](Store/ENVIO.md). El MSIX empieza en `1.0.0.0`, porque Store exige un primer componente mayor que cero y reserva el cuarto para su uso; no se reutiliza la versión `0.4.0.0` de las pruebas anteriores.

Fuentes: [opciones de firma de Microsoft](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options), [registro](https://developer.microsoft.com/es-es/microsoft-store/register).

## Probar en tu ordenador mediante compilación local

Para desarrollar y probar no es necesario descargar el EXE de GitHub: este repositorio permite compilar desde su código. En el ordenador en el que se está desarrollando ya se ha generado una copia local en `artifacts/windows/FirmaPDF/FirmaPDF.exe`.

En otro equipo: instala el [SDK .NET 10 oficial](https://dotnet.microsoft.com/download/dotnet/10.0), obtiene el código del repositorio con Git y ejecuta `scripts/Build-Windows.ps1`. Conserva toda la carpeta de salida al ejecutar la app. Incluye .NET y no necesita Java.

La compilación local **no añade un editor verificado ni garantiza que una política de control de aplicaciones permita ejecutarla**. No modifica SmartScreen, Smart App Control ni la confianza de Windows. Un administrador puede tener que autorizar el programa en equipos gestionados. En la captura del usuario no aparecía «Ejecutar de todas formas»; es compatible con una configuración que impide omitir el aviso, pero la captura no permite identificar por sí sola la política exacta.

## Descarga pública firmada fuera de Store

Hace falta obtener una identidad de firma de código de un proveedor que Windows reconozca. Se debe contratar el producto **Code Signing**, con validación de identidad, clave privada protegida y sello de tiempo. Un certificado de PDF no se convierte automáticamente en certificado de firma de código.

Microsoft indica que Artifact Signing está disponible para organizaciones de la UE, pero los particulares siguen limitados a EE. UU. y Canadá. Para un particular de España se puede estudiar una autoridad de certificación reconocida que admita su tipo de solicitante. Verifica elegibilidad y precio directamente con el proveedor antes de contratar. Los proyectos abiertos que cumplan sus condiciones también pueden solicitar [SignPath Foundation](https://signpath.org/); la admisión no está garantizada.

Cuando el certificado y su proveedor estén instalados en el almacén del usuario, el script preparado es:

```powershell
.\scripts\Sign-Windows.ps1 `
  -Thumbprint 'HUELLA_SHA1_DEL_CERTIFICADO_DE_FIRMA_DE_CODIGO' `
  -TimestampUrl 'URL_RFC3161_DEL_PROVEEDOR'
```

El script exige uso de clave `Code Signing`, clave privada, vigencia y cadena validada por Windows; firma los binarios propios con SignTool y exige firma válida y sello de tiempo. No crea certificados de confianza, no exporta claves ni pide contraseñas en la línea de comandos. Para servicios de firma en la nube se necesita integrar su método de autenticación/proveedor concreto.

**Firma válida y ausencia de todos los avisos son cosas distintas:** Microsoft señala que un archivo nuevo firmado puede seguir recibiendo avisos mientras obtiene reputación. Un certificado EV tampoco garantiza evitar SmartScreen desde la primera descarga. [Reputación de SmartScreen](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).

## Uso interno administrado

Si solo se distribuye dentro de una organización, su administrador puede establecer confianza de firma y reglas de aplicación mediante Intune o directivas. Es una decisión y configuración de esa organización, no confianza pública del editor. El proyecto no cambia automáticamente esas reglas ni instala raíces o certificados de editor en ningún equipo.

## Alternativa de producto: aplicación web/PWA

Una web instalable desde el navegador evita distribuir un ejecutable Windows. Requeriría otra implementación del firmador y cargar la copia `.p12/.pfx` localmente, en lugar de usar directamente el almacén de Windows. La versión entregada aquí es C# de escritorio; no se presenta como una PWA ni usa un servidor custodio de certificados.
