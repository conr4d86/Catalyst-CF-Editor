# Recuperación del amplificador

Ten preparada una copia de tus presets y el actualizador oficial antes de probar firmware experimental. Este procedimiento no garantiza recuperar cualquier fallo de hardware o firmware.

## Si la app informa de una transferencia activa

Mantén alimentación y USB. Espera una confirmación de finalización o de error antes de reiniciar. El aviso rojo **Apaga y enciende tu ampli** corresponde a una transferencia ya terminada; en ese momento se pide un encendido normal, sin botones.

## Si no arranca o no tiene audio tras el fallo

1. Conserva la carpeta de copias y el mensaje de error. Cierra Catalyst CF Editor, CatalystIR, Catalyst Edit y el actualizador.
2. Apaga el ampli y vuelve a abrir el actualizador oficial de Line 6.
3. Con USB conectado, enciende manteniendo **CH A + CH B** para entrar en recuperación.
4. Selecciona el modelo correcto y reinstala su firmware oficial. Sigue las indicaciones del actualizador, sin interrumpir la escritura.

Estas indicaciones resumen la [guía oficial de recuperación](https://kb.line6.com/catalyst-firmware-update-recovery-procedure). Descarga la herramienta oficial desde [Line 6](https://line6.com/software/).

## Reset y recuperación de presets

**A+B y B+MANUAL realizan acciones diferentes.** El procedimiento oficial termina con un reset al encender con **CH B + MANUAL**. Ese reset devuelve los presets a fábrica: asegúrate de conservar tu copia antes de hacerlo. Consulta el apartado Factory Reset del [manual oficial, página 12](https://line6.com/data/6/0a020a4117d6f657a259004574/application/pdf/Catalyst%20CX%20Pilot%27s%20Guide%20-%20English%20.pdf#page=12).

En las recuperaciones anteriores de nuestra unidad se probó primero un encendido normal después de reinstalar el firmware oficial y se conservaron los presets. Es una observación de esa unidad; no sustituye el reset que prescribe la guía oficial ni garantiza el mismo resultado.

Si realizas el reset, abre Catalyst Edit cuando el ampli vuelva a arrancar y restaura la copia `.catl6a` mediante su función de restauración. Guarda la carpeta completa de verificación por si hubiera que revisar la copia.

## Firmware oficial incluido

Cada instalación copia el firmware original a:

```text
%LOCALAPPDATA%\CatalystCFEditor\initial-installations\backups\<operación>\Catalyst-OFICIAL-recuperacion.ctf
```

El mismo original 2.02 queda integrado en la aplicación y se copia en la carpeta de cada instalación. Conserva una copia local antes de necesitar recuperación. El ZIP Windows de la release también lo incluye en `recovery`. Es el archivo oficial exacto usado como base; no contiene IR personalizados.

SHA256:

```text
f9fe2482e561bef8a04128a38af89cadee4f8d06071b06a9053e36c206471ae8
```

Para comprobarlo en PowerShell:

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath .\recovery\Catalyst-OFICIAL-recuperacion.ctf
```

Utiliza la selección local de firmware del actualizador oficial si está disponible; en caso contrario sigue su descarga y procedimiento oficiales. El archivo corresponde a la familia indicada por su fabricante, pero esta revisión de Catalyst CF Editor solo habilita el producto Catalyst 60/CX60 comprobado. Nunca elijas otro modelo para forzar la actualización.

Restaurar el firmware oficial elimina las funciones del CF. La copia de presets
no sustituye tus archivos NAM e IR: consérvalos por separado. Para volver después
al CF, instálalo desde la app y vuelve a enviar los recursos que quieras utilizar.

Si no aparece ni siquiera en recuperación, revisa cable, puerto, alimentación y controlador. Si sigue sin detectarse, solicita soporte de Line 6; no repitas a ciegas candidatos que ya hayan fallado.

## English

Keep power and USB connected during an active update. After the updater confirms
completion, restart normally when prompted. If the amplifier does not boot or
produce audio, close all editors and use the official Line 6 updater. Start with
CH A + CH B held for recovery, then install the correct official firmware.
CH B + MANUAL is a different, destructive factory reset: preserve your preset
backup first and follow the official recovery guide linked above. Official
firmware removes CF functions; keep original NAMs and IRs separately from presets.
