#!/usr/bin/env python3
"""Writes the .meta file for every asset in the package.

Unity ignores a file that has no .meta when a package is installed from a git
URL, because the package folder is read-only and Unity can't create one. GUIDs
come from each file's path, so they stay the same across runs and scenes can
refer to them. Renaming a file changes its GUID.
"""
import hashlib
from pathlib import Path

root = Path(__file__).resolve().parent.parent
ROOT_FILES = ["package.json", "README.md", "CHANGELOG.md", "LICENSE.md"]
FOLDERS = ["Runtime", "Editor", "Tests", "Samples~/DeckMonitor"]

TAIL = "  userData: \n  assetBundleName: \n  assetBundleVariant: \n"


def guid(relative):
    return hashlib.md5(f"live.clockworks.clx:{relative}".encode()).hexdigest()


def body(path):
    if path.is_dir():
        return "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n" + TAIL
    name = path.name
    if name == "package.json":
        return "PackageManifestImporter:\n  externalObjects: {}\n" + TAIL
    if path.suffix == ".cs":
        return (
            "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n"
            "  executionOrder: 0\n  icon: {instanceID: 0}\n" + TAIL
        )
    if path.suffix == ".asmdef":
        return "AssemblyDefinitionImporter:\n  externalObjects: {}\n" + TAIL
    if path.suffix == ".md":
        return "TextScriptImporter:\n  externalObjects: {}\n" + TAIL
    return "DefaultImporter:\n  externalObjects: {}\n" + TAIL


def write(path):
    relative = path.relative_to(root).as_posix()
    meta = path.with_name(path.name + ".meta")
    meta.write_text(f"fileFormatVersion: 2\nguid: {guid(relative)}\n{body(path)}")


def main():
    assets = [root / name for name in ROOT_FILES]
    for folder in FOLDERS:
        top = root / folder
        # A folder inside a "~" folder comes in with the sample, so it needs one
        # only below the sample's top level, which is the import root.
        if "~" not in folder:
            assets.append(top)
        assets += [p for p in sorted(top.rglob("*")) if p.suffix != ".meta"]
    for asset in assets:
        write(asset)
    print(f"Wrote {len(assets)} .meta files")


if __name__ == "__main__":
    main()
