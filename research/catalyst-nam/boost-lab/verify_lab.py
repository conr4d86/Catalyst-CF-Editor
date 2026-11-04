"""Build and test an independent ARM-only overdrive feasibility prototype.

Nothing is linked into the editor or the Catalyst CTF. No hardware APIs exist
in this script. Instruction counts are not clock cycles or real-time proof.
The algorithms are tonal approximations, not validated pedal models.
"""
import ctypes
import hashlib
import json
import math
import os
import random
import struct
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
TV = ROOT.parents[2]
NAM = ROOT.parent
TOOLS = TV / 'research' / 'catalyst-editor' / 'tools'
sys.path[:0] = [str(TOOLS / 'emulation'), str(NAM)]
from unicorn import Uc, UC_ARCH_ARM, UC_MODE_THUMB, UC_MODE_MCLASS, UC_HOOK_CODE
from unicorn.arm_const import *
from build import elf

OUT = ROOT / 'build'
STATE, CONFIG, INPUT, OUTPUT = 0x20000100, 0x20000400, 0x20001000, 0x20005000
STACK, STOP = 0x2000f000, 0x17ff0


class Config(ctypes.LittleEndianStructure):
    _fields_ = [(n, ctypes.c_uint32) for n in ('mode', 'enabled', 'boost', 'bright')] + [
        (n, ctypes.c_float) for n in ('drive', 'tone', 'level')]


