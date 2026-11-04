"""Derive the automatic first-5150 adapter from the reviewed trial source.
No device access. Boost controls input gain only; no activation gestures remain.
"""
from build import ROOT

def replace_once(source,old,new):
    assert source.count(old)==1,old[:100]
    return source.replace(old,new)

def main():
    s=(ROOT/'trial.c').read_text()
    s=replace_once(s,'Off at boot; arm with Boost OFF/ON/OFF/ON.','Automatic on Chime 1; fixed -12 dB input trim, independent Boost.')
    s=replace_once(s,'unsigned diagnostic_mute,diagnostic_gate,diagnostic_tick;',
        'unsigned diagnostic_mute,diagnostic_gate,diagnostic_tick;\n    float drive_scale,boost_scale;unsigned auto_entry,boost_enabled;')
    start=s.index('static void apply_control(');end=s.index('/* Observe the native DSP-node setter',start)
    s=s[:start]+'''static void update_input_gain(void) {
    R->input_gain=0.251188643150958f*R->drive_scale*(R->boost_enabled?R->boost_scale:1.0f);
}
static void apply_control(unsigned param,const unsigned *value,int rc) {
    if(rc<0||R->magic!=MAGIC)return;
    union {unsigned u;float f;} v={value[1]};
    float f=value[0]==3?v.f:((value[0]==1||value[0]==2||value[0]==5)?(float)value[1]:-1.0f);
    if(!finite(f)||f<0||f>1)return;
    if(param==TARGET_BOOSTSWITCH){R->boost_enabled=f>=0.5f;update_input_gain();return;}
    /* Amount maps to a transparent pre-NAM gain of 1..2 (0..6.02 dB).
     * This is a level boost, not the original Chime boost algorithm.
     */
    if(param==TARGET_BOOST){R->boost_scale=1.0f+f;update_input_gain();return;}
    if(param==TARGET_DRIVE){
        unsigned index=(unsigned)(f*255.0f+0.5f);if(index>255)index=255;
        R->drive_scale=drive_table[index];update_input_gain();
    }
    if(param==TARGET_CHVOL)R->level=2.0f*f;
    unsigned band=4;
    if(param==TARGET_BASS)band=0;if(param==TARGET_MID)band=1;
    if(param==TARGET_TREBLE)band=2;if(param==TARGET_PRESENCE)band=3;
    if(band<4)R->eq_index[band]=(unsigned)(f*20.0f+0.5f);
}

'''+s[end:]
    s=replace_once(s,'param!=TARGET_TREBLE&&param!=TARGET_PRESENCE&&param!=TARGET_BOOSTSWITCH',
        'param!=TARGET_TREBLE&&param!=TARGET_PRESENCE&&param!=TARGET_BOOSTSWITCH&&param!=TARGET_BOOST')
    s=replace_once(s,'if(R->leave_target) {','if(R->leave_target) {\n        R->auto_entry=0;')
    s=replace_once(s,'R->seen=1;observe_stack();', '''R->seen=1;observe_stack();
    if(!R->auto_entry) {
        R->auto_entry=1;R->fault=0;R->want=1;R->mode=0;R->gate=0;
        R->diagnostic_mute=R->diagnostic_gate=0;R->dwt_on=0;
        R->last_target_cycle=0;R->min_period=0xffffffffu;R->max_period=0;
        R->late_periods=R->late_streak=0;
        R->input_peak=R->output_peak=0;R->clipped_samples=0;
    }''')
    s=replace_once(s,'R->nam_fade=0;R->stock_fade=1;','R->nam_fade=0;R->stock_fade=0;')
    s=replace_once(s,'if(!R->reset_done||R->stock_fade>0)return 0;',
        'if(!R->reset_done){for(unsigned i=0;i<32;++i)output[i]=0;return 1;}')
    s=replace_once(s,'gain={R->input_gain},level={R->level};',
        'gain={R->input_gain},level={R->level},drive={R->drive_scale},boost={R->boost_scale},trim={0.251188643150958f};')
    s=replace_once(s,'0x00020005u,R->magic','0x00020006u,R->magic')
    s=replace_once(s,'R->eq_index[2],R->eq_index[3]};',
        'R->eq_index[2],R->eq_index[3],R->auto_entry,R->boost_enabled,drive.u,boost.u,trim.u};')
    s=replace_once(s,'8+5*49+1','8+5*54+1')
    s=replace_once(s,'packet[6]=5;packet[7]=49;','packet[6]=6;packet[7]=54;')
    s=replace_once(s,'i<49;++i','i<54;++i')
    (ROOT/'autoboot.c').write_text(s)
    print('Automatic adapter generated; no device access')

if __name__=='__main__':main()
