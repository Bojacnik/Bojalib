# Regenerates the Roaring serialization fixtures with the reference implementation (CRoaring via pyroaring):
#   python -m venv venv && venv/bin/pip install pyroaring && venv/bin/python generate.py
# The .bin files must match what RoaringBitmap writes (plain cases) and reads (all cases).
import json, sys
from pyroaring import BitMap

def plain(values):
    b = BitMap()
    for v in values:
        b.add(v)
    return b

cases = {}
cases["empty"] = (plain([]), False)
cases["small"] = (plain([1, 2, 3, 1000, 65535, 65536, 70000, 1 << 20, 4000000000, 4294967295]), False)
cases["dense_chunk"] = (plain(list(range(0, 10000)) + [200000]), False)
cases["multiples_of_3"] = (plain([i * 3 for i in range(200000)]), False)

def ranges(spec, optimize=True):
    b = BitMap()
    for start, end in spec:
        for v in range(start, end):
            b.add(v)
    if optimize:
        b.run_optimize()
    return b

cases["runs_few_containers"] = (ranges([(10, 1000), (5000, 5003), (70000, 140000)]), True)
cases["runs_many_containers"] = (ranges([(k * 65536 + 10, k * 65536 + 5000) for k in range(6)]), True)
mixed = ranges([(10, 20), (65536 * 2, 65536 * 2 + 30000)], optimize=False)
for v in range(65536 * 5, 65536 * 6, 2):
    mixed.add(v)
for v in [65536 * 9 + 7, 65536 * 9 + 9000]:
    mixed.add(v)
mixed.run_optimize()
cases["runs_mixed"] = (mixed, True)

for name, (bitmap, has_runs) in cases.items():
    with open(f"{name}.bin", "wb") as f:
        f.write(bitmap.serialize())
