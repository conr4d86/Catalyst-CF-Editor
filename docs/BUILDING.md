# Building and source layout / Compilación y fuentes

This document is for developers. To use the released application, download
`CatalystCFEditor.exe`; users do not need to compile an editor or firmware.

## Scope of this source snapshot

The tree contains the desktop editor's C# source, the corresponding original C#
for its embedded legacy host components, resource definitions, the NAM importer,
IR converter, and the C/Python/assembly sources used for the corrected r105
all-model audio adapter, DSP control owner, resource owner and firmware assembly
pipeline. Generated **source** translation units and headers are included
alongside their generators. Earlier adapters remain as generator inputs and
negative regression fixtures; their inclusion does not select them for release.

`SOURCE-INVENTORY.json` lists the curated project files and their SHA256 hashes.
The source layout retains the original relative `research/` structure so that
includes and local module imports remain understandable.

**This is not a self-contained, one-command reproduction of the complete EXE.**
Manufacturer runtime components, firmware images, demo capture/IR payloads and
private verification artifacts are not part of this source tree. The distributed
single EXE contains its runtime resources; their presence in that EXE does not
mean that the manufacturer's source code is available here.

## Source map

| Directory | Contents |
| --- | --- |
| `research/catalyst-editor/` | Editor, protocol clients, resource preparation, installer coordination and offline test sources |
| `research/catalyst-editor/resources/` | Generated C# metadata, control catalogue, translations and source artwork |
| `research/catalyst-editor/family-source/` | Corresponding source for the embedded family-aware legacy host component |
| `research/catalyst-editor/cf-transport-build/source/` | Corresponding source for the embedded initial-install transport component |
| `research/catalyst-editor/legacy-pinned-sources/` | Decoder source pinned by the unchanged legacy components |
| `research/catalyst-ir/` | Original host codecs, filters, level preparation and container tooling needed by those components |
| `research/catalyst-nam/app/` | NAM model importer and original packaging source |
| `research/catalyst-nam/build-fast/span-max-r94/` | Optimized audio-kernel C translation unit consumed by the selected corrected build |
| `research/catalyst-nam/dual-lab/lease-build/` | Earlier adapter source and source tables used by the all-model generator |
| `research/catalyst-nam/all-amps-lab/` | All-model adapter/actor generators, generated C and offline verification code |
| `research/catalyst-nam/all-amps-lab/control-owner-lab/` | Selected DSP-owner C/assembly, generated adapter and actor source, binding headers and linker scripts |
| `research/catalyst-nam/all-amps-lab/podgo-review/` | Static native task/mutex ABI provenance and its read-only verification script |
| `research/catalyst-nam/asset-lab/` | Serialized resource store, transfer protocol, recovery and owner sources |
| `research/catalyst-nam/boost-lab/` | Preamp processing and preset sidecars; `tonal-v2/` contains the selected pedal implementation |
| `research/catalyst-nam/external/NeuralAmpModelerCore/` | Official NAM Core preferred source used as the host comparison reference, with its MIT licence |

The official comparison reference is pinned to commit
`0b3d3c97b0859a3a8c92a8628c4dd89a25eb5842`. See
[`third_party/NAM-PROVENANCE.json`](../third_party/NAM-PROVENANCE.json).
The project kernel sources and the upstream reference retain their own licence
notices. Manufacturer components, model captures and artwork are separate works;
do not assume a source licence grants rights to every external input.

## Toolchain

- Windows and the Microsoft .NET Framework C# compiler/WinForms reference assemblies.
  The maintainer build uses the x86 compiler at
  `%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe`.
- Python 3. The delivered build was prepared with Python 3.14.
- Zig 0.13.0 for the freestanding ARM Cortex-M7 hard-float C/assembly build.
  Existing scripts expect `.tools/zig-cross/zig-windows-x86_64-0.13.0/zig.exe`
  relative to the checkout root. Compiler installations and caches are excluded.
- For ARM emulation and inspection: Unicorn 2.1.4 and Capstone 5.0.9.
  Some historical scripts add a local `tools-recovery` directory to `sys.path`;
  an ordinary Python environment with those packages can supply the imports.
- For the optional official C++ host comparator: the upstream reference's Eigen,
  nlohmann JSON and AudioDSPTools dependencies. These header/source trees are
  external; use the upstream `.gitmodules` and the pinned provenance information.

