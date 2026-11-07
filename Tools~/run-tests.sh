#!/bin/bash
# Runs everything that can be checked without Unity. Needs the dotnet SDK. Mono is
# optional and is the closest runtime to Unity's that runs outside Unity.
#   run-tests.sh [folder of .rwf, .bg and .cues files from a real Viewer]
set -euo pipefail
cd "$(dirname "$0")"

args=()
[ $# -gt 0 ] && args=(--cache "$1")

echo "== Package layout"
python3 check-package.py

echo "== Protocol layer: .NET Standard 2.1, C# 9"
dotnet build Compile/Protocol.netstandard.csproj -nologo -v q | grep -E "rror\(s\)|arning\(s\)"

echo "== Unity layer, Editor and sample against stubs"
dotnet build UnityStubs/UnityLayer.csproj -nologo -v q | grep -E "rror\(s\)|arning\(s\)"

echo "== Tests on .NET"
dotnet build TestRunner/CLX.TestRunner.csproj -nologo -v q -c Release | grep -E "rror\(s\)"
dotnet TestRunner/.build/CLX.TestRunner/bin/Release/net10.0/CLX.TestRunner.dll ${args[@]+"${args[@]}"}

if command -v mono >/dev/null && command -v csc >/dev/null; then
    echo "== Tests on Mono"
    out=TestRunner/.build/mono
    lib="$(dirname "$(dirname "$(command -v mono)")")/lib/mono/4.5"
    mkdir -p "$out"
    csc -nologo -langversion:9 -debug -nowarn:1701,1702 -target:exe -out:"$out/CLXTests.exe" \
        -r:"$lib/System.dll" -r:"$lib/System.Core.dll" \
        -r:"$lib/Facades/System.Runtime.InteropServices.RuntimeInformation.dll" \
        TestRunner/*.cs $(find ../Runtime/Protocol ../Tests/Editor -name '*.cs')
    mono --debug "$out/CLXTests.exe" ${args[@]+"${args[@]}"}
else
    echo "== Mono not found, skipping"
fi
