# Revisión independiente del propietario de control DSP

2026-11-04. Alcance: lectura del firmware original y del parche propuesto;
informes aislados. Sin USB, SDK, cambios de firmware, cierre de aplicaciones ni
publicación. La implementación pertenece a `release_audit` del chat Catalyst.

## Hallazgo que cambia la propuesta

`UserMode:task` y `Patch:task` no bastan para afirmar que el servicio CF y la carga
nativa de modelos se ejecutan en un único productor. Hay otra tarea explícita:
**`DspSchedulerCtrlTask:task`**. Es un contexto de control, distinto del callback
de audio. La selección de modelo se encola desde Patch y la consume esta tarea.

| Elemento | Dirección / valor |
|---|---|
| Entrada Thumb | `0x8000b979` |
| Constructor | `0x8000cef4` |
| Creación de tarea, BL | `0x8000d37c` |
| Nombre literal | `[0x8000d44c] = 0x8015c694` |
| Objeto global | `0x808062fc` |
| Slot del handle de tarea | `0x80806300` |
| TCB estático | `0x80806304` |
| Stack | `0x80806370 <= SP < 0x80806b70` |
| Tamaño stack | `0x200` palabras de 32 bits |
| Slot cola | `0x80806bd4` |
| Cola | 128 mensajes de 16 bytes |
| Slot handler FSM | `0x80806bd0` |
| Handler inicial | `0x8000c475` |
| Receive BL | `0x8000b9b0 -> 0x8004c8f8 -> 0x6981` |
| Dispatcher BL | `0x8000b9b8 -> 0x8002f218` |

`Patch:task` handler `0x80010454`, caso 3, llama `0x8000f4d8`, que llama
`0x8000d460`. Esta última encola opcode 3 con timeout 0 en la cola del controlador
DSP. El handler `0x8000c474`, caso 3, consume la selección y aplica cambios mediante
`0x8000e35c`. La llamada indirecta posterior de `0x8004aa08` por vtable +0x18
coincide con el método `0x8004ba68` en la tabla `0x80172358`; se debe verificar
también la instancia/vtable efectiva si se usa esa ruta.

## Mutex externo y API pública

`0x80048b44` recibe:

```
r0 = publicobj, objeto/mutex externo del backend
r1 = native_node_handle, NO puntero de nodo
r2 = índice del parámetro
r3 = puntero al dato
```

Orden confirmado por instrucciones: toma mutex externo (BL `0x80048b50`),
obtiene arena en `[publicobj+0xdc]`, resuelve handle (`0x80046d3c`, BL
`0x80048b5c`), llama setter `0x8004650c` (BL `0x80048b68`), suelta mutex.
Los cargadores `0x80048a04` y `0x80048d78` también toman ese mutex externo.

El setter interno prepara el grafo **antes** de adquirir el mutex de intercambio
`arena+0xf0`. Una comprobación de punteros antes y después de llamar al setter
no protege esa preparación frente a otro productor. No adquirir `arena+0xf0`
antes de llamar al setter: éste vuelve a tomarlo y no es recursivo.

### Mutex in situ: no desreferenciar el primer argumento

`0x1385c` es exactamente `mov r1,-1; push; bl 0x6c28; mov r0,0; pop`.
**No contiene `ldr r0,[r0]`.** `0x8004c4ec` crea la cola/mutex estática mediante
`0x67a4(1, publicobj)`. La primera palabra de `publicobj` queda a 0 como tipo
mutex; esto es normal, no un handle ausente.

```
tryTake = Thumb 0x6c29(publicobj, 0)       // 1 adquirido, 0 no adquirido
give    = Thumb 0x646d(publicobj, 0, 0, 0) // 1 correcto
```

El wrapper `0x1386c` convierte el 1 del give a 0, y fallo a -38. Si se toma el
mutex externo por separado, llamar al setter INTERNO bajo ese mutex y liberarlo
en toda salida. No volver a llamar a la API pública que lo adquiriría otra vez.

## Obtener el contexto y el ampli actuales

El constructor prueba la relación de layout, sin depender de una dirección de
heap de una emulación concreta:

```
root = 0x20026378                 // literal en 0x800054bc
context_base = U32(root + 0x6c)   // contexto 0
publicobj = context_base + 8
arena = context_base + 0x160
publicobj = arena - 0x158
```

`0x80049660` llama `0x800490f0(base+8, base+0x160, ...)`; este constructor crea
el mutex en el primer argumento y guarda el segundo en su +0xdc. También crea
el stream en `base+0x3e4`, mediante `0x8004c460`, con `[stream+4]=publicobj`.
Ejemplo del fixture comunicado por Catalyst: publicobj `0x200254e8`, arena
`0x20025640`, diferencia `0x158`. Es un ejemplo, no direcciones a hardcodear.

El constructor del controlador busca los nombres `AmpSlot` y `AmpSubSlot`:

```
descriptor_amp_slot = U32(0x80807454)
descriptor_amp_subslot = U32(0x80807458)
stream = U32(U32(descriptor_amp_subslot))
slot = U32(descriptor_amp_subslot + 4)
table = U32(stream + 0x1c)
// Comprobar slot < U32(stream+0x18)
handle = U32(table + 12*slot + 4)
valid = U8(table + 12*slot + 8)
publicobj_from_stream = U32(stream + 4)
```

Este mapping sigue `0x8004aa08` y `0x8004ba68`. Para no asumir que el descriptor
de slot equivale al nodo de implementación exacto observado, resolver bajo lock
y comprobar igualdad con el nodo/arena/modelo actuales. Si no coincide, diferir;
no llamar con un puntero antiguo. Alternativa: invertir el nodo actual con
`0x80046d20(arena,node)` y volver a resolverlo bajo el mismo lock, validando
epoch/graph/definición antes de mutar. Esto sigue necesitando una política de
vida de punteros y validación del nodo activo.

`0x80046d3c`: handle 0 -> NULL; handle 1 -> arena+0x58; demás ->
`U32(arena+0x40)+handle-2`. No aplica validación de límites por sí solo.

## Contrato del hook de espera

El receive nativo devuelve 1 sólo con mensaje y 0 al vencer el timeout. El caller
no consulta el retorno y despacha el buffer siempre. El wrapper debe preservar
la espera infinita hacia fuera: receive con timeout corto, servicio entre
esperas, reintentar internamente, retornar sólo ante mensaje real. Nunca entregar
un timeout como mensaje ni inventar opcode. Cinco ticks no son necesariamente
cinco milisegundos; no se ha establecido aquí la frecuencia del tick.

El hook de espera específico del controlador DSP es una ubicación mejor
fundamentada para la reconciliación. Debe dejar procesar los mensajes nativos,
no retener locks durante esperas de otras tareas y diferir si try-lock falla.
No equivale a demostrar que no existe ningún otro productor del grafo.

## Evidencia y límites

`dsp_owner_review.py` verifica literales/BL del binario y ejemplos abstractos de
mutex no recursivo, preflight insuficiente y timeout de receive. Genera
`dsp-owner-static.json` y `dsp-owner-disassembly.txt`. Los checks abstractos
exponen contraejemplos; no ejecutan el firmware ni garantizan un parche.

Sigue pendiente comprobar la implementación integrada y después el cambio de
modelos/presets y Boost en el ampli. No se declara arreglo físico del cuelgue.
