# Differential fixtures: random bitmap pairs with mixed chunk densities, and their |, &, ^, - as computed by the
# reference implementation (CRoaring via pyroaring). RoaringBitmap must reproduce every result byte for byte.
#   python -m venv venv && venv/bin/pip install pyroaring && venv/bin/python generate.py
import random
from pyroaring import BitMap

rng = random.Random(2026)
for case in range(3):
    sets = []
    for _ in range(2):
        b = BitMap()
        for chunk in rng.sample(range(0, 40), 10):
            density = rng.choice([0.0005, 0.01, 0.05, 0.08, 0.3, 0.9])
            for v in rng.sample(range(65536), int(65536 * density)):
                b.add((chunk << 16) + v)
        sets.append(b)
    a, b = sets
    for name, value in [("a", a), ("b", b), ("or", a | b), ("and", a & b), ("xor", a ^ b), ("andnot", a - b)]:
        with open(f"case{case}_{name}.bin", "wb") as f:
            f.write(value.serialize())
