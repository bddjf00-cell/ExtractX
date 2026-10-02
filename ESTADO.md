# ExtractX v1.2.1 — Estado final (todo verificado)

🌐 **Web:** https://bddjf00-cell.github.io/ExtractX/
📦 **Repo:** https://github.com/bddjf00-cell/ExtractX · **Release v1.2.1** con Setup + portable.

## Qué hay
- **App principal** `ExtractX-v1.2.1.exe` (129 MB, autocontenida, icono nuevo sin fondo):
  gestor estilo WinRAR con esencia Fluent (menú, barra, columnas, totales),
  explorador interno (navegar/abrir/selección/filtro), ZIP/RAR/7Z/TAR/GZ/ISO,
  contraseñas, verificación doble motor, reparación ZIP, benchmark, historial,
  favoritos, bóveda, búsqueda, temas, pantalla de carga, niveles, updates con notas.
- **Doble motor**: SharpCompress nativo + 7-Zip externo (`redist/7za.exe` incluido;
  completo `7z.exe` auto-descargado una vez a `%LocalAppData%\ExtractX\bin`).
- **Instalador propio** `dist/ExtractX-Setup.exe` (6 pasos, ventana elevada con progreso,
  payload auto-descargable, tolerante a versiones): asocia formatos, menús de clic
  derecho, accesos directos, desinstalador que restaura al dueño anterior.
- **Requisito resuelto**: `dist/windowsdesktop-runtime-9.0-win-x64.exe` (58 MB).
- **Web**: 6 páginas + assets + app.js, offline salvo versión viva de la API.
- **Tests** `Tests/`: 48 pruebas con RARs creados por WinRAR de verdad.

## Verificado (batería: TODO OK, 48/48)
RAR5/sólido/cifrado, ZIP±pwd, TAR/TGZ/GZ, 7Z±pwd, niveles, eliminar en ZIP/RAR/7Z,
añadir, convertir, zip-slip contenido, extracción parcial, updater en vivo
(tag + Setup), motor 7z completo, installer solo-setup (descarga payload),
instalación/desinstalación silenciosa, smoke app/mini/setup.

## Bugs cazados por los tests (ya corregidos)
1. RARs con rutas absolutas → saneadas (sin `C:` ni `..`).
2. ZIP-AES de 7-Zip se corrompía en nativo → con contraseña manda 7-Zip primero.
3. `7z x` con `-bsp1` + `--` final → código 7; orden de args corregido.
4. Scraper del MSI elegía `7z920` → orden numérico + mínimo v22.
5. Motor 9.20 obsoleto en disco → se expulsa y re-descarga solo.
6. Nombres relativos vs con carpeta según motor → tests agnósticos al layout.
7. PowerShell 5.1 rompe UTF-8 al reescribir (Get/Set-Content): NUNCA usarlo para
   editar; solo herramienta edit. MainWindow.xaml se restauró de git una vez.

## Pendiente (tuyo, 2 minutos)
1. **Revoca el token pegado en el chat** (GitHub → Settings → Developer settings →
   Personal access tokens) y pega el nuevo en Configuración → token.
2. Ejecuta `dist\ExtractX-Setup.exe` → en Formatos pulsa **Abrir Aplicaciones
   predeterminadas** y elige ExtractX (Windows lo exige; sin esto WinRAR sigue ganando).

Datos de usuario en `%AppData%\ExtractX`. Licencia MIT.