def floats(raw):
    return struct.unpack('<%df' % (len(raw) // 4), raw)


def packed(values):
    return struct.pack('<%df' % len(values), *values)


def amplitude(signal, hz):
    w = 2 * math.pi * hz / 48000
    real = sum(v * math.cos(w * n) for n, v in enumerate(signal))
    imag = sum(v * math.sin(w * n) for n, v in enumerate(signal))
    return 2 * math.hypot(real, imag) / len(signal)


def direct_reference(signal, mode):
    """Same preliminary shaping at 1x, with matched first-order pole rates.

    Only a spectral baseline for the resampler, not a real-pedal reference.
    """
    emphasis = tone_z = dc_in = dc_out = 0.0
    result = []
    def soft(x, p, n):
        limit = n if x < 0 else p
        y = max(-1, min(1, x / limit))
        return limit * (y - y * y * y / 3)
    for x in signal:
        if mode == 1:
            y = soft(x * 10, 2, 1.8)
        else:
            emphasis += (1 - (1 - .045) ** 2) * (x - emphasis)
            y = soft((x - emphasis) * 31, 1, 1) if mode == 2 else soft((x - emphasis) * 31, .55, .9)
        tone_z += (1 - (1 - .38) ** 2) * (y - tone_z)
        y = tone_z - dc_in + (.99947655 ** 2) * dc_out
        dc_in, dc_out = tone_z, y
        result.append(y)
    return result


def compile_lab():
    OUT.mkdir(exist_ok=True)
    # Hamming-window halfband; make exact zeros and normalize the DC gain.
    h = []
    for k in range(15):
        n = k - 7
        ideal = 0.5 if n == 0 else (0.0 if n % 2 == 0 else math.sin(math.pi * n / 2) / (math.pi * n))
        h.append(ideal * (0.54 + 0.46 * math.cos(math.pi * n / 7)))
    # Exact halfband center/DC constraints keep a constant input constant in
    # both interpolation phases; normalizing every tap would modulate DC.
    side_sum = sum(v for k, v in enumerate(h) if k != 7)
    h = [0.5 if k == 7 else v * 0.5 / side_sum for k, v in enumerate(h)]
    assert abs(sum(h) - 1) < 1e-12
    assert h[7] == .5 and abs(sum(h[::2]) - .5) < 1e-12
    assert all(h[k] == 0 for k in (1, 3, 5, 9, 11, 13))
    (OUT / 'halfband.h').write_text('static const float halfband[15] = {' +
                                  ','.join(f'{v:.12e}f' for v in h) + '};\n')
    zig = TV / '.tools' / 'zig-cross' / 'zig-windows-x86_64-0.13.0' / 'zig.exe'
    env = os.environ.copy()
    env['ZIG_GLOBAL_CACHE_DIR'] = str(OUT / 'zig-global-cache')
    env['ZIG_LOCAL_CACHE_DIR'] = str(OUT / 'zig-local-cache')
    commands = [[str(zig), 'cc', '-target', 'thumb-freestanding-eabihf',
                 '-mcpu=cortex_m7+fp_armv8d16sp', '-mfpu=fpv5-sp-d16',
                 '-mfloat-abi=hard', '-Os', '-ffp-contract=off',
                 '-ffreestanding', '-fno-builtin', '-fno-stack-protector',
                 '-I', str(OUT), '-c', str(ROOT / 'preamp.c'), '-o', str(OUT / 'preamp.o')],
                [str(zig), 'build-exe', str(OUT / 'preamp.o'), '-target', 'thumb-freestanding-eabihf',
                 '-mcpu', 'cortex_m7+fp_armv8d16sp', '--script', str(ROOT / 'lab.ld'),
                 '-fentry=preamp_process', '--no-gc-sections', '-fno-strip', '-fno-compiler-rt', '-O', 'ReleaseSmall',
                 '-femit-bin=' + str(OUT / 'preamp.elf')]]
    for i, command in enumerate(commands):
        r = subprocess.run(command, capture_output=True, text=True, env=env, cwd=ROOT)
        (OUT / f'build-{i}.log').write_text(r.stdout + r.stderr)
        if r.returncode:
            raise RuntimeError(r.stdout + r.stderr)
    segments, symbols = elf(OUT / 'preamp.elf')
    unresolved = [n for n, s in symbols.items() if s['index'] == 0 and s['info'] >> 4 in (1, 2)]
    assert not unresolved, unresolved
    assert all(s['address'] + s['size'] < STOP for s in segments)
    return segments, symbols


class Machine:
    def __init__(self, segments, symbols):
        self.symbols = symbols
        self.u = u = Uc(UC_ARCH_ARM, UC_MODE_THUMB | UC_MODE_MCLASS)
        u.ctl_set_cpu_model(UC_CPU_ARM_CORTEX_M7)
        u.mem_map(0x10000, 0x8000)
        u.mem_map(0x20000000, 0x10000)
        for s in segments:
            u.mem_write(s['address'], s['data'])
        u.reg_write(UC_ARM_REG_C1_C0_2, 0xf << 20)
        u.reg_write(UC_ARM_REG_FPEXC, 0x40000000)
        self.size = self.call('preamp_state_bytes')
        assert self.size <= 228, self.size
        u.mem_write(STATE - 32, b'\xa5' * 32)
        u.mem_write(STATE + self.size, b'\x5a' * 32)
        self.call('preamp_init', STATE)

    def call(self, name, *args):
        u = self.u
        u.reg_write(UC_ARM_REG_SP, STACK)
        u.reg_write(UC_ARM_REG_LR, STOP | 1)
        for r, value in zip((UC_ARM_REG_R0, UC_ARM_REG_R1, UC_ARM_REG_R2, UC_ARM_REG_R3), args):
            u.reg_write(r, value)
        saved = [UC_ARM_REG_R4, UC_ARM_REG_R5, UC_ARM_REG_R6, UC_ARM_REG_R7,
                 UC_ARM_REG_R8, UC_ARM_REG_R9, UC_ARM_REG_R10, UC_ARM_REG_R11]
        fp = [UC_ARM_REG_S16, UC_ARM_REG_S17, UC_ARM_REG_S18, UC_ARM_REG_S19,
              UC_ARM_REG_S20, UC_ARM_REG_S21, UC_ARM_REG_S22, UC_ARM_REG_S23,
              UC_ARM_REG_S24, UC_ARM_REG_S25, UC_ARM_REG_S26, UC_ARM_REG_S27,
              UC_ARM_REG_S28, UC_ARM_REG_S29, UC_ARM_REG_S30, UC_ARM_REG_S31]
        for i, r in enumerate(saved): u.reg_write(r, 0xabcd0000 + i)
        for i, r in enumerate(fp): u.reg_write(r, 0x3f000000 + i)
        u.emu_start(self.symbols[name]['address'] | 1, STOP, count=5000000)
        assert u.reg_read(UC_ARM_REG_PC) == STOP
        assert u.reg_read(UC_ARM_REG_SP) == STACK
        assert [u.reg_read(r) for r in saved] == [0xabcd0000 + i for i in range(8)]
        assert [u.reg_read(r) for r in fp] == [0x3f000000 + i for i in range(16)]
        if hasattr(self, 'size'):
            assert bytes(u.mem_read(STATE - 32, 32)) == b'\xa5' * 32
            assert bytes(u.mem_read(STATE + self.size, 32)) == b'\x5a' * 32
        return u.reg_read(UC_ARM_REG_R0)

    def configure(self, mode, enabled=1, drive=0.5, tone=0.5, level=1.0, boost=0, bright=0):
        c = Config(mode, enabled, boost, bright, drive, tone, level)
        self.u.mem_write(CONFIG, bytes(c))
        rc = self.call('preamp_configure', STATE, CONFIG)
        assert bytes(self.u.mem_read(CONFIG, ctypes.sizeof(c))) == bytes(c)
        return rc

    def process(self, signal, chunks=(32,), inplace=False):
        result = []
        pos = step = 0
        while pos < len(signal):
            n = min(chunks[step % len(chunks)], len(signal) - pos)
            raw = packed(signal[pos:pos + n])
            self.u.mem_write(INPUT, raw)
            destination = INPUT if inplace else OUTPUT
            self.u.mem_write(INPUT - 16, b'\xa3' * 16)
            self.u.mem_write(INPUT + len(raw), b'\x3a' * 16)
            if not inplace:
                self.u.mem_write(OUTPUT - 16, b'\xa4' * 16)
                self.u.mem_write(OUTPUT + len(raw), b'\x4a' * 16)
            self.call('preamp_process', STATE, INPUT, destination, n)
            assert bytes(self.u.mem_read(INPUT - 16, 16)) == b'\xa3' * 16
            assert bytes(self.u.mem_read(INPUT + len(raw), 16)) == b'\x3a' * 16
            if not inplace:
                assert bytes(self.u.mem_read(INPUT, len(raw))) == raw
                assert bytes(self.u.mem_read(OUTPUT - 16, 16)) == b'\xa4' * 16
                assert bytes(self.u.mem_read(OUTPUT + len(raw), 16)) == b'\x4a' * 16
            result.extend(floats(bytes(self.u.mem_read(destination, n * 4))))
            pos += n
            step += 1
        assert all(math.isfinite(v) for v in result)
        return result


def main():
    protected = [NAM / 'autoboot.c', NAM / 'app/resources/nam-engine.ctf',
                 TV / 'CatalystEditor/Development/CatalystCFEditor.exe']
    before = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}
    segments, symbols = compile_lab()
    machine = lambda: Machine(segments, symbols)
    rng = random.Random(80211)
    signal = list(floats(packed([rng.uniform(-1.2, 1.2) for _ in range(1024)])))
    bypass = machine()
    assert packed(bypass.process(signal)) == packed(signal)
    signed = [-0.0, 0.0, -1e-38, 1e-38]
    assert packed(bypass.process(signed)) == packed(signed)
    state = bytes(bypass.u.mem_read(STATE, bypass.size))
    bypass.call('preamp_process', STATE, INPUT, OUTPUT, 0)
    assert bytes(bypass.u.mem_read(STATE, bypass.size)) == state
    assert bypass.configure(4) == 0xffffffff
    assert bypass.configure(2, drive=float('nan')) == 0xffffffff
    assert bytes(bypass.u.mem_read(STATE, bypass.size)) == state
    rows = []
    for mode in range(4):
        for drive, tone, level in ((0, 0, 0), (0, 1, 1), (1, 0, 1), (1, 1, 2), (.5, .5, 1)):
            m = machine(); assert m.configure(mode, drive=drive, tone=tone, level=level) == 0
            y = m.process(signal)
            b = machine(); assert b.configure(mode, drive=drive, tone=tone, level=level) == 0
            assert packed(b.process(signal, chunks=(1, 7, 13, 32, 5, 64), inplace=True)) == packed(y)
            # Transparent Boost x2 and Level x2 intentionally reach x4;
            # the stress input spans +/-1.2, so its valid bound is 4.8.
            assert max(map(abs, y)) <= (4.80001 if mode == 0 else 4), (mode, drive, tone, level, max(map(abs, y)))
            if level == 0: assert y[128:] == [0.0] * (len(y) - 128)
            assert m.configure(mode, enabled=0) == 0
            tail = m.process(signal[:256])
            assert packed(tail[128:]) == packed(signal[128:256])
            rows.append(dict(mode=mode, drive=drive, tone=tone, level=level,
                             output_peak=max(map(abs, y)), chunk_and_inplace_identical=True))
    # Transparent mode keeps the current 0..6-dB boost law exactly after fade.
    m = machine(); assert m.configure(0, drive=1) == 0
    y = m.process([.125] * 256)
    assert y[128:] == [.25] * 128
    m = machine(); assert m.configure(2) == 0
    y = m.process([0.0, float('nan'), .5, -.5])
    assert y[1:] == [0, 0, 0]
    quality = []
    stress = list(floats(packed([.8 * math.sin(2 * math.pi * 7000 * n / 48000) for n in range(9600)])))
    for mode in (1, 2, 3):
        dc = machine(); assert dc.configure(mode, drive=1, tone=1) == 0
        tail = dc.process([.25] * 16384)
        dc_peak = max(map(abs, tail[-256:]))
        assert dc_peak < 1e-5, (mode, dc_peak)
        high = machine(); assert high.configure(mode, drive=1, tone=1) == 0
        y = high.process(stress)[4800:]
        direct = direct_reference(stress, mode)[4800:]
        # Fifth harmonic at 35 kHz folds to 13 kHz at a 48-kHz sample rate.
        # This one measurement alone is not a complete antialiasing test.
        ratio = amplitude(y, 13000) / amplitude(y, 7000)
        direct_ratio = amplitude(direct, 13000) / amplitude(direct, 7000)
        improvement = 20 * math.log10(direct_ratio / max(ratio, 1e-15))
        assert improvement > 6, (mode, improvement)
        quiet = list(floats(packed([.02 * math.sin(2 * math.pi * 1000 * n / 48000) for n in range(2048)])))
        gain_amplitudes = []
        for drive in (0, 1):
            v = machine(); assert v.configure(mode, drive=drive, tone=1) == 0
            gain_amplitudes.append(amplitude(v.process(quiet)[1024:], 1000))
        assert gain_amplitudes[1] > gain_amplitudes[0] * 1.5, (mode, gain_amplitudes)
        # Level changes output gain, not the nonlinear drive or filter state.
        a = machine(); b = machine()
        assert a.configure(mode, level=1) == b.configure(mode, level=2) == 0
        first, second = a.process(signal), b.process(signal)
        error = max(abs(y - 2 * x) for x, y in zip(first[128:], second[128:]))
        assert error < 1e-6, error
        tone_input = [.1 * (math.sin(2 * math.pi * 300 * n / 48000) +
                            math.sin(2 * math.pi * 5000 * n / 48000)) for n in range(9600)]
        tone_ratios = []
        for tone in (0, 1):
            v = machine(); assert v.configure(mode, drive=0.25, tone=tone) == 0
            audio = v.process(tone_input)[4800:]
            tone_ratios.append(amplitude(audio, 5000) / amplitude(audio, 300))
        assert tone_ratios[1] > tone_ratios[0] * 2, (mode, tone_ratios)
        quality.append(dict(mode=mode, dc_tail_peak=dc_peak,
                            measured_13khz_alias_improvement_db=improvement,
                            alias_measurement_is_not_full_validation=True,
                            drive_low_and_high_1khz_amplitudes=gain_amplitudes,
                            tone_low_and_high_5khz_to_300hz_ratios=tone_ratios,
                            independent_level_max_error=error))
    profiles = []
    for mode in range(4):
        m = machine(); assert m.configure(mode, drive=1) == 0
        count = [0]; minimum_sp = [STACK]
        def hook(u, address, size, user):
            count[0] += 1
            minimum_sp[0] = min(minimum_sp[0], u.reg_read(UC_ARM_REG_SP))
        # Attach before the first process call to include translated code.
        handle = m.u.hook_add(UC_HOOK_CODE, hook)
        m.process(signal[:32])
        m.u.hook_del(handle)
        profiles.append(dict(mode=mode, instructions_per_32=count[0],
                             stack_fixture_bytes=STACK - minimum_sp[0]))
    assert before == {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}
    text_size = sum(s['size'] for s in segments if s['flags'] & 1)
    report = dict(status='PASS_ISOLATED_OVERDRIVE_FEASIBILITY_PROTOTYPE',
                  hardware_accessed=False, firmware_created=False, editor_changed=False,
                  source_sha256={p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                                 for p in (ROOT / 'preamp.c', ROOT / 'preamp.h', ROOT / 'lab.ld', Path(__file__))},
                  elf_sha256=hashlib.sha256((OUT / 'preamp.elf').read_bytes()).hexdigest(),
                  protected_files_sha256=before, text_and_rodata_bytes=text_size,
                  state_bytes=bypass.size, runtime_gap_bytes=228,
                  state_plus_future_pending_config_and_sequence_bytes=bypass.size+ctypes.sizeof(Config)+4,
                  pending_config_protocol_implemented=False,
                  current_itcm_gap_bytes=1904, complete_module_fits_current_code_gap=text_size < 1904,
                  bypass_bit_exact=True, zero_count_keeps_state=True,
                  invalid_config_rejected_without_changes=True, finite_and_fault_tests=True,
                  callee_saved_registers_and_state_guards=True, parameter_cases=rows,
                  preliminary_quality_checks=quality,
                  instruction_profiles=profiles, sample_rate=48000, nonlinear_resample_factor=1, antialiasing="polynomial antiderivative divided difference",
                  quality_matched_to_hardware_pedals=False, realtime_validated=False,
                  reference_sources=[
                      'https://line6.com/podgo-models/?locale=en_US',
                      'https://xotic.us/manuals/pdf/EP_Booster_manual.pdf',
                      'https://www.boss.info/ca/products/sd-1/'],
                  limitations=['Instruction counts are not cycles or hardware timing',
                               'Pedal algorithms are preliminary tonal approximations',
                               'Antialiasing sweep, actual-circuit behavior, gain calibration and audible comparison remain pending',
                               'No protocol or preset storage for the new controls yet',
                               'No live amplifier memory reservation or integration has been made'])
    (OUT / 'result.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({k: report[k] for k in ('status', 'state_bytes', 'text_and_rodata_bytes',
                                          'complete_module_fits_current_code_gap', 'instruction_profiles', 'preliminary_quality_checks',
                                          'hardware_accessed', 'firmware_created')}, indent=2))


if __name__ == '__main__': main()
