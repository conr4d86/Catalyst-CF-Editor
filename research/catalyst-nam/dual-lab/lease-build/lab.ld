ENTRY(trial_route)
SECTIONS { . = 0x15000; .text : { *(.text*) *(.rodata*) } . = 0x20064b00; .nam_tables : { *(.nam_tables*) } /DISCARD/ : { *(.ARM.exidx*) *(.ARM.extab*) *(.comment*) } }
