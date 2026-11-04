# Validation status / Estado de validación

Local checks, physical transfer, startup and listening are separate results.
This document contains no private device identifiers, presets or capture data.

## Current v1.0 preparation — host r106, 2026-11-04

The project owner authorized publication. The user confirmed both physical CH A/B
changes with apps closed and GUI A → B → A changes with working audio and controls.
Preamp-save/cold-boot persistence and the complete hardware matrix remain pending.
Initial installation is enabled for **Catalyst 60/CX60 only**. Catalyst 100/200
have not been physically tested and their initial installation remains disabled.

| Check | Result | Scope |
| --- | --- | --- |
| Host executable | r106; SHA256 below | Exact executable tested locally |
| Host checks | 13 groups passed; no SDK/USB opened | Capability gating, preset selection, journal, MIDI, data session, access, firmware UI, data workflow, system UI, native preamp protocol, installation fixture, interaction and self-test |
| Corrected ARM control owner | Native model load/set/audio code exercised for all twelve model signatures | Emulation with explicit RTOS/mutex/IPSR mocks |
| Regression against earlier all-model code | Earlier binary rejected when it calls the native graph setter from audio | Negative regression, not a physical test |
| Model lifecycle and locking | Same-address reload, FX-only load, inactive-node controls, changed generation, busy mutex and release failure checked | Local native-code regression |
| Queue contract | Only real messages reach the original dispatcher; finite/foreign waits preserved | Local checks |
| Control-call stack | Lowest emulated SP 0x80806a58; 1,768 bytes above the task stack base in tested cases | Excludes real IRQ/preemption and is not a silicon watermark |
| Exact package owner/reset | Host/ARM resource owner, packaged copies, source hashes and memory bounds passed | Offline exact-package checks |
| Upgrade backup | Full installed edition and archive/read hashes matched; missing, changed or ambiguous records rejected before installation connection | Fake transport and synthetic files; no repeated preset scan |
| Physical corrected-CF transfer | Transfer completed and updater closure confirmed | One development Catalyst 60/CX60 |
| Normal startup | User confirmed lights and sound after restarting | Same unit |
| Runtime diagnostics | Read-only FS/audio/control observations progressed without a recorded fault | Same unit; neither a write nor an all-model listening test |
| Physical CH A/B preset changes | Passed: user confirmed switching and continuing sound | Same unit, all apps closed; this does not test GUI switching |
| GUI A → B → A preset changes | Passed: user confirmed switching with working audio and controls | Same unit, current r106 editor workflow |
| Preamp save and cold-boot persistence | Pending | Current r106 editing workflow; normal startup alone does not validate saved settings |
| Full controls, outputs, settings and listening matrix | Not complete | One-unit observations do not establish all-model coverage |
| Catalyst 100/200 | Not physically tested | Initial installation disabled |

The graph setter is removed from the audio callback. Native Boost changes are
reconciled on the original DSP control task, with the native outer mutex and
current Amp descriptor resolved under that mutex. Source-level ownership and
emulation results do not alone establish real scheduling or audible stability.

The two demo NAM positions contain identical prepared payloads, with adjustable
input initially **−5 dB** and independent output initially **manual −3 dB**.
The three demo IRs have **80 Hz / 8,000 Hz** cuts. Reinstallation creates a new
edition and activates these five examples; archived resources from an earlier
edition are not migrated automatically.

### Exact artifact fingerprints

These identify files, not a guarantee of hardware behavior.

```text
CatalystCFEditor.exe (r106)
a2595487dded2c2df297781b7009458e42a7365a1876dcce6bb2a9f7d5b79a16
Audio ELF
a652621cc772759d5a0912cff1fd4d9c383410d4c3737f13a3e2a3bb98237c91
Actor ELF
329cb0dfb8164200040ccc8092a583e81c9eb4e63ca4e1850f4b988452080654
Firmware segment
a4f5c1038787b91a2c0272d1f64cb6d55bfba3bc58d0b67bcba875aa9f7c5ff8
```

## Earlier observations and retained checks

The earlier r95 engine booted and produced audio on the development 60/CX60.
Earlier NAM uploads and listening were successful there; EP, 808 and SD-1 each
maintained roughly 1,500 audio blocks/s during their recorded short windows.
These observations belong to that earlier engine, not a complete physical matrix
for the corrected engine above.

IR conversion checks cover decodable WAV/AIFF/AIFC/FLAC, resampling, anti-aliasing,
malformed input and a complete odd terminal WAV chunk missing its final alignment
byte. Truncated payloads remain rejected. Loading and drag/drop share the same
conversion path and do not modify original files.

Preset-save conflicts use bounded volatile retries. Persistent SAVE, uncertain
uploads and uncertain installations are not blindly repeated. The data client
connects using the selected slot rather than scanning four unrelated positions;
metadata operations have a bounded 30-second deadline. This is not a promised
latency on every PC or device.

Measured dispatch cycles omit work outside those dispatches. Local checks,
matching hashes, successful transfer, normal boot and audible behavior each
provide different evidence. No zero-failure or physical 100/200 guarantee is made.

## Español

La preparación actual utiliza el host **r106**, con **13 grupos locales correctos**
sin abrir SDK ni USB. El motor corregido evita cambiar el grafo desde audio y usa
el propietario de control DSP con su mutex nativo. Las regresiones en emulación
cubren doce modelos, recargas con las mismas direcciones, cambios de efectos,
generaciones y errores de sincronización.

La transferencia del CF corregido terminó en una unidad **60/CX60**, y el usuario
confirmó luces y sonido después del reinicio. Las lecturas de diagnóstico
posteriores mostraron actividad sin fault registrado. **El usuario confirmó cambios
físicos con CH A/B, con las apps cerradas y manteniendo el sonido.** También confirmó
A → B → A desde el editor, con audio y controles funcionando. No se declara completa
la matriz de audio y controles. Los 100/200 no están probados
físicamente y su instalación permanece deshabilitada.
También siguen pendientes el guardado del preamp y la persistencia tras apagar
y encender dentro del flujo actual r106.

Cuando la app indica que una revisión anterior necesita actualizarse, utiliza una
copia local verificada de esa edición en vez de recorrer sus presets otra vez.
Si falta o no coincide, se detiene antes de la conexión de instalación. Esto no es
una copia nueva de los últimos ajustes. Reinstalar crea una edición nueva con
los dos NAM y tres IR de prueba activos; los anteriores quedan archivados, sin
migración automática. Consulta [Uso](USO.md), [FAQ](FAQ.md) y
[Recuperación](RECUPERACION.md).
