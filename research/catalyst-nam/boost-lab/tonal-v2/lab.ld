/* Emulator-only addresses; no reservation or patch in the amplifier. */
ENTRY(preamp_process)
SECTIONS {
  . = 0x10000;
  .text : { *(.text*) *(.rodata*) }
  /DISCARD/ : { *(.ARM.exidx*) *(.ARM.extab*) *(.comment*) }
}
