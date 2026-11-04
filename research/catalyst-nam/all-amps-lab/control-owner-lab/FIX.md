# Native control-owner correction (offline candidate)

The preserved r103 audio binary called the native graph setter0x17f81 from
stock audio dispatch. The native routine rebuilds graph entries and takes the
arena semaphore indefinitely. This violates the audio/control ownership order.
The regression executes the preserved r103 binary and rejects its first such
call at0x17f80; it does not rely on finite output or mocked success from a lock.

## Corrected ownership

- The audio ELF contains no native graph setter invocation. Before a custom
  stock-model pedal runs it requires confirmed OEM Boost bypass.
- The exact original DspSchedulerCtrlTask wait at0x8000b9b0 now services a
  bounded mailbox before waiting at most5ticks. Task identity requires IPSR0,
  stack0x80806370..0x80806b70 and the native queue from0x80806bd4.
- No synthetic native FSM messages are dispatched. The wrapper returns to the
  native caller only after receive returns1. Timeout0 retries internally;
  any unexpected return yields one proven native scheduler tick before retry.
  Foreign task/queue and finite waits retain their original semantics.
- The control task tries the original external DSP mutex with timeout0. It
  resolves the currently active Amp descriptor and native handle AFTER taking
  that mutex, validates the object/arena/stream/slot/node/model, and only then
  invokes the original setter. This uses native outer-mutex -> arena-mutex
  order. Give uses all four required arguments and is checked for return1.
- The public native loader is wrapped with its exact relocated four-byte
  prologue. It invalidates the OEM-state proof and increments a generation
  before loading, including same-model/same-address reuse. The control owner
  rebinds and reads the real Boost bitmap afterwards, including FX-only loads
  that do not emit another Amp BoostSwitch callback.
- The pre-setter wrapper masks only the effective native BoostSwitch payload
  when a custom pedal is requested. The post-observer receives the ORIGINAL
  user payload; inactive model nodes cannot overwrite active Boost intent.
- Completion requires the same graph/control epoch, no active native load,
  and the same complete configuration sequence. Late publications are retried.
- HELLO capability32 is advertised only by this hooked control-owner variant.
  It complements capability16 (all-model custom pedals) and is not an audible
  ready acknowledgement. Old all-model variants lack this safety capability.

## Evidence

`build/native-tests.json` executes all12 real native amp implementations with
factory Boost, EP,808 and SD-1, including mode restoration, ON/OFF, blocked
outer mutex, invalid task/IRQ context, queue timeout/error sequences, exact
address/model reuse, FX-only load, inactive-node controls, a new generation
published during the native setter, diagnostic14 and release failure.

The original native graph load, setters and audio dispatch run as compiled
Thumb code. Synchronization/scheduler and MRS IPSR are explicit functional
mocks. No USB, SDK, device, installable CTF or physical audio was accessed by
this verifier. These are functional offline results, not hardware timing or
cold-boot validation.

Control-call stack is observed at every emulated block. With callerSP80806b58,
the lowest measuredSP is80806a58, leaving1768bytes to the proven task stack
base80806370. This excludes real IRQ/preemption and is not a hardware watermark.

## Fixed read-only diagnostic14 (version1,84bytes)

21 words: status,version,waitCalls,waitPolls,waitMessages,minWaitSP,epoch,
completedEpoch,requestedSequence,appliedSequence,serviceCalls,nativeCompleted,
nativeActive,blockedContext,known,node,arena,graph,model,nativeBoost,
unexpectedReceive. nativeActive2 means a failed mutex release and permanent
fail-closed state until restart;0 is idle,1 is native setter in progress.
minWaitSP measures the wrapper entry floor, not the full native stack depth.
The request must use the receiver product, session0, nonzero transaction,
slot/number/bytes0. It exposes no arbitrary address or writable selector.

## Exact final artifacts

- Audio ELF: `a652621cc772759d5a0912cff1fd4d9c383410d4c3737f13a3e2a3bb98237c91`
- Actor ELF: `329cb0dfb8164200040ccc8092a583e81c9eb4e63ca4e1850f4b988452080654`
- Noninstallable segment: `a4f5c1038787b91a2c0272d1f64cb6d55bfba3bc58d0b67bcba875aa9f7c5ff8`
- Native verifier source: `2d6e33014ffe7dfa70be195d81755d2b02d657374e48bd9f77773e691d4ef50f`

Build entrypoints: `../build_control_owner.py`, `../build_control_actor.py`, `../build_control_boot.py`.
Regression: `../verify_control_native.py`. Native and production owner/FS reports must match these exact hashes before packaging.
