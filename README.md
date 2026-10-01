# ExtractX v1.1.0 — Descompresor moderno estilo Windows 11

## Ejecutar
Doble clic en `ExtractX-v1.1.0.exe` (autocontenido, no requiere instalar .NET).

## Motores (doble motor con rescate automático)
- **Nativo SharpCompress** (sin dependencias, funciona offline): ZIP, 7Z, TAR, GZ/TGZ,
  CAB, BZ2, XZ, RAR no sólidos.
- **7-Zip externo** (`redist/7za.exe` incluido; completo `7z.exe` se descarga una vez):
  RAR/RAR5 sólidos y cifrados, ISO, WIM, 7Z con AES, ZIP con AES-256.
- La extracción prueba el nativo y rescata con 7-Zip (o al revés en RAR/ISO con motor local).
  Las rutas se sanean (sin unidades absolutas ni `..`), como WinRAR/7-Zip.

## Modos
- **Modo grande** (sin argumentos): ventana 1080×680 con todas las secciones.
- **Mini extracción**: doble clic a un `.zip/.rar/.7z/...` abre una ventanita (460px) con
  nombre, tipo, tamaño, contraseña, progreso y botones *Extraer aquí / En carpeta*.
  Se cierra sola al terminar y puede saltar al modo completo (botón ⤢).
- **Mini compresión**: clic derecho → *Comprimir con ExtractX* (archivos o carpetas),
  o arrastrar varios archivos sobre el exe. Formato + nivel + contraseña.
- **Pantalla de carga**: logo ExtractX con animación al iniciar, extraer y comprimir
  (ventana grande y minis).

## Compresión
- Formatos de salida: **ZIP, 7Z, TAR, TAR.GZ, GZ** (7Z requiere el motor 7-Zip;
  RAR de salida no existe: es formato propietario de RARLAB).
- Niveles: Sin compresión / Rápida / Normal / Máxima (ZIP/TAR/7Z; `-mx` en 7-Zip).
- Contraseña: 7Z y ZIP vía 7-Zip usan **AES-256**; ZIP nativo usa **ZipCrypto clásico**
  (compatible con WinRAR/7-Zip/Explorador).
- Formato y nivel **predeterminados** configurables (Configuración).

## Actualizaciones (GitHub Releases)
- Comprueba `releases/latest` al iniciar (si está activado) y con botón manual.
- Diálogo con **notas de la versión**, tipo de paquete (parche incremental o completo),
  botones Descargar e instalar / Omitir versión / Más tarde.
- Descarga con progreso y lanza el instalador en silencioso encima.
- Repo configurable en Configuración (`usuario/nombre`, por defecto `bddjf00-cell/ExtractX`).
- Token opcional para repos privados o más peticiones: se pega en Configuración y se
  guarda en `%AppData%\ExtractX\github.token` (nunca en el código).
  > Si pegaste tu token en un chat, **revócalo en GitHub** (Settings → Developer settings
  > → Personal access tokens) y genera uno nuevo.

## Instalador (quita a WinRAR del medio)
`dist/` contiene: `ExtractX-Setup.exe` + `ExtractX-v1.1.0.exe` + `redist/7za.*` (+ runtime .NET).
Asistente propio estilo Fluent oscuro: Bienvenida → Licencia → Destino →
Formatos → Instalando → Listo. Hace:
- Copia a `%LocalAppData%\Programs\ExtractX` (o `Program Files` con admin).
- Accesos directos (Inicio + Escritorio), entrada en *Agregar o quitar programas*.
- Registra `ExtractX.archive`, Capabilities (sale en *Aplicaciones predeterminadas*),
  `Extraer con ExtractX` / `Comprimir con ExtractX` en el clic derecho.
- Desinstalador propio (`Uninstall.exe --uninstall`, restaura al dueño anterior).

> Windows protege la app predeterminada: tras instalar, en el paso Formatos pulsa
> **Abrir Aplicaciones predeterminadas** y elige ExtractX en .zip/.rar/.7z.
> Sin ese paso de Windows, el doble clic seguirá abriendo WinRAR.

Silencioso: `ExtractX-Setup.exe --silent --dir="..." --formats=zip,rar,7z [--all-users] [--no-shortcuts] [--launch]`
Desatender: `Uninstall.exe --uninstall --silent`.

El instalador detecta si falta .NET Desktop Runtime 9 y lo instala solo
(usa `dist/windowsdesktop-runtime-9.0-win-x64.exe` si está al lado, si no lo descarga).

## Web
`docs/index.html` (ábrelo en el navegador): hero, funciones, formatos,
descargas y FAQ. Sin dependencias externas, funciona offline.

## Asociar formatos
Configuración → marca ZIP/RAR/7Z → *Guardar cambios*. Eso escribe la asociación en
HKCU (doble clic abre ExtractX + menú contextual), sin necesidad de administrador.
- Extrae: ZIP, RAR (incluidos sólidos y cifrados vía motor 7-Zip), 7Z, TAR, GZ/TGZ,
  ISO, CAB, BZ2, XZ, WIM.
- Comprime: ZIP, 7Z, TAR, TAR.GZ, GZ (página Extraer → Comprimir, y extracción masiva).
- Contraseñas: detección + diálogo + caja de contraseña + bóveda local (`%AppData%\ExtractX\passwords.json`).
- Verificación de integridad (doble motor), vista previa de .txt, reparar ZIP, benchmark.
- Historial persistente, favoritos, búsqueda, temas Oscuro/Claro, idioma, auto-abrir carpeta.
- Pantallas: bienvenida, carga, contraseña, actualización, completado, error elegante.

## Compilar desde fuente
```
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
dotnet run --project Tests/Tests.csproj -c Release   # 28 pruebas con WinRAR real
```
Salida: `bin\Release\net9.0-windows\win-x64\publish\ExtractX.exe`

## Estructura
- `MainWindow.xaml(.cs)` + `MiniWindow.xaml(.cs)` — UI Fluent oscura.
- `Core/ArchiveService.cs` — fachada listar/extraer/verificar + rescate entre motores.
- `Core/SevenZip.cs` — motor externo 7za/7z (extraer/comprimir/listar/verificar/descarga).
- `Core/CompressService.cs` — compresión nativa ZIP/TAR/TGZ/GZ + niveles.
- `Core/ZipCrypto.cs` — escritor ZIP con contraseña a pulso.
- `Core/UpdateService.cs` — GitHub Releases + token seguro + parches.
- `Core/AppStore.cs` + `Core/Models.cs` — persistencia JSON en `%AppData%\ExtractX`.
- `Tests/` — batería con RARs creados por WinRAR de verdad.
- Iconografía: fuente del sistema Segoe MDL2 Assets + logo X vectorial (sin emojis).

Licencia MIT © 2026 ExtractX.
