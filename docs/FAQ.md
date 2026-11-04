# Preguntas frecuentes / FAQ

[Inicio rápido](INICIO_RAPIDO.md) · [Quick start](QUICKSTART.md) ·
[Recuperación / Recovery](RECUPERACION.md)

## Español

### ¿Necesito instalar un VST o dejar el PC procesando el sonido?

No. NAM y los IR se procesan dentro del amplificador. La app prepara y envía
los archivos y permite editar sus controles; no es un plugin de audio.
La aplicación se distribuye como un solo **CatalystCFEditor.exe**, con el CF
y los ejemplos integrados. Windows puede necesitar el controlador oficial de Line 6.

### ¿Qué modelos admite esta versión?

La instalación inicial está habilitada para **Catalyst 60/CX60**. El desarrollo
contempla 100/200, pero no hay pruebas físicas en esas unidades y su instalación
permanece deshabilitada. Una base de firmware compartida no prueba por sí sola
la compatibilidad del CF. Consulta el [estado de validación](VALIDACION.md).

### ¿Por qué solo puedo entrar en FIRMWARE CF?

La app todavía no ha confirmado un CF compatible. Con firmware oficial, instala
el Custom Firmware desde ese apartado. Si ya lo tienes, conecta el amplificador
y espera su comprobación; otras apps Line 6 deben estar cerradas. Un nombre de
archivo o un recibo anterior no sustituyen la confirmación de la unidad conectada.

### ¿Hay que instalar todo el firmware cada vez que cambio un archivo?

No. Instálalo una vez desde **FIRMWARE CF**. Después usa **CAMBIAR NAM / IR**:
carga o arrastra el archivo, conecta al ampli y envía la posición elegida.
Se conserva el contenido de las demás posiciones; puedes cambiar solo un NAM
o un IR. No es necesario rellenar las cinco.

### ¿Qué incluye Instalar o Reinstalar Custom Firmware?

Un único botón instala el motor, **la misma captura de prueba en NAM 1 y NAM 2**
y **tres IR de prueba**, tras verificar una copia de los doce presets.
Reinstalar crea una edición nueva y vuelve a poner esos cinco ejemplos como
recursos activos. Los de la edición anterior quedan archivados, sin migrarse
automáticamente: conserva tus archivos originales para volver a enviarlos.
No reinstales solo para cambiar una captura.

### Ya tengo un CF anterior. ¿Cómo se hace la actualización?

Depende de la revisión. Si la app indica que el CF instalado necesita actualizarse,
identifica su edición y reutiliza una copia local verificada
de los doce presets asociada a esa misma edición. Comprueba también sus archivos
de verificación; no vuelve a cambiar de preset repetidamente para hacer otra copia
en el CF antiguo. Esta copia anterior no incluye necesariamente tus últimos ajustes.

Si falta, está modificada o no corresponde a esa edición, la actualización se
detiene antes de abrir la conexión de instalación y transferir firmware. Conserva
la carpeta completa de la instalación anterior. No elimines las verificaciones ni
insistas repitiendo selecciones de preset para saltarte el bloqueo.

Las revisiones anteriores que no requieren esa actualización mantienen el flujo
normal con una copia nueva de presets antes de reinstalar.

La reinstalación deja activos **los dos NAM y tres IR de prueba**, no tus capturas
anteriores. Después usa **CAMBIAR NAM / IR** para volver a enviar las que quieras.

### ¿Admite cualquier archivo .nam?

No: esta versión requiere **NAM A2 Lite, mono y 48 kHz**, con la estructura
admitida por el importador. Cambiar la extensión o el nombre no convierte otra
arquitectura. Si el archivo no es compatible, la app lo rechaza antes de enviarlo.

### ¿La copia de presets también guarda mis NAM, IR y ajustes del CF?

No. **Copia completa** reúne los doce presets nativos en `.catl6a`; `.catl6p`
contiene un preset. Esos formatos no incluyen capturas NAM, IR ni los ajustes
adicionales del CF almacenados aparte. Conserva tus archivos originales y no
interpretes una copia de presets como una copia completa de los recursos del CF.

### ¿Por qué hay Entrada NAM y Volumen de salida?

Son ajustes distintos. **Entrada NAM** empieza en **−5 dB** y modifica el nivel
que recibe la captura, por lo que afecta a su saturación y respuesta. **Salida**
ajusta el volumen final de esa captura; empieza en **manual a −3 dB** y también
permite Automático. Ambos valores se aplican al preparar y enviar el archivo:
para cambiarlos en el ampli, vuelve a enviar el NAM.