Windows Media Foundation is used by the optional audio decoder fallback and
the synthetic FLAC check. None of these tools is an audio plugin required to play
through the released amplifier firmware.

## External inputs for the complete single-EXE build

`research/catalyst-editor/build.py` prepares resources and compiles the WinForms
host. Its omitted inputs include:

1. The manufacturer editor/updater runtime bundles and SDK assemblies.
2. The original official firmware container and its extracted analysis segment,
   runtime maps and model database used by the firmware verification pipeline.
3. The legacy embedded component binaries and the reports which pin their source
   hashes. Their corresponding original C# is included; their proprietary runtime
   dependencies are not.
4. Knob/amplifier manufacturer graphics used by the historical resource build.
5. The NAM and IR seed payload bundle. No private capture, commercial IR, demo
   waveform or model weights are included in this source snapshot.
6. Generated compiler/emulator reports used by the freshness checks. They are
   build outputs, not substitute source inputs or hardware approval.

Obtain manufacturer inputs from the manufacturer's own distribution, keep them
outside the public source package, and reconstruct the expected local resource
tree before attempting the complete build. Do not disable hash, identity,
memory-bound or product checks to make an incomplete checkout compile.

## Maintainer pipeline for this engine revision

The historical top-level editor builder still describes an earlier base engine.
The corrected engine is selected by `build_control_app.py`. It removes native
graph setters from audio and reconciles the OEM Boost state on the native DSP
control task, using a nonblocking outer-mutex acquisition and a current Amp slot
resolved under that mutex. The shared node wrapper preserves requested values;
the native loader hook invalidates state when a model is loaded again, including
reuse at the same address. These are implementation properties, not a substitute
for a physical preset-change test.
Some internal helper names retain `main`; their stack/queue/IPSR guards and the
installed wait hook select `DspSchedulerCtrlTask`, rather than UserMode/Main.

After restoring the required external inputs and rebuilding prerequisite
reference artifacts, the selected stages are:

```powershell
python research/catalyst-nam/all-amps-lab/build_control_owner.py
python research/catalyst-nam/all-amps-lab/build_control_actor.py
python research/catalyst-nam/all-amps-lab/build_control_boot.py
python research/catalyst-nam/all-amps-lab/verify_control_native.py
python research/catalyst-nam/all-amps-lab/build_control_app.py --bootstrap
```

The bootstrap executable has installation disabled. Its fake-backend firmware UI
check prepares an exact package without sending it to a device. The next gate
runs `verify_control_owner.py --package <offline-package.ctf> --output <folder>`
on that package. `verify_control_variant.py` checks the audio, actor, native-control,
reset, source hashes and exact-package owner results before
`build_control_app.py` can create the enabled user-trial executable.

The actor and reset/owner verifiers also use earlier locally built reference
artifacts as invariants. This snapshot intentionally excludes those binaries,
hardware reports and private package/demo inputs. These commands are **not** a
claim that a clean download can reconstruct the release in isolation. Inspect
the scripts for prerequisites and rebuild the corresponding originals first.

`build_all_amps.py`, `build_actor_variant.py`, `build_trial_app.py` and their
earlier verification scripts are retained as historical generator/regression
inputs. In particular, the old all-model binary is expected to fail the new
regression if it attempts a native graph setter from audio. Do not substitute
that older segment for the selected `control-owner-lab` segment.

The explicit product allowlist and physical-test status inside these build gates
describe their actual validation scope. They do not establish new hardware
coverage. Regenerate verification results after a source change rather than
editing a report or weakening a gate to force a pass.

## Host upgrade path

`CFUpgradeBackup.cs` validates a local archive and its repeated-read evidence
against the full edition identified on the connected amplifier. The coordinator's
upgrade path resolves that archive before acquiring an installation connection;
it does not repeat the older firmware's preset-selection backup scan. Missing,
changed or ambiguous evidence fails closed. `CFInstallChecks.cs` covers this
behavior with fake transports and synthetic backup files; no personal backup is
included in this snapshot.

Reinstallation creates a new resource edition with the bundled demo slots. The
older edition's stored resources are archival, not a migration of the active NAM
and IR slots. Backup reuse must not be described as preserving active capture
content or as taking a fresh snapshot of recent preset edits.

