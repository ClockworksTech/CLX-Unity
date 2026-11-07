#!/bin/bash
# Builds the package tarball that Unity's Package Manager installs with
# "Add package from tarball": everything Unity imports, under a package/ folder
# as `npm pack` lays it out. Tools~ and the repo's own files stay out.
#   pack.sh <output .tgz>
set -euo pipefail

out=$(cd "$(dirname "$1")" && pwd)/$(basename "$1")
root=$(cd "$(dirname "$0")/.." && pwd)
stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT

mkdir "$stage/package"
cd "$root"
for item in package.json README.md CHANGELOG.md LICENSE.md Runtime Editor Tests; do
    cp -R "$item" "$item.meta" "$stage/package/"
done
cp -R Samples~ "$stage/package/"
find "$stage/package" -name .DS_Store -delete

# Sorted entries owned by root, as npm pack writes them.
( cd "$stage" && find package -type f | LC_ALL=C sort > list )
tar -C "$stage" -czf "$out" -T "$stage/list" \
    $(tar --version 2>/dev/null | grep -q GNU && echo --owner=0 --group=0 --numeric-owner || echo --uid 0 --gid 0)
echo "Packed $(wc -l < "$stage/list" | tr -d ' ') files into $out"