Automático calcula un ajuste de salida antes del envío. No es un control
continuo mientras tocas ni garantiza idéntica sonoridad con cualquier guitarra o preset.

### ¿La app adapta mis IR? ¿Llevan Low Cut y High Cut?

Los archivos de audio decodificables se adaptan a **mono, 48 kHz y 2.048 muestras**.
La app admite WAV, AIFF/AIFC y FLAC, con conversión cuando hace falta. No puede
reconstruir audio de un archivo corrupto o de un formato que no pueda decodificar.
El límite es **64 MiB por archivo**. Los formatos propietarios no decodificables
se rechazan; cambiar la extensión no convierte su contenido.

Los cortes iniciales y los tres ejemplos son **80 Hz / 8.000 Hz**. Puedes ajustar
los filtros antes del envío. Se aplican al preparar el IR: para guardar otros
cortes, prepara y vuelve a enviar ese IR. El archivo original no se modifica.

### ¿Dónde se oyen NAM e IR? ¿Se mantienen al apagar?

NAM llega al altavoz y a las salidas directas. Los IR corresponden a
**auriculares, Direct Out y USB**; no cambian el cono físico. Los datos se guardan
en la unidad. Guarda también el preset para conservar su selección y ajustes.
No necesitas volver a pasar por la app en cada encendido. La cobertura física
de cada revisión se detalla en [Validación](VALIDACION.md).

### ¿EP, 808 y SD-1 son los algoritmos de POD Go?

Son aproximaciones propias, no algoritmos extraídos de POD Go o Helix.
La revisión del motor incluye su ruta para los modelos originales y NAM.
Drive, Level y Tone afectan a la señal antes del modelo de amplificador;
Level también puede cambiar la saturación. No se promete identidad sonora con
los productos comerciales de referencia.

### ¿Vaciar borra lo guardado en el amplificador?

No. **Vaciar** quita el archivo seleccionado en la app. El contenido del ampli
solo cambia al completar una operación de envío o una instalación.

### ¿Qué hago si aparece un error de conexión o una operación pendiente?

Cierra los demás gestores Line 6 y conserva el mensaje de error. No abras dos
editores a la vez. Si la app permite comprobar un resultado pendiente, hazlo
antes de repetir un envío o una instalación. No cortes la alimentación durante
una transferencia activa. Para problemas de arranque o audio después de una
actualización, sigue [Recuperación](RECUPERACION.md).

### ¿La advertencia de Windows o antivirus demuestra que es malware?

