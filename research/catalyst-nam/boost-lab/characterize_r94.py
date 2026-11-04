"""Offline characterization of the EXISTING r94 preamps, not a pedal match.

Reads installed POD Go Edit metadata only. Does not import a device SDK, open
USB, build firmware, change the editor, or modify the selected DSP sources.
The temporary host DLL is used for measurements, not hardware cycle claims.
"""
import ctypes as C
import hashlib
import json
import math
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parent
TV = ROOT.parents[2]
OUT = ROOT / "checks" / "tonal-r94"
OEM = Path("C:/Program Files (x86)/Line6/POD Go Edit/res/distortion.models")
FS = 48000


class Config(C.Structure):
    _fields_ = [(n, C.c_uint32) for n in ("mode", "enabled", "boost", "bright")] + [
        (n, C.c_float) for n in ("drive", "tone", "level")]


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def fundamental(y, hz):
    n = len(y)
    w = 2 * math.pi * hz / FS
    return 2 * math.hypot(sum(x * math.cos(w * i) for i, x in enumerate(y)),
                          sum(x * math.sin(w * i) for i, x in enumerate(y))) / n


def db(v):
    return 20 * math.log10(max(abs(v), 1e-30))


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    protected = [ROOT / "preamp.c", ROOT / "preamp.h",
                 ROOT.parent / "dual-lab/lease-build/combined.elf",
                 TV / "CatalystEditor/Revision-1.0/CatalystCFEditor.exe"]
    before = {str(p.relative_to(TV)): sha(p) for p in protected}
    manifest = json.loads((ROOT.parent / "dual-lab/lease-build/build.json").read_text())
    assert manifest["elf_sha256"] == before[str(protected[2].relative_to(TV))]
    assert manifest["sources"][r"research\catalyst-nam\boost-lab\preamp.c"] == sha(ROOT / "preamp.c")
    wrapper = OUT / "audit-native.c"
    wrapper.write_text('''#include "preamp.c"
__declspec(dllexport) unsigned audit_size(void){return sizeof(PreampState);}
__declspec(dllexport) void audit_init(PreampState *s){preamp_init(s);}
__declspec(dllexport) int audit_config(PreampState *s,const PreampConfig *c){return preamp_configure(s,c);}
__declspec(dllexport) void audit_process(PreampState *s,const float *in,float *out,unsigned n){preamp_process(s,in,out,n);}
''')
    dll = OUT / "preamp-audit.dll"
    zig = TV / ".tools/zig-cross/zig-windows-x86_64-0.13.0/zig.exe"
    env = os.environ.copy()
    env["ZIG_LOCAL_CACHE_DIR"] = str(OUT / "zig-local-cache")
    env["ZIG_GLOBAL_CACHE_DIR"] = str(OUT / "zig-global-cache")
    command = [str(zig), "cc", "-shared", "-O2", "-ffp-contract=off", "-I", str(ROOT),
               str(wrapper), "-o", str(dll)]
    result = subprocess.run(command, env=env, capture_output=True, text=True)
    (OUT / "build.log").write_text(result.stdout + result.stderr)
    if result.returncode:
        raise RuntimeError(result.stderr)
    lib = C.CDLL(str(dll))
    lib.audit_size.restype = C.c_uint32
    lib.audit_config.argtypes = [C.c_void_p, C.POINTER(Config)]
    lib.audit_init.argtypes = [C.c_void_p]
    lib.audit_process.argtypes = [C.c_void_p, C.POINTER(C.c_float), C.POINTER(C.c_float), C.c_uint32]
    state_size = lib.audit_size()

    def process(signal, mode, drive=.5, tone=.5, level=1, boost=0, bright=0):
        state = C.create_string_buffer(state_size)
        lib.audit_init(state)
        config = Config(mode, 1, boost, bright, drive, tone, level)
        assert lib.audit_config(state, C.byref(config)) == 0
        zeros = (C.c_float * 1024)()
        warm = (C.c_float * 1024)()
        lib.audit_process(state, zeros, warm, 1024)
        x = (C.c_float * len(signal))(*signal)
        y = (C.c_float * len(signal))()
        lib.audit_process(state, x, y, len(signal))
        assert all(math.isfinite(v) for v in y)
        return list(y)

    models = json.loads(OEM.read_text())
    names = {"HD2_DistKinkyBoostMono", "HD2_DistScream808Mono", "HD2_DistStuporODMono"}
    selected = [{"symbolicID": x["symbolicID"], "name": x["name"],
                 "parameters": [{k: p[k] for k in ("symbolicID", "name", "default", "min", "max")}
                                for p in x["params"]]}
                for x in models if x["symbolicID"] in names]
    assert len(selected) == 3
    rows = []
    # Half-second steady periodic tail; all frequencies are exact DFT bins.
    freqs = (80, 200, 400, 720, 1000, 2000, 4000, 8000)
    for drive in (0, .5, 1):
        for tone in (0, .5, 1):
            for mode in (1, 2, 3):
                for hz in freqs:
                    amplitude = .0001
                    signal = [amplitude * math.sin(2 * math.pi * hz * i / FS) for i in range(36000)]
                    output = process(signal, mode, drive, tone)[12000:]
                    rows.append(dict(mode=mode, drive=drive, tone=tone, hz=hz,
                                     gain_db=db(fundamental(output, hz) / amplitude)))
    distortion = []
    for drive in (0, .5, 1):
        for amp in (.001, .01, .05, .1, .2, .5):
            signal = [amp * math.sin(2 * math.pi * 400 * i / FS) for i in range(36000)]
            outputs = {}
            for mode in (1, 2, 3):
                y = process(signal, mode, drive)[12000:]
                outputs[mode] = y
                f = fundamental(y, 400)
                harmonics = {str(h): fundamental(y, 400 * h) for h in range(2, 11)}
                distortion.append(dict(mode=mode, drive=drive, input_peak=amp,
                                       fundamental_gain_db=db(f / amp),
                                       h2_dbc=db(harmonics['2'] / max(f, 1e-30)),
                                       h3_dbc=db(harmonics['3'] / max(f, 1e-30)),
                                       thd_db=db(math.sqrt(sum(v*v for v in harmonics.values())) / max(f, 1e-30))))
            # Level-normalized residual, not a measure of perceived fidelity.
            a, b = outputs[2], outputs[3]
            scale = sum(x*y for x, y in zip(a, b)) / max(sum(x*x for x in b), 1e-30)
            residual = math.sqrt(sum((x-scale*y)**2 for x, y in zip(a, b)) / max(sum(x*x for x in a), 1e-30))
            distortion[-1]["808_sd1_level_matched_residual_db"] = db(residual)
    same = {str(p.relative_to(TV)): sha(p) for p in protected} == before
    assert same
    report = dict(status="CHARACTERIZED_CURRENT_R94_ONLY", sample_rate=FS, state_bytes=state_size,
                  hardware_accessed=False, USB_opened=False, SDK_loaded=False,
                  firmware_created=False, editor_replaced=False, protected_unchanged=same,
                  source_sha256=before, host_dll_sha256=sha(dll),
                  OEM_metadata=dict(path=str(OEM), sha256=sha(OEM), models=selected,
                                    proves="Control names/defaults/ranges only; not audio algorithm"),
                  linear_response=rows, nonlinear_response=distortion,
                  limitations=["No POD Go processed reference audio captured or rendered",
                               "No real-pedal fidelity claim; no listening equivalence test",
                               "Host DLL arithmetic is not an ARM hardware timing measurement",
                               "Input amplitudes are digital values, not calibrated guitar voltage"])
    (OUT / "result.json").write_text(json.dumps(report, indent=2) + "\n")
    for mode in (1, 2, 3):
        values = {r['hz']: round(r['gain_db'], 3) for r in rows
                  if r['mode'] == mode and r['drive'] == .5 and r['tone'] == .5}
        print("mode", mode, "linear gain dB, Drive/Tone50%", values)
    for row in distortion:
        if row['mode'] == 3 and row['drive'] == .5:
            print("808/SD1 residual", row['input_peak'], round(row['808_sd1_level_matched_residual_db'], 2), "dB")
    print(json.dumps({k:report[k] for k in ('status', 'protected_unchanged', 'hardware_accessed', 'firmware_created')}))


if __name__ == "__main__":
    main()
