# Validation status / Estado de validación

One-unit physical observations and local checks are distinguished below.
This report contains no device identifiers, personal presets or capture data.

## v1.0 publication — 2026-11-04

The project owner authorized publication after the r103 delivery and requested
the data-connection fix now included in the r104 host executable.
Publication approval is distinct from a complete hardware validation matrix.
The running CF identity on the development unit matched its completed initial
installation record. A full listening matrix for the all-model engine is not
documented. Initial installation is enabled for 60/CX60;
100/200 installation remains disabled and those models have not been physically tested.

- Original ARM audio graph emulation: EP, 808, SD-1 and factory Boost checked on
  all twelve original model IDs. Native Boost bypass/restore and input-buffer
  restoration passed. NAM 1/2 output parity passed for identical captures.
- Packaged reset copies, memory bounds, owner loop, resource persistence and
  production host protocol passed offline checks. These do not establish physical
  USB timing, interrupt/cache behavior or real-time audio stability.
- NAM input is adjustable, default −5 dB; output defaults to manual −3 dB.
  Both embedded demo NAM positions use identical prepared payloads.
- Final odd WAV metadata without an EOF alignment byte is accepted only when
  the complete chunk ends at EOF. Truncated payloads remain rejected. The reported
  IR passes both file loading and drag/drop without modifying its source.
- Preset-save revision conflicts have bounded volatile retries. Persistent SAVE
  and uncertain operations are not blindly repeated. Fault-injection checks passed.
- The release EXE passed eleven local test groups: access gating, firmware UI,
  resource workflow, system UI, native preamp protocol, simulated installation,
  interaction, self-test, persistent journal, MIDI exchange and data session.
  These tests did not open USB or load the device SDK.
- A separate read-only production data-channel check on the development 60/CX60
  completed identity/HELLO in 256 ms, NAM 2 metadata in 2,945 ms and IR1 metadata
  in 9,072 ms. The former 6 s metadata deadline was shorter than the observed IR
  read. The host now allows a single bounded 30 s scan and connects using only
  the selected slot, without prefetching four unrelated slots. Closure was
  confirmed. No firmware, NAM/IR payload or preset was written by this check.
  These timings describe this unit and operation, not a general latency promise.

Earlier physical observations below belong to the earlier r95 engine.

| Check | Evidence | Status |
| --- | --- | --- |
| r95 engine installation | Confirmed transfer/closure; user confirmed normal boot and sound | One Catalyst 60/CX60 |
| Resources preserved | Five pre-update slot receipts matched the prepared image | Same payloads and metadata |
| NAM uploads over USB | Earlier transfer/readback and user listening result | Observed on the development unit |
| EP with NAM 2 | 32 reads over 31.92 s; ~1499.97 blocks/s | No added late blocks or recorded NAM/preamp faults |
| 808 with NAM 2 | 32 reads over 31.86 s; ~1499.95 blocks/s | No added late blocks or recorded NAM/preamp faults |
| SD-1 with NAM 2 | 32 reads over 31.83 s; ~1499.99 blocks/s | No added late blocks or recorded NAM/preamp faults |
| r95 listening | User reported that it sounds very good | Positive report; no POD Go fidelity comparison |
| New preamp layout | 40 cases, two NAM paths, four types, five scales | Local containment/non-overlap checks passed |
| 125% and 160% panel sizes | Component tested separately when the whole window exceeded the test desktop | Panel checks only at those scales |
| Save destination update | Fresh SDK read; 12 target cases and invalid/racing-state rejection | Local checks passed; manual save pending |
| Firmware CF interface | One install action; NAM positions first; adjacent numbered load/connect/send actions | Local mode, ownership and proportional layout checks passed |
| Embedded demo package | Identical NAM payloads and metadata; three IRs with 80 Hz / 8 kHz cuts; complete image round-trip | Offline validation only; no physical demo installation |
| Firmware typography | Larger scoped fonts, no clipped controls, footer v1.0 | Local checks; manual review pending |
| Full manual app review, new pedal persistence and all settings/outputs | No complete matrix yet | Pending |
| Catalyst 100/200 hardware | Family handling exists in code | Not physically tested |

Measured dispatch cycles exclude work outside those dispatches. They do not
prove worst-case capacity. Numeric model checks, timing, transfer success and
audible behavior are different evidence. This is not a zero-failure guarantee.

## Español

R95 arrancó y produjo audio en una unidad 60/CX60. Los tres preamps mantuvieron
NAM 2 activo durante las ventanas medidas y el usuario comunicó un buen resultado
sonoro. El autor autorizó publicar v1.0 el 4 de noviembre de 2026. Las pruebas
locales y la lectura física del host r104 no equivalen a una matriz completa de sonido, guardado,
persistencia y salidas en el hardware ni a pruebas físicas en los modelos 100/200.

## Revision r99 — 2026-11-04

Local checks passed for all six main views, 15 resource/scale cases, dynamic
captions, Spanish/English switching without device writes, tuner validity,
BPM placement, footer layout, dark-log scrolling, deferred preparation after
connection, data workflow, preset target guards and mock installation/receipts.
The packaged owner loop passed ARM emulation. Boot/code/native metadata match
the physically observed r95 engine; only seed data, manifest and edition vary.
Initial installation is enabled only for product2424833 (60/CX60). This does not
claim general memory/cache/IRQ certification or a physical installation of the
new demo package. Manual r99 review is still pending.
