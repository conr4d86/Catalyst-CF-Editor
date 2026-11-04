"""Isolated audio variant: native graph setters are forbidden in audio.

Control prepare/observe/service lives in the actor's Main-thread component.
The previous r103 build and generator are retained for failure regression.
"""
import hashlib,importlib.util,json
from pathlib import Path
BASE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('all_amp_audio_base',BASE/'build_all_amps.py')
base=importlib.util.module_from_spec(spec);spec.loader.exec_module(base)
original_derive=base.derive
def derive(rows):
 text=original_derive(rows)
 text=text.replace('static int factory_amp(const unsigned *g)', '__attribute__((noinline)) static int factory_amp(const unsigned *g)')
 first=text.index('static void stock_switch(');last=text.index('static void observe_stack(',first)
 text=text[:first]+text[last:]
 old='    sync_preamp();stock_switch(model);'
 new='''    sync_preamp();
    /* A native control owner must confirm bypass before a custom pedal runs.
     * Never call a graph setter, take a mutex or rebuild a graph from audio.
     * Until confirmation, retain the OEM path instead of stacking boosts. */
    if(R->pedal.target.mode&&stock.boost_native!=0u)return 0;'''
 assert text.count(old)==1;text=text.replace(old,new)
 # Unknown after reset is deliberately different from confirmed OEM OFF.
 old='static StockPreamp stock __attribute__((section(".stock_data")))={.level=1};'
 new='static StockPreamp stock __attribute__((section(".stock_data")))={.boost_native=2,.level=1};'
 assert text.count(old)==1;text=text.replace(old,new)
 assert '0x17f81u' not in text and 'stock_switch(' not in text
 return text
if __name__=='__main__':
 base.ROOT=BASE/'control-owner-lab';base.derive=derive;base.main()
 path=base.ROOT/'build/build.json';report=json.loads(path.read_text())
 report['native_setter_from_audio_removed']=True
 report['control_owner_required']=True
 report['sources'][str(Path(__file__).relative_to(base.TV))]=hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
 path.write_text(json.dumps(report,indent=2))
