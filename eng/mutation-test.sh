#!/usr/bin/env bash
# Mutation testing with Stryker.NET (https://stryker-mutator.io/docs/stryker-net/introduction/).
#
#   eng/mutation-test.sh "<glob of files to mutate>" [output-dir]
#   e.g. eng/mutation-test.sh "**/Sorting/**/*.cs"
#
# Runs on a throwaway copy of the repo because Stryker's per-test coverage needs two changes there:
#  - the classic VSTest runner (its Microsoft.Testing.Platform runner times out capturing coverage), and
#  - CsCheck_Threads=1: CsCheck samples on several threads, which Stryker can't attribute to the test, so mutants
#    it only exercises there would be reported as surviving.
set -euo pipefail

mutate="${1:?glob of files to mutate, e.g. \"**/Sorting/**/*.cs\"}"
repo="$(cd "$(dirname "$0")/.." && pwd)"
work="$(mktemp -d)"
output="${2:-$repo/StrykerOutput/$(date +%Y%m%d-%H%M%S)}"

git -C "$repo" ls-files -z | (cd "$repo" && xargs -0 tar -cf - --) | tar -xf - -C "$work"

python3 - "$work" <<'PY'
import re, sys, pathlib
root = pathlib.Path(sys.argv[1])
props = root / "tests/Directory.Build.props"
props.write_text(props.read_text().replace("<OutputType>Exe</OutputType>", "").replace("<EnableNUnitRunner>true</EnableNUnitRunner>", ""))
packages = root / "Directory.Packages.props"
packages.write_text(packages.read_text().replace(
    '<PackageVersion Include="NUnit" Version="5.0.0" />',
    '<PackageVersion Include="NUnit" Version="5.0.0" />\n    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.12.0" />'))
project = root / "tests/Bojalib.Tests/Bojalib.Tests.csproj"
project.write_text(project.read_text()
    .replace('<PackageReference Include="NUnit" />', '<PackageReference Include="NUnit" />\n    <PackageReference Include="Microsoft.NET.Test.Sdk" />')
    .replace('    <PackageReference Include="coverlet.MTP" />\n', ''))
global_json = root / "global.json"
global_json.write_text(re.sub(r',\s*"test":\s*\{[^}]*\}', '', global_json.read_text()))
PY

command -v dotnet-stryker >/dev/null || dotnet tool install -g dotnet-stryker
export PATH="$PATH:$HOME/.dotnet/tools"
export CsCheck_Threads=1
cd "$work/tests/Bojalib.Tests"
dotnet stryker --mutate "$mutate" --concurrency "${STRYKER_CONCURRENCY:-4}" --reporter html --reporter json --reporter progress --output "$output"
echo "Report: $output/reports/mutation-report.html"
