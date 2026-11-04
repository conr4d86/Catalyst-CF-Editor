# Uso / Usage

[Español](../README.es.md) · [English](../README.md)

En SONIDO / SOUND, elige banco y canal A o B. NAM 1 y NAM 2 ocupan las dos
posiciones Chime del firmware personalizado. El nombre de la captura aparece
junto al selector. Guarda el preset para conservar sus ajustes.

FIRMWARE CF instala o reinstala el motor con los ejemplos incluidos. CAMBIAR
NAM / IR es otra pestaña principal: carga una posición y conserva las demás.
Reinstalar crea una edición nueva y sustituye las cinco posiciones activas por
los ejemplos incluidos: conserva los archivos NAM e IR que quieras volver a cargar.
Los recursos de la edición anterior quedan archivados; no se migran ni se mantienen
activos automáticamente. Después de una instalación
completa, reinicia normalmente cuando lo indique la app y pulsa Comprobar arranque.
Durante el envío, espera para tocar y mantén alimentación y USB conectados.
La app alterna la conexión del editor y la de datos; no abras otro gestor Line 6
a la vez. Si un envío queda pendiente, comprueba su guardado antes de repetirlo.

Si la app indica que el CF instalado necesita actualizarse, reutiliza únicamente una copia local
verificada de los doce presets asociada a la edición instalada que acaba de
identificar. Comprueba el archivo y sus lecturas de respaldo antes de abrir la
conexión de instalación. No vuelve a recorrer los presets del CF antiguo ni
crea una copia nueva de los últimos ajustes. Si esa copia falta, está modificada
o no coincide, detiene la actualización antes de transferir firmware. Conserva
la carpeta completa de la instalación anterior; no borres ni reconstruyas a mano
sus archivos de verificación para intentar continuar.

«Copia completa» reúne los doce presets nativos en un archivo `.catl6a`;
un `.catl6p` contiene un preset. Estas copias no incluyen capturas NAM, IR ni
los ajustes adicionales del CF guardados aparte. Conserva los archivos originales
de tus recursos; una copia de presets no los sustituye.

El afinador está en SISTEMA / SYSTEM. Actívalo y toca una nota: la nota aparece
en grande; el centro verde indica afinación. BPM y TAP están debajo, en TEMPO.
Español / English se elige debajo del botón USB y la preferencia queda guardada.

Los preamps son aproximaciones propias, no algoritmos extraídos de POD Go.
EP, 808 y SD-1 tienen ruta para los modelos originales y NAM; consulta el
[alcance de las pruebas](VALIDACION.md). Level está antes del modelo:
también puede modificar la saturación. Entrada NAM empieza en −5 dB y es ajustable;
la salida empieza en manual a −3 dB. Para aplicar estos dos ajustes, vuelve a enviar
el NAM. Un NAM no se reentrena al mover Gain. Los filtros IR se aplican al preparar
el recurso antes del envío; para cambiarlos, prepara y vuelve a enviar ese IR.

## English

Choose an A or B channel in SOUND. Save the preset to keep its settings.
FIRMWARE CF installs or reinstalls the engine and examples. CHANGE NAM / IR
uploads one position and preserves the others. Wait for transfers to finish
before playing, and keep USB/power connected. Do not open another Line 6
manager at the same time. Reconcile an uncertain save before resending.
Reinstalling creates a new edition and replaces all five active positions with
the included examples: retain the NAM and IR files you want to reload. Previous
edition resources remain archived; they are not migrated or kept active
automatically. After a full installation, restart normally
when prompted and click Verify startup.

If the app reports that the installed CF needs an update, it reuses only a locally verified twelve-preset
backup associated with the installed edition it has just identified. It checks
the archive and its repeated-read evidence before opening the installation
connection. It does not cycle through the older CF's presets or create a fresh
backup of recent edits. A missing, modified or mismatched backup stops the upgrade
before firmware transfer. Keep the complete previous installation folder; do not
delete or manually rebuild its verification files to bypass the check.

“Full backup” collects the twelve native presets in a `.catl6a` archive;
a `.catl6p` contains one preset. These files do not include NAM captures, IRs
or the CF's separate additional settings. Keep your original resource files;
a preset backup does not replace them.

The tuner is in SYSTEM, with BPM/TAP in TEMPO below it. Choose Spanish/English
under the USB button. The engine routes EP, 808 and SD-1 through the original
models and NAM; see [validation coverage](VALIDACION.md). Pedal Level precedes the model
and can affect saturation. Adjustable NAM input defaults to −5 dB; output defaults
to manual −3 dB. Upload the NAM again to apply those settings.
Gain does not retrain a capture. IR cuts are applied during preparation: change
them and upload that IR again to store a different filter setting.
