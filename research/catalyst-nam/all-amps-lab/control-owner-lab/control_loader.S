.syntax unified
.thumb
.section .text.cf_stock_load_original,"ax",%progbits
.global cf_stock_load_original
.type cf_stock_load_original,%function
.thumb_func
cf_stock_load_original:
    push.w {r4-r11,lr} /* Exact stolen four-byte OEM prologue. */
    ldr r12,=0x80048d7d
    bx r12
    .ltorg
.size cf_stock_load_original,.-cf_stock_load_original
