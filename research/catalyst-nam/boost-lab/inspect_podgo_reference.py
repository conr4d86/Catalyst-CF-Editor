"""Read-only POD Go native pedal descriptors, never device code execution.

Invoke with Python -B. Import only the existing offline metadata readers;
do not invoke their mains or change any POD Go project files.
"""
import hashlib
import json
from pathlib import Path
import struct
import sys
import zlib

ROOT = Path(__file__).resolve().parent
TV = ROOT.parents[2]
PORT = TV / 'PODGoCustomFirmware/GitHub/firmware/port'
sys.path.insert(0, str(PORT))
from map_l6ia import wordswap, parse_records
from inspect_model_catalog import parse_catalog
from podgo_module_resources import ModuleResources


def main():
    source = TV / 'PODGoCustomFirmware/GitHub/build/POD_GO_NAM_Conr4d_v8.hxf'
    expected = 'd3fc10a52d71920e60627c39cf67c88d9b9116137f42642321914677e7002eae'
    file = source.read_bytes()
    assert hashlib.sha256(file).hexdigest() == expected
    assert file[:4] == b'FORM' and file[8:12] == b'L6FF'
    assert struct.unpack_from('>I', file, 4)[0] == len(file)-8
    chunks = {}
    pos = 12
    while pos < len(file):
        name, size = struct.unpack_from('>4sI', file, pos)
        assert size <= len(file)-pos-8
        chunks.setdefault(name, []).append(file[pos+8:pos+8+size])
        pos += 8+size+(size & 1)
    assert pos == len(file) and len(chunks[b'data']) == len(chunks[b'HEAD']) == 1
    head = chunks[b'HEAD'][0]
    wanted = struct.unpack_from('>I', head, 16)[0]
    assert 0 < wanted <= 128*1024*1024
    inflater = zlib.decompressobj()
    raw = inflater.decompress(chunks[b'data'][0], wanted+1)
    assert len(raw) == wanted and inflater.eof and not inflater.unused_data
    assert hashlib.md5(raw).digest() == head[20:36]
    _, records, _ = parse_records(raw)
    view = wordswap(raw)
    catalog = parse_catalog(view, records[3], allow_nonadjacent_class_table=True)
    resources = ModuleResources(view, records[4], catalog)
    names = ('HD2_DistKinkyBoostMono', 'HD2_DistScream808Mono', 'HD2_DistStuporODMono')
    # Firmware classes omit Mono for these wrappers; resolve exact observed
    # names with a single explicit fallback, not an index from another table.
    rows = []
    for requested in names:
        name = requested if requested in resources.by_name else requested.removesuffix('Mono')
        cls = resources.by_name[name]
        m = resources.model(name)
        _, objects, _ = resources.algorithm(m['native_id'])
        row = {k:m[k] for k in ('class_name', 'class_id', 'module_index', 'native_id',
                               'native_input_channels', 'native_output_channels')}
        row.update(module_bytes=len(resources.data(m['module_index'])), kind=cls['kind_word'],
                   native_object_count=len(objects),
                   states=[{k:s[k] for k in ('name','state_index','type_word')} for s in cls['states']],
                   controls=[{k:c[k] for k in ('name','parameter_id','type_word','range_and_default_words')}
                             for c in cls['controls']])
        rows.append(row)
    report = dict(source=str(source), source_sha256=expected, hardware_accessed=False,
                  SDK_loaded=False, firmware_written=False, DSP_executed=False,
                  metadata_not_audio_response=True, models=rows)
    out = ROOT/'checks/tonal-r94/podgo-native-catalog.json'
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, indent=2)+'\n')
    for r in rows:
        print(r['class_name'], 'native', hex(r['native_id']),
              'objects', r['native_object_count'], 'module bytes', r['module_bytes'])
    print('Saved', out)


if __name__ == '__main__':
    main()
