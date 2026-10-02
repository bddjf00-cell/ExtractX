# ExtractX v1.1.0 — Estado final (todo verificado)

🌐 **Web publicada:** https://bddjf00-cell.github.io/ExtractX/
📦 **Repo:** https://github.com/bddjf00-cell/ExtractX · **Release v1.1.0** con Setup (+ portable en subida).

## Qué hay
- **App principal** `ExtractX-v1.1.0.exe` (129 MB, autocontenida, sin dependencias):
  modo grande Fluent oscuro (1080×680) + minis de extracción/compresión,
  ZIP/RAR/7Z/TAR/GZ/ISO/CAB/BZ2/XZ/WIM, contraseñas, verificación doble motor,
  reparación ZIP, benchmark, historial, favoritos, bóveda, búsqueda, temas,
  pantalla de carga con logo, niveles de compresión, formato/nivel por defecto,
  actualizaciones vía GitHub Releases con notas y parches.
- **Doble motor**: SharpCompress nativo + 7-Zip externo (`redist/7za.exe` incluido;
  completo `7z.exe` auto-descargado una vez a `%LocalAppData%\ExtractX\bin`).
- **Instalador propio** `dist/ExtractX-Setup.exe` (estilo Fluent, 6 pasos):
  asocia formatos, menús de clic derecho, accesos directos, entrada en
  *Agregar o quitar programas*, desinstalador que restaura al dueño anterior.
- **Requisito resuelto**: `dist/windowsdesktop-runtime-9.0-win-x64.exe` (58 MB).
  El setup detecta si falta .NET 9, usa la copia local o la descarga solo.
- **Web** `docs/index.html` + `styles.css`: offline, sin dependencias, con favicon.
- **Tests** `Tests/`: 28 pruebas con RARs creados por WinRAR de verdad.

## Verificado (batería `dotnet run --project Tests -c Release`: TODO OK)
| Prueba | Resultado |
|---|---|
| Compilación app + setup | 0 errores |
| RAR5 / sólido / defecto / cifrado (WinRAR real) | extracción byte a byte OK |
| RAR con contraseña incorrecta | falla como debe |
| ZIP ± contraseña, TAR, TAR.GZ, GZ, 7Z ± contraseña | roundtrip OK |
| Niveles Stored vs Maximum | Stored > 2× Maximum |
| Motor 7z completo (descarga MSI oficial) | 7z.exe + 7z.dll listos |
| RAR vía 7z completo | OK (cubre el bug "unpacked file size…") |
| Updates (IsNewer + CheckAsync sin repo) | OK sin reventar |
| App modo grande / mini (smoke) | arrancan sin crash |
| Instalación/desinstalación silenciosa | exit 0, registro restaurado |
| Asociaciones reales del usuario | **NO tocadas** (tests con extensiones falsas y TEMP) |

## Bugs cazados por los tests (ya corregidos)
1. RARs con rutas absolutas → ahora se sanean (sin `C:` ni `..`).
2. ZIP-AES de 7-Zip se corrompía en nativo → con contraseña manda 7-Zip primero.
3. `7z x` con `-bsp1` + `--` final → código 7; orden de args corregido.
4. Scraper del MSI elegía `7z920` (orden alfabético) → orden numérico + mínimo v22.
5. Motor 9.20 obsoleto en disco → se expulsa y re-descarga solo.

## Novedades (01/10/2026)
- **Explorador interno**: navegar carpetas, abrir archivos con doble clic, extraer
  selección, filtro por nombre, columnas Nombre/Tamaño/Modificado. Tests: 35/35.
- **Setup**: ventana elevada con progreso (adiós "se queda ahí"), tolera payloads
  `ExtractX-v*.exe`, versión dinámica. Usa `dist/ExtractX-Setup.exe` (v1.1.0).
- **Web**: 6 páginas publicadas en https://bddjf00-cell.github.io/ExtractX/
- **Repo + Release v1.1.0** con Setup y portable (auto-update activo).

## Pendiente (lo haces tú en 2 minutos)
1. Sube `dist/ExtractX-Setup.exe` (y/o el portable) como assets de un Release `v1.1.0`
   en `github.com/bddjf00-cell/ExtractX` → las auto-actualizaciones empiezan a funcionar.
2. **Revoca el token pegado en el chat** (GitHub → Settings → Developer settings →
   Personal access tokens) y pega el nuevo en Configuración → token.
3. Ejecuta `dist\ExtractX-Setup.exe` → en Formatos pulsa **Abrir Aplicaciones
   predeterminadas** y elige ExtractX (Windows lo exige; sin esto WinRAR sigue ganando).

Datos de usuario en `%AppData%\ExtractX`. Licencia MIT.
