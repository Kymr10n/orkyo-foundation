#!/usr/bin/env python3
"""sync-product-pins.py — keep a product's package pins at or above foundation's.

Foundation sets CentralPackageTransitivePinningEnabled, so its pins become dependency
floors: a product pin lower than foundation's restores as NU1109 (package downgrade).
The products mirror foundation's Directory.Packages.props by hand, and foundation's
Dependabot moves ahead of them.

    sync-product-pins.py FOUNDATION_PROPS PRODUCT_PROPS          raise the product's pins
    sync-product-pins.py --check FOUNDATION_PROPS PRODUCT_PROPS  list them, exit 1 if any

Only pins the product already has are compared, and a pin is only ever raised: a
product can be ahead of foundation (it needs a newer test package, say), never behind.
The auto-bump workflow runs the first form; `./dev.sh check` in a product runs the second.
"""
import re
import sys

PIN = re.compile(r'<PackageVersion Include="([^"]+)" Version="([^"$]+)" />')


def version_key(v):
    return tuple(int(p) if p.isdigit() else p for p in re.split(r"[.-]", v))


def main(argv):
    check = "--check" in argv
    paths = [a for a in argv if a != "--check"]
    if len(paths) != 2:
        sys.exit(__doc__)
    foundation_props, product_props = paths

    floors = dict(PIN.findall(open(foundation_props).read()))
    behind = []

    def raise_pin(m):
        name, version = m.groups()
        floor = floors.get(name)
        if floor and version_key(floor) > version_key(version):
            behind.append(f"{name}: {version} -> {floor}")
            return m.group(0).replace(f'Version="{version}"', f'Version="{floor}"', 1)
        return m.group(0)

    source = open(product_props).read()
    raised = PIN.sub(raise_pin, source)
    for line in behind:
        print(("behind " if check else "raised ") + line)

    if check:
        if behind:
            print(f"{product_props} pins packages below foundation's floor (NU1109). "
                  f"Fix: {sys.argv[0]} {foundation_props} {product_props}", file=sys.stderr)
        return 1 if behind else 0
    if raised != source:
        open(product_props, "w").write(raised)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
