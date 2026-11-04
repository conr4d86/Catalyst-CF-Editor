# Inicio rápido

**Español** · [English](QUICKSTART.md)

[Descarga](https://github.com/conr4d86/Catalyst-CF-Editor/releases) ·
[Preguntas frecuentes](FAQ.md#español) · [Recuperación](RECUPERACION.md)

## 1. Abre la aplicación y conecta el amplificador

Descarga **CatalystCFEditor.exe** y ábrelo en Windows. La aplicación, el Custom
Firmware y los ejemplos están incluidos en ese EXE; no necesitas seleccionar
otro firmware ni instalar un plugin de audio.

Guarda tus ajustes pendientes, cierra Catalyst Edit y otros gestores Line 6,
incluido POD Go NAM Manager, y conecta el Catalyst por USB. Mantén el Master y
los auriculares bajos. Si Windows no detecta el amplificador, revisa su cable y
el controlador oficial disponible en [Line 6](https://line6.com/software/).

La instalación inicial de esta versión está habilitada para **Catalyst 60/CX60**.
Los 100/200 no están probados físicamente y su instalación permanece deshabilitada.

## 2. Instala el Custom Firmware una vez

Abre **FIRMWARE CF** y pulsa su único botón **Instalar Custom Firmware**.
Si ya tienes un CF reconocido, el botón se llama **Reinstalar Custom Firmware**.

Incluye **NAM 1 y NAM 2 con la misma captura de prueba** y **tres IR de prueba**.
La app verifica una copia de los doce presets antes de instalar. Deja el USB,
la alimentación y la app conectados hasta que termine.

Cuando lo indique, apaga y enciende normalmente, **sin pulsar botones**, y pulsa
**Comprobar arranque**. Las pestañas del editor se desbloquean cuando la app
confirma un CF compatible.

Reinstalar vuelve a poner los cinco ejemplos. Conserva tus archivos NAM e IR
originales para volver a cargarlos después. Un preset no sustituye esos archivos.

## 3. Cambia un NAM o IR sin reinstalar

En **CAMBIAR NAM / IR**:

1. Elige **NAM 1, NAM 2, IR1, IR2 o IR3** y pulsa **1. Cargar** o arrastra el archivo.
2. Pulsa **2. Conectar al ampli**.
3. Pulsa **3. Enviar** a la posición elegida y espera la confirmación.

Solo cambia esa posición; las otras cuatro se conservan. No hace falta rellenar
todas. Cargar un archivo lo prepara en el PC: el envío empieza al pulsar Enviar.
**Vaciar** quita la selección local, no borra el contenido del amplificador.

NAM admite **A2 Lite, mono, 48 kHz**. **Entrada NAM** empieza en **−5 dB** y es
ajustable: cambia la saturación y la respuesta. El volumen de salida empieza en
**manual a −3 dB**; puedes elegir Automático. Para aplicar otros valores, vuelve
a enviar el NAM.

Los IR decodificables se adaptan a mono, 48 kHz y 2.048 muestras. Los cortes
iniciales son **Low Cut 80 Hz / High Cut 8.000 Hz**, ajustables antes del envío.
Los archivos originales no se modifican.

## 4. Elige el sonido y guarda el preset

Vuelve a **SONIDO**, selecciona banco y canal A o B, elige **NAM 1 o NAM 2** en
AMPLIFICADOR / NAM y el IR que quieras. Ajusta Gain, EQ, preamp y volumen.
Pulsa **Guardar preset** y espera su confirmación antes de cambiar de preset.

NAM se procesa dentro del amplificador y llega al altavoz y a las salidas
directas. El IR corresponde a **auriculares, Direct Out y USB**; el altavoz
físico conserva su ruta. No hace falta un VST ni procesar el audio en el PC.

Si una operación informa de error o queda pendiente, conserva el mensaje y lee
[Preguntas frecuentes](FAQ.md#español) o [Recuperación](RECUPERACION.md). No
reinicies durante una transferencia activa.
