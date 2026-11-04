.equ CF_STOCK_PREPARE_ADDRESS, 2156366285u
.equ CF_STOCK_OBSERVE_ADDRESS, 2156367749u
.syntax unified
.thumb
.section .text.cf_stock_set_thunk,"ax",%progbits
.global cf_stock_set_thunk
.type cf_stock_set_thunk,%function
.thumb_func
cf_stock_set_thunk:
 push {r4,r5,r6,r7,r8,lr}
 sub sp,#8
 mov r5,r0
 mov r6,r1
 mov r7,r2
 mov r8,r3
 add r4,sp,#4
 str r4,[sp]
 ldr r12,=CF_STOCK_PREPARE_ADDRESS
 blx r12
 mov r3,r0
 mov r0,r5
 mov r1,r6
 mov r2,r7
 ldr r12,=0x17f81
 blx r12
 mov r4,r0
 mov r0,r5
 mov r1,r6
 mov r2,r7
 mov r3,r8
 str r4,[sp]
 ldr r12,=CF_STOCK_OBSERVE_ADDRESS
 blx r12
 add sp,#8
 mov r0,r4
 pop {r4,r5,r6,r7,r8,pc}
 .pool
.size cf_stock_set_thunk,.-cf_stock_set_thunk
