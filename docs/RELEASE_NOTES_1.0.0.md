# Catalyst CF Editor 1.0

## Español

Editor y cargador NAM/IR para Windows, con el Custom Firmware integrado en un
solo **CatalystCFEditor.exe**. Interfaz en español e inglés.

- Dos posiciones NAM A2 Lite y tres IR de pantalla.
- Instalación inicial con un botón, cinco ejemplos y copia verificada de presets.
- Cambio individual de NAM/IR por USB sin reinstalar el firmware completo.
- Entrada NAM ajustable, inicialmente −5 dB; salida automática o manual,
  inicialmente manual a −3 dB.
- Conversión de IR decodificables a mono/48 kHz/2.048 muestras, cortes ajustables
  de 80 Hz / 8 kHz y volumen automático o manual.
- Boost original y aproximaciones propias EP Boost, 808 y SD-1; efectos PRE/POST,
  presets, afinador y tempo.
- Motor revisado: los cambios nativos de grafo se ejecutan desde el control DSP,
  con protección de carga y del nodo actual; se retiran de la ruta de audio.
- Conexión de datos por posición elegida, diagnóstico sin estado y actualización
  de revisiones antiguas con copia local verificada cuando la app lo requiere.

### Si ya tienes Custom Firmware

**Si ya tienes instalado el motor corregido incluido aquí, basta con sustituir
el EXE por esta revisión del host.** No necesitas reinstalarlo por la corrección
del host ni para cambiar una captura.

Si la app indica que tu CF necesita actualizarse, abre **FIRMWARE CF** y pulsa
**Reinstalar Custom Firmware**. Esa ruta exige una copia local verificada asociada
a la misma edición instalada y evita recorrer de nuevo sus presets. Si falta o no
coincide, la operación se detiene antes de transferir firmware. Otras revisiones
que no requieren esa actualización mantienen el flujo normal de copia nueva.

**Reinstalar crea una edición nueva y activa los dos NAM y tres IR de prueba.**
Los recursos anteriores quedan archivados, sin migración automática. Conserva tus
archivos originales y vuelve a enviarlos desde **CAMBIAR NAM / IR**. Usuarios del
firmware oficial: comienza por **FIRMWARE CF** y sigue la comprobación del arranque.

### Estado de esta preparación

El EXE r106 pasó **13 grupos locales**, sin SDK/USB. El CF corregido se transfirió
a la unidad de desarrollo 60/CX60; el usuario confirmó luces y sonido tras
reiniciar y los diagnósticos posteriores avanzaron sin fault registrado.
**El usuario confirmó cambios físicos con CH A/B, con las apps cerradas y sin
perder sonido. También confirmó A → B → A desde el editor, con audio y controles
funcionando.** Siguen pendientes el guardado del preamp, la persistencia tras
apagar y encender y la matriz física completa. Consulta [Validación](VALIDACION.md)
antes de instalar.

Instalación habilitada para **Catalyst 60/CX60**. Los 100/200 no tienen pruebas
físicas y su instalación inicial permanece deshabilitada. Es experimental y el
EXE no está firmado. Lee [Uso](USO.md), [FAQ](FAQ.md) y
[Recuperación](RECUPERACION.md). Los hashes identifican los archivos distribuidos.

Descarga principal: **CatalystCFEditor.exe**. El ZIP Windows añade guías, avisos
y firmware oficial de recuperación; el ZIP source es para desarrolladores.
No necesitas compilar.

## English

A Windows amplifier editor and NAM/IR loader with Custom Firmware included in
one **CatalystCFEditor.exe**, with Spanish and English interface.

- Two NAM A2 Lite positions and three cabinet IRs.
- One-button initial installation, five examples and a verified preset backup.
- Individual NAM/IR uploads over USB without reinstalling the full firmware.
- Adjustable NAM input, default −5 dB; independent automatic/manual output,
  default manual −3 dB.
- Decodable IR conversion to mono/48 kHz/2,048 samples, adjustable 80 Hz / 8 kHz
  cuts and automatic/manual level.
- Factory Boost and original EP Boost, 808 and SD-1 approximations, PRE/POST
  effects, presets, tuner and tempo.
- Revised engine: native graph changes run on the DSP control owner, with load
  and current-node protection, rather than from audio.
- Selected-slot data connection, stateless diagnostics and verified local-backup
  reuse when an earlier firmware revision requires that upgrade path.

### If you already have Custom Firmware

**If the corrected engine included here is already installed, replace only the
EXE for this host revision.** Reinstallation is not needed for the host fix or
to change one capture.

If the app reports that your CF needs an update, open **FIRMWARE CF** and click
**Reinstall Custom Firmware**. That path requires a locally verified backup tied
to the same installed edition, without cycling through its presets again. Missing
or mismatched evidence stops the operation before firmware transfer. Earlier
revisions that do not require that update use the normal fresh-backup workflow.

**Reinstallation creates a new edition and activates the two demo NAMs and three
demo IRs.** Previous resources remain archived, without automatic migration.
Keep your original files and upload them again through **CHANGE NAM / IR**.
With official firmware, start in **FIRMWARE CF** and follow **Verify startup**
after restarting when prompted.

### Status of this preparation

The r106 EXE passed **13 local groups**, without SDK/USB access. The corrected CF
was transferred to the development 60/CX60; the user confirmed lights and sound
after restarting, and later diagnostics progressed without a recorded fault.
**The user confirmed physical CH A/B changes with all apps closed and continuing
audio. GUI A → B → A switching was also confirmed with working audio and controls.**
Preamp-save/cold-boot persistence and the full hardware matrix remain pending.
See [Validation](VALIDACION.md) before installation.

Initial installation is enabled for **Catalyst 60/CX60**. Physical 100/200 testing
is unavailable and their initial installation remains disabled. Experimental and
unsigned; read [Usage](USO.md), [FAQ](FAQ.md#english) and
[Recovery](RECUPERACION.md#english). Published hashes identify the files.

Main download: **CatalystCFEditor.exe**. The Windows ZIP adds guides, notices and
official recovery firmware; the source ZIP is for developers. End users do not
need to compile.

Developed by **conr4d / conr4daudio**.
[GitHub](https://github.com/conr4d86/Catalyst-CF-Editor) ·
[Buy me a coffee](https://buymeacoffee.com/conr4d)
