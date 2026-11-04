/* SPDX-License-Identifier: MIT. Emulator-only hook for two native receive calls.
 * Native driver remains in r7. Every original byte and the original CMP flags
 * are preserved, including -1/no-data. No universal identity handler is hooked.
 */
.syntax unified
.thumb
.global cf_receive_wrapper
.type cf_receive_wrapper,%function
.thumb_func
cf_receive_wrapper:
    push {r1,r2,r3,lr}
    blx r7
    push {r0,r1}
    bl cf_rx_byte
    pop {r0,r1}
    cmp r0,#0xf7
    pop {r1,r2,r3,pc}