## Useful offline checks

### Standalone IR converter

This check needs only the included converter source, the .NET compiler, Python
and Windows audio decoding support. From the checkout root:

```powershell
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$irRoot = Join-Path (Get-Location) 'research\catalyst-editor'
$irChecks = Join-Path $irRoot 'checks\ir-import'
New-Item -ItemType Directory -Path $irChecks -Force | Out-Null
& $compiler /nologo /target:exe /reference:System.Web.Extensions.dll `
  ('/out:' + (Join-Path $irChecks 'IRImportProbe.exe')) `
  (Join-Path $irRoot 'IRImport.cs') `
  (Join-Path $irRoot 'IRMediaFoundation.cs') `
  (Join-Path $irChecks 'IRImportProbe.cs')
if ($LASTEXITCODE -ne 0) { throw 'Converter probe compilation failed' }
python research/catalyst-editor/verify_ir_import.py
```

The script generates synthetic WAV/AIFF/FLAC fixtures, checks conversion,
anti-aliasing, terminal-chunk handling and rejection of malformed input. It does
not load the device SDK or open USB. Optional user-supplied regression fixtures
can be configured with `CATALYST_LONG_IR_FIXTURE` and
`CATALYST_IR_REGRESSION_FIXTURE`; no example path or private file is shipped.

### Editor checks with a prepared development executable

After the complete editor build is available, these development switches use
synthetic/fake backends:

```powershell
& .\CatalystCFEditor.exe --self-test .\checks\self-test.json
& .\CatalystCFEditor.exe --interaction-test .\checks\interaction.json
& .\CatalystCFEditor.exe --firmware-access-test .\checks\access
& .\CatalystCFEditor.exe --native-preamp-test .\checks\native-preamp.json
```

Create the output folder before running file-based checks. Test switches are
development tooling, not the normal user flow. The firmware-UI and resource
workflow checks additionally accept a locally supplied valid A2 Lite `.nam` file.

The `--cf-install-test` switch uses a simulated service but needs local firmware
fixtures. In contrast, switches containing `--install-hardware-trial`,
`--recover-hardware-trial`, `--send-...`, `--connection-check` and similar names
can access a real device. Do not use hardware tooling as a substitute for an
offline test, or while another Line 6 application owns the connection.

`verify_control_native.py` runs the pinned native load/set/dispatch routines in
emulation and covers twelve model signatures, mutex-busy deferral, same-address
reloads, inactive graph controls, mailbox generations, original queue-message
semantics and failure paths. RTOS/mutex/IPSR behavior is explicitly mocked.
`verify_control_owner.py` covers the packaged host/ARM owner path. Neither test
establishes silicon scheduling, interrupt stack usage, audible quality or a
successful physical installation. The optional static ABI script under
`podgo-review/` reads the original firmware image and writes only local analysis
outputs; those outputs are not distributed here.

Local build, emulation, USB transfer and audible hardware behavior are distinct
results. Source availability and matching hashes do not establish model support
or a successful physical installation.

## Español

Este árbol contiene las fuentes correspondientes del editor, importador NAM,
convertidor IR, preamps y motor r105 corregido, además de los generadores y sus
fuentes C/ensamblador generadas. La ruta seleccionada está en
`all-amps-lab/control-owner-lab`; los adaptadores anteriores se conservan como
entradas y regresiones, no como firmware seleccionado. No contiene SDK/DLL del
fabricante, firmware, capturas NAM, IR privados,
copias de presets, registros de pruebas ni herramientas instaladas.

Por ello, la compilación completa del EXE requiere preparar por separado esos
componentes externos. No se anuncia una compilación pública autosuficiente ni una
CI que reconstruya el motor sin ellos. El usuario de la aplicación publicada solo
necesita el EXE y, si Windows lo requiere para detectar la unidad, el controlador
oficial del amplificador.

La prueba independiente del convertidor descrita arriba genera sus propios
archivos sintéticos y no usa el USB. Los cambios de ruta en cuatro pruebas del
snapshot eliminan referencias locales y conservan su intención; están anotados
en `SOURCE-INVENTORY.json`. Las fuentes preferidas del núcleo y sus avisos de
licencia se conservan sin atribuirles licencias ajenas.
