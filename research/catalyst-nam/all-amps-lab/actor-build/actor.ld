ENTRY(cf_fs_dispatch)
SECTIONS { . = 0x80874000; .text : { *(.text*) *(.rodata*) } . = 0x80860000; .bss (NOLOAD) : { *(.bss*) *(COMMON) __cf_actor_bss_end = .; } /DISCARD/ : { *(.ARM.exidx*) *(.ARM.extab*) *(.comment*) } }
