"""Read stock firmware only; write evidence to this isolated review directory.

This is static/abstract evidence, never a hardware acceptance test.
Run from the TV workspace with: python -B <this file>
"""
import hashlib
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
sys.path.insert(0, str(ROOT / 'research/catalyst-ir'))
import arm_tools as fw
from capstone import Cs, CS_ARCH_ARM, CS_MODE_THUMB

decoder = Cs(CS_ARCH_ARM, CS_MODE_THUMB)
checks = []


def check(name, condition):
    checks.append({'name': name, 'passed': bool(condition)})


def instruction(address, mnemonic, operand=None):
    ins = next(decoder.disasm(fw.read(address, 4), address))
    check(hex(address), ins.mnemonic == mnemonic and
          (operand is None or ins.op_str == operand))


for address, value in {
    0x8000b9c0: 0x808062fc,
    0x8000d428: 0x808062fc,
    0x8000d43c: 0x80806bd0,
    0x8000d440: 0x8000c475,
    0x8000d448: 0x8000b979,
    0x8000d44c: 0x8015c694,
    0x8004c8fc: 0x00006981,
    0x800054bc: 0x20026378,
    0x8000d1d4: 0x80807450,
    0x8000d1e8: 0x8015c334,
    0x8000d1ec: 0x8015c33c,
    0x8004c4e8: 0x80172358,
    0x80172370: 0x8004ba69,
}.items():
    check(f'literal_{address:08x}', fw.word(address) == value)
check('task_name', fw.read(0x8015c694, 64).split(b'\0', 1)[0] == b'DspSchedulerCtrlTask:task')
instruction(0x8000b9a8, 'mov.w', 'r2, #-1')
instruction(0x8000b9b0, 'bl', '#0x8004c8f8')
instruction(0x8000b9b8, 'bl', '#0x8002f218')
instruction(0x8000d37c, 'bl', '#0x8004c8c0')
instruction(0x80048b50, 'bl', '#0x8004c7d0')
instruction(0x80048b5c, 'bl', '#0x80046d3c')
instruction(0x80048b68, 'bl', '#0x8004650c')
instruction(0x80049764, 'add.w', 'sb, r5, #0x160')
instruction(0x80049890, 'add.w', 'r6, r5, #8')
instruction(0x800498a0, 'bl', '#0x800490f0')
instruction(0x800490fa, 'bl', '#0x8004c4ec')
instruction(0x80049106, 'str.w', 'r5, [r4, #0xdc]')
instruction(0x8004c4ec, 'mov', 'r1, r0')
instruction(0x0001385c, 'mov.w', 'r1, #-1')
instruction(0x00013862, 'bl', '#0x6c28')
instruction(0x00013874, 'bl', '#0x646c')
instruction(0x8004ba88, 'bl', '#0x80048b44')


class NonRecursiveMutex:
    def __init__(self):
        self.owner = None

    def take(self, task):
        if self.owner is not None:
            return False  # Represents blocking, NOT successful acquisition.
        self.owner = task
        return True

    def give(self, task):
        assert self.owner == task
        self.owner = None


# Native late graph-swap mutex cannot be re-entered by the audio owner.
lock = NonRecursiveMutex()
assert lock.take('audio')
check('abstract_audio_self_wait_detected', not lock.take('audio'))
check('abstract_control_waits_for_audio', not lock.take('dsp_control'))
lock.give('audio')
check('abstract_control_proceeds_after_audio_release', lock.take('dsp_control'))
lock.give('dsp_control')

# A pointer preflight and postflight do not protect the intervening mutation.
generation = 1
cached_generation = generation
assert cached_generation == generation
generation = 2  # A second graph producer rebuilds after preflight.
mutation_used_stale_graph = cached_generation != generation
check('abstract_preflight_postflight_counterexample', mutation_used_stale_graph)

# External serialization must cover graph resolution, preparation AND swap.
outer = NonRecursiveMutex()
assert outer.take('dsp_control')
check('abstract_external_lock_excludes_loader', not outer.take('loader'))
outer.give('dsp_control')
check('abstract_loader_proceeds_after_release', outer.take('loader'))
outer.give('loader')

# Preserve queue receive semantics: only a real message may reach the caller.
return_codes = [0, 0, 0, 1]
dispatches = [rc for rc in return_codes if rc == 1]
check('abstract_timeouts_never_dispatched', dispatches == [1])

binary = ROOT / 'research/catalyst-ir/analysis/image-0-segment-1-60001000.bin'
result = {
    'status': 'PASS_STATIC_AND_ABSTRACT_ONLY' if all(c['passed'] for c in checks) else 'FAIL',
    'binary_sha256': hashlib.sha256(binary.read_bytes()).hexdigest(),
    'checks': checks,
    'dsp_control_task': {
        'entry': '0x8000b979', 'name': 'DspSchedulerCtrlTask:task',
        'constructor': '0x8000cef4', 'create_call': '0x8000d37c',
        'queue_handle_slot': '0x80806bd4', 'handler_slot': '0x80806bd0',
        'task_handle_slot': '0x80806300', 'tcb': '0x80806304',
        'stack_start': '0x80806370', 'stack_end_exclusive': '0x80806b70',
        'stack_depth_words': 512, 'queue_capacity': 128, 'message_bytes': 16,
        'wait_call': '0x8000b9b0', 'dispatch_call': '0x8000b9b8',
    },
    'public_setter': {
        'address': '0x80048b45',
        'abi': 'r0=backend object, r1=native node handle (NOT pointer), r2=parameter, r3=data pointer',
        'order': ['take object lock', 'resolve native handle', 'native setter', 'release object lock'],
        'warning': 'Do not acquire its nonrecursive object lock before calling this API.',
    },
    'backend_layout': {
        'root': '0x20026378', 'context_base': 'U32(root+0x6c)',
        'publicobj': 'context_base+8', 'arena': 'context_base+0x160',
        'identity': 'publicobj=arena-0x158; U32(publicobj+0xdc)=arena',
        'mutex_abi': 'raw take/give receive publicobj DIRECTLY; U32(publicobj)==0 is normal mutex type',
        'amp_subslot_descriptor_slot': '0x80807458',
    },
    'limitations': [
        'Direct-call/literal evidence; not a complete dynamic callgraph.',
        'Static vtable method and constructor resolved; actual runtime instance still needs validation.',
        'AmpSubSlot descriptor handle must be resolved and checked against the currently observed implementation node.',
        'No proof that DSP control task is the only graph producer.',
        'Abstract tests expose failure modes; they do not execute firmware or prove a patch.',
        'No SDK, USB, hardware, flash, application close, publication or firmware edits.',
    ],
}
(HERE / 'dsp-owner-static.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
(HERE / 'dsp-owner-disassembly.txt').write_text('\n\n'.join(
    fw.disasm(address, size) for address, size in [
        (0x8000b978, 70), (0x8000d34a, 58), (0x80048b44, 52), (0x80046d3c, 28),
        (0x80049764, 8), (0x80049890, 24), (0x800490f0, 30),
        (0x8004c4ec, 14), (0x1385c, 40), (0x8004ba68, 38),
    ]), encoding='utf-8')
print(json.dumps({'status': result['status'], 'checks': len(checks), 'failed': [c for c in checks if not c['passed']]}))
assert all(c['passed'] for c in checks)