El EXE no está firmado y puede generar avisos. Un aviso no identifica por sí solo
la causa, y haberlo compilado tampoco garantiza ausencia de problemas. Descarga
desde la [release del proyecto](https://github.com/conr4d86/Catalyst-CF-Editor/releases)
y compara la huella publicada. No es necesario desactivar el antivirus para leer las guías.

### ¿Cómo comunico un fallo sin publicar información personal?

Indica versión, modelo, pasos, código de error y una captura recortada del mensaje.
Oculta nombres personales, rutas locales e identificadores de la unidad.
No adjuntes copias de presets, backups completos, registros personales o capturas
NAM/IR de terceros al issue público. Describe primero el problema.

## English

### Do I need a VST or a PC processing my audio?

No. NAM and IR audio processing runs inside the amplifier. The app prepares and
uploads files and edits controls; it is not an audio plugin. A single
**CatalystCFEditor.exe** includes the CF and examples. Windows may need the
official Line 6 driver.

### Which models does this version support?

Initial installation is enabled for **Catalyst 60/CX60**. Development includes
100/200 handling, but those units have not been physically tested and their
installation remains disabled. A shared firmware base alone does not prove CF
compatibility. See [Validation](VALIDACION.md).

### Why can I only open FIRMWARE CF?

The app has not yet confirmed compatible CF. With official firmware, install CF
from that tab. If you already have it, connect the amplifier and wait for its
check; close other Line 6 apps. A filename or an earlier receipt does not replace
confirmation of the connected unit.

### Do I need a full firmware installation for every new file?

No. Install once from **FIRMWARE CF**. Afterwards, use **CHANGE NAM / IR**:
load or drop a file, connect to the amp, and send the selected position.
The other positions are preserved. You can replace only one NAM or IR;
you do not have to fill all five.

### What does Install or Reinstall Custom Firmware include?

One button installs the engine, **the same demo capture in NAM 1 and NAM 2**,
and **three demo IRs**, after verifying a twelve-preset backup.
Reinstalling creates a new edition and activates those five examples. Previous
edition resources remain archived and are not migrated automatically: retain
your original files to upload them again. Do not reinstall merely to change one capture.

### I already have an older CF. How does upgrading work?

It depends on the revision. If the app reports that the installed CF needs an
update, it identifies its edition and reuses a locally verified
twelve-preset backup associated with that same edition. It also checks the backup's
verification files; it does not repeatedly select presets on the older CF to make
another backup. That earlier backup may not contain your most recent edits.

A missing, modified or mismatched backup stops the upgrade before opening the
installation connection and transferring firmware. Keep the complete previous
installation folder. Do not delete verification files or keep cycling presets
to bypass the block.

Earlier revisions that do not require that update use the normal workflow,
including a fresh preset backup before reinstalling.

Reinstallation activates **the two demo NAMs and three demo IRs**, rather than your
previous captures. Afterwards, use **CHANGE NAM / IR** to upload the files you want.

### Will any .nam file work?

No. This version requires **NAM A2 Lite, mono, 48 kHz**, with the structure
accepted by the importer. Renaming a file does not convert another architecture.
An incompatible file is rejected before uploading.

### Does a preset backup also save my NAMs, IRs and CF settings?

No. **Full backup** collects the twelve native presets in `.catl6a`; `.catl6p`
contains one preset. These formats do not include NAM captures, IRs or separately
stored additional CF settings. Keep your original files and do not treat a preset
backup as a complete backup of CF resources.

### Why are NAM Input and Output Volume separate?

**NAM Input** starts at **−5 dB** and changes the level entering the capture,
affecting saturation and response. **Output** sets that capture's final level;
it starts in **manual mode at −3 dB**, with Automatic also available. Both values
are applied during file preparation and upload: upload the NAM again to change
them in the amplifier.

Automatic calculates an output adjustment before uploading. It is not continuous
automatic level control while you play, or a guarantee of equal perceived loudness
with every guitar or preset.

### Does the app convert IRs and provide low/high cuts?

Decodable audio files are converted to **mono, 48 kHz, 2,048 samples**.
WAV, AIFF/AIFC and FLAC are supported, with conversion when needed. Corrupt audio
or an undecodable format cannot be reconstructed by the importer.
The limit is **64 MiB per file**. Undecodable proprietary formats are rejected;
renaming the extension does not convert their contents.

Initial cuts and all three examples use **80 Hz / 8,000 Hz**. Adjust filters
before uploading. They are applied during IR preparation: prepare and upload
that IR again to store different cuts. Original files are unchanged.

### Which outputs use NAM and IRs? Are they kept after switching off?

NAM reaches the speaker and direct outputs. Cabinet IRs apply to **headphones,
Direct Out and USB**, without changing the physical speaker. Data is stored in
the unit. Save the preset too, to keep its selection and settings. The app is
not required at each startup. [Validation](VALIDACION.md) describes the recorded
hardware coverage for each revision.

### Are EP, 808 and SD-1 the POD Go algorithms?

They are original approximations, not algorithms extracted from POD Go or Helix.
This engine revision routes them through the original amp models and NAM.
Drive, Level and Tone affect the signal before the amp model; Level can also
change saturation. No identical response to the referenced commercial products
is promised.

### Does Clear erase the amplifier's stored file?

No. **Clear** removes the local file selection. Amplifier content changes only
after completing an upload or installation operation.

### What should I do after a connection error or pending operation?

Close other Line 6 managers and keep the error message. Do not run two editors
at the same time. If the app offers to check a pending result, do that before
repeating an upload or installation. Keep power connected during active transfers.
For boot or audio problems after an update, follow [Recovery](RECUPERACION.md#english).

### Does a Windows or antivirus warning prove that the EXE is malware?

The EXE is unsigned and may trigger warnings. A warning alone does not establish
the cause, and compiling an executable does not guarantee it has no problems.
Download from the [project release](https://github.com/conr4d86/Catalyst-CF-Editor/releases)
and compare its published hash. You do not need to disable antivirus to read the guides.

### How can I report a problem without exposing private information?

Include the version, model, steps, error code and a cropped screenshot of the
message. Remove personal names, local paths and device identifiers. Do not attach
preset backups, complete backups, personal logs or third-party NAM/IR captures
to a public issue. Describe the problem first.
