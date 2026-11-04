"""Extract factory amp graph identities using the stock native graph loader.
Offline only. The exact three-IR parent is hash pinned; no SDK/device access.
"""
import contextlib,hashlib,json
from pathlib import Path
from verify_trial import ready,ROOT,image_dict,decode_hex,PARENT_SHA
from verify_tcm_ir import bootstrap
from verify_fallback import amp_graph
import target

def main():
    import os
    journal=json.loads((Path(os.environ['LOCALAPPDATA'])/'CatalystIR/v2/installation.json').read_text())
    container=Path(journal['candidate']).read_bytes();assert hashlib.sha256(container).hexdigest()==PARENT_SHA
    image=decode_hex(image_dict(container)[0])[0][1][1]
    db=json.loads((ROOT.parent/'catalyst-ir/analysis/asset-database.json').read_text())
    models=[a for a in db['models'] if 'Amp' in a['name'] and a['name']!='A45_AmpHarness']
    result=[]
    with (ROOT/'evidence/factory-amp-signatures.log').open('w') as log,contextlib.redirect_stdout(log):
        m=ready(image,bootstrap(image))
        for model in models:
            m.block_events.clear();symbol=int(model['fields'][2],16)
            assert m.call(0x8004a914,m.control,0x20024208,symbol,count=100000000)==0
            g=amp_graph(m,m.get(m.parent+16));definition=m.get(g+20)
            signature=[m.get(definition+4*i) for i in range(6)]
            result.append(dict(model_index=model['index'],name=model['name'],symbol=symbol,signature=signature))
    assert len(result)==12 and len({tuple(r['signature']) for r in result})==12
    assert next(r['signature'] for r in result if r['model_index']==target.MODEL_INDEX)==list(target.SIGNATURE)
    report=dict(status='NATIVE_FACTORY_AMP_SIGNATURES_EXTRACTED_OFFLINE',parent_sha256=PARENT_SHA,models=result,hardware_accessed=False)
    (ROOT/'evidence/factory-amp-signatures.json').write_text(json.dumps(report,indent=2))
    print('Extracted twelve distinct native factory amp signatures; no hardware access.')

if __name__=='__main__':main()
