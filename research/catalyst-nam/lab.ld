/* Emulator laboratory addresses ONLY. Not a Catalyst memory reservation. */
ENTRY(nam_process)
SECTIONS {
  . = 0x15000;
  .text : { *(.text*) *(.rodata*) }
  /DISCARD/ : { *(.ARM.exidx*) *(.ARM.extab*) *(.comment*) }
}
