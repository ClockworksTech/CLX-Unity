#!/usr/bin/env python3
"""Checks what Unity would otherwise only complain about on import."""
import json
import re
import sys
from pathlib import Path

root = Path(__file__).resolve().parent.parent
errors = []

manifest = json.loads((root / "package.json").read_text())
if not re.fullmatch(r"[a-z0-9][a-z0-9._-]*", manifest["name"]) or len(manifest["name"]) > 214:
    errors.append(f"Bad package name: {manifest['name']}")
if not re.fullmatch(r"\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?", manifest["version"]):
    errors.append(f"Bad version: {manifest['version']}")
if not re.fullmatch(r"\d{4}\.\d+", manifest["unity"]):
    errors.append(f"Bad unity version: {manifest['unity']}")
for sample in manifest["samples"]:
    if not (root / sample["path"]).is_dir():
        errors.append(f"Missing sample folder: {sample['path']}")

# Every asset needs a .meta, every .meta needs an asset, and no GUID may repeat.
guids = {}
scopes = [root / "Runtime", root / "Editor", root / "Tests", root / "Samples~" / "DeckMonitor"]
assets = [root / n for n in ("package.json", "README.md", "CHANGELOG.md", "LICENSE.md")]
assets += [s for s in scopes[:3]]
for scope in scopes:
    for path in sorted(scope.rglob("*")):
        if path.suffix == ".meta":
            if not path.with_suffix("").exists():
                errors.append(f"Orphan meta: {path.relative_to(root)}")
        else:
            assets.append(path)
for asset in assets:
    meta = asset.with_name(asset.name + ".meta")
    if not meta.exists():
        errors.append(f"No meta: {asset.relative_to(root)}")
        continue
    guid = re.search(r"^guid: ([0-9a-f]{32})$", meta.read_text(), re.M)
    if not guid:
        errors.append(f"No GUID in {meta.relative_to(root)}")
    elif guid[1] in guids:
        errors.append(f"Repeated GUID: {meta.relative_to(root)} and {guids[guid[1]]}")
    else:
        guids[guid[1]] = meta.relative_to(root)

# Assembly definitions must parse, have unique names and refer to real assemblies.
asmdefs = {}
for path in root.rglob("*.asmdef"):
    if ".build" in path.parts:
        continue
    data = json.loads(path.read_text())
    if data["name"] in asmdefs:
        errors.append(f"Repeated assembly name: {data['name']}")
    asmdefs[data["name"]] = data
for name, data in asmdefs.items():
    for reference in data["references"]:
        if reference not in asmdefs and not reference.startswith(("UnityEngine.", "UnityEditor.")):
            errors.append(f"{name} refers to unknown assembly {reference}")
if not asmdefs["Clockworks.CLX.Protocol"]["noEngineReferences"]:
    errors.append("The protocol assembly must stay free of Unity")

# The protocol layer must not use Unity, and nothing may pass C# 9.
for path in (root / "Runtime" / "Protocol").rglob("*.cs"):
    if re.search(r"^\s*using\s+Unity", path.read_text(), re.M):
        errors.append(f"Unity in the protocol layer: {path.relative_to(root)}")

# The scene's objects must link up, and its scripts must exist.
scene = (root / "Samples~/DeckMonitor/DeckMonitor.unity").read_text()
if not scene.startswith("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n"):
    errors.append("Scene header is wrong")
ids = set(re.findall(r"^--- !u!\d+ &(\d+)$", scene, re.M))
for reference in re.findall(r"fileID: (\d+)\}", scene):
    if reference != "0" and reference not in ids:
        errors.append(f"Scene refers to missing object {reference}")
for guid in re.findall(r"guid: ([0-9a-f]{32})", scene):
    if guid not in guids:
        errors.append(f"Scene refers to unknown script {guid}")
    elif not str(guids[guid]).endswith(".cs.meta"):
        errors.append(f"Scene GUID {guid} isn't a script")

for error in errors:
    print(error)
print(f"Package check: {len(assets)} assets, {len(asmdefs)} assemblies, {len(errors)} problems")
sys.exit(1 if errors else 0)
