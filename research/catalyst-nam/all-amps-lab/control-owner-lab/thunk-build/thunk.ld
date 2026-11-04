ENTRY(cf_stock_set_thunk)
SECTIONS { . = 0x17f00; .text : { *(.text*) *(.rodata*) } /DISCARD/ : { *(.ARM.exidx*) *(.ARM.extab*) *(.comment*) } }
