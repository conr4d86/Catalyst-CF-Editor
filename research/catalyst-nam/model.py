"""Strict offline A2-Lite importer and ARM layout; no USB or updater code."""
from pathlib import Path
import json, math, struct

KERNELS = [6]*14 + [15,15] + [6]*7
DILATIONS = [1,3,7,17,41,101,239]*2 + [1,13] + [1,3,7,17,41,101,239]
WEIGHTS = 1871

def fail(message):
    raise ValueError(message)

def unique(pairs):
    result = {}
    for k,v in pairs:
        if k in result: fail('Duplicate JSON key: '+k)
        result[k] = v
    return result

def load(path):
    raw = Path(path).read_bytes()
    if len(raw) > 16*1024*1024: fail('Capture exceeds 16 MiB limit')
    d = json.loads(raw, object_pairs_hook=unique,
                   parse_constant=lambda s: fail('Non-finite JSON number: '+s))
    models = [s['model'] for s in d['config']['submodels']] if d.get('architecture') == 'SlimmableContainer' else [d]
    candidates = [m for m in models if m.get('architecture') == 'WaveNet' and
                  m.get('config',{}).get('layers',[{}])[0].get('channels') == 3]
    if len(candidates) != 1: fail('Exactly one 3-channel WaveNet required')
    m = candidates[0]; c = m['config']; layers = c['layers']
    if len(layers) != 1: fail('One layer array required')
    l = layers[0]
    for key,value in {'input_size':1,'condition_size':1,'channels':3,'bottleneck':3,
                      'groups_input':1,'groups_input_mixin':1}.items():
        if type(l.get(key)) is not int or l[key] != value: fail('Unsupported '+key)
    if c.get('in_channels',1) != 1: fail('Mono input required')
    if c.get('head') is not None or c.get('condition_dsp') is not None:
        fail('Post-stack head/conditioning DSP unsupported')
    if not isinstance(c.get('head_scale'),(int,float)) or not math.isfinite(c['head_scale']):
        fail('Finite head_scale required')
    if l.get('kernel_sizes') != KERNELS or l.get('dilations') != DILATIONS:
        fail('Unsupported convolution geometry')
    if l.get('head') != {'out_channels':1,'kernel_size':16,'bias':True}:
        # Accept explicit default dilation only.
        if l.get('head') != {'out_channels':1,'kernel_size':16,'bias':True,'head_dilation':1}:
            fail('Head must be mono, 16 taps, bias, dilation 1')
    if l.get('layer1x1') != {'active':True,'groups':1}: fail('Residual transform unsupported')
    if l.get('slimmable') is not None: fail('Slimmable layer weights unsupported')
    if l.get('gated',False): fail('Gating unsupported')
    if l.get('gating_mode') not in (None,['none']*23): fail('Gating unsupported')
    if l.get('secondary_activation') not in (None,[None]*23): fail('Secondary activation unsupported')
    if l.get('activation') != [{'type':'LeakyReLU','negative_slope':0.01}]*23:
        fail('Only LeakyReLU 0.01 supported')
    for key in ['head1x1','conv_pre_film','conv_post_film','input_mixin_pre_film',
                'input_mixin_post_film','activation_pre_film','activation_post_film',
                'layer1x1_post_film','head1x1_post_film']:
        value = l.get(key)
        if value not in (None,False) and (not isinstance(value,dict) or value.get('active',False)):
            fail('Unsupported '+key)
    rates = [v for v in [d.get('sample_rate'),m.get('sample_rate')] if v is not None]
    if not rates or any(type(v) not in (int,float) or v != 48000 for v in rates):
        fail('Explicit 48000 Hz training rate required')
    w = m.get('weights',[])
    if len(w) != WEIGHTS or any(type(v) not in (float,int) or not math.isfinite(v) for v in w):
        fail('1871 finite numeric weights required')
    try: w = list(struct.unpack('<1871f',struct.pack('<1871f',*w)))
    except (OverflowError,struct.error): fail('Weights outside float32 range')
    if not all(math.isfinite(v) for v in w): fail('Weights overflow float32')
    return m, w

def pack(w):
    out = w[:3]; offset = 3
    for k in KERNELS:
        conv = w[offset:offset+9*k]; offset += 9*k
        bias = w[offset:offset+3]; offset += 3
        mix = w[offset:offset+3]; offset += 3
        res = w[offset:offset+9]; offset += 9
        rb = w[offset:offset+3]; offset += 3
        out += bias+mix+[conv[(i*3+j)*k+t] for t in range(k) for j in range(3) for i in range(3)]
        out += rb+[res[i*3+j] for j in range(3) for i in range(3)]
    head = w[offset:offset+48]; offset += 48
    out += [w[offset]]+[head[j*16+t] for t in range(16) for j in range(3)]+[w[offset+1]]
    assert len(out) == WEIGHTS
    return struct.pack('<1871f',*out)

def fnv1a(data):
    value=2166136261
    for byte in data:value=((value^byte)*16777619)&0xffffffff
    return value

def geometry():
    ring_offsets=[]; lengths=[]; weight_offsets=[]; r=0; w=3
    for k,d in zip(KERNELS,DILATIONS):
        ring_offsets.append(r); lengths.append((k-1)*d+1); r+=lengths[-1]*3
        weight_offsets.append(w); w+=18+k*9
    return dict(kernels=KERNELS,dilations=DILATIONS,ring_offset=ring_offsets,
                ring_length=lengths,weight_offset=weight_offsets,
                head_ring_offset=r,history_floats=r+48,head_weight_offset=w,
                state_bytes=24*4+(r+48)*4,weights_bytes=WEIGHTS*4,
                receptive_field_samples=16+sum((k-1)*d for k,d in zip(KERNELS,DILATIONS)))
