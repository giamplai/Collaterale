#!/usr/bin/env bash
# Builds the Inno Setup installer and smoke-tests it under Wine, in Docker (works on Linux; no Windows needed).
#   scripts/test-windows-installer.sh
# Checks: setup compiles, installs silently, Collaterale.exe runs (--version, --diff), the four Explorer registry entries exist, uninstall removes everything.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"
VERSION=$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' Directory.Build.props | head -1)
dotnet publish src/Collaterale.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts/publish/win-x64 --nologo -v q
mkdir -p artifacts/packages
docker run --rm -v "$PWD:/work" amake/innosetup "/DAppVersion=$VERSION" packaging/windows/collaterale.iss | tail -3

cat > artifacts/wine-test.sh <<EOS
set -u
export WINEDEBUG=-all
P=\$WINEPREFIX; fail=0
run() { xvfb-run -a wine "\$@" 2>&1 | grep -v "X connection"; }
check() { if eval "\$2"; then echo "ok   \$1"; else echo "FAIL \$1"; fail=1; fi; }
run /work/artifacts/packages/Collaterale-$VERSION-win-x64-setup.exe /VERYSILENT /SUPPRESSMSGBOXES '/DIR=C:\\collaterale-inst' >/dev/null
check "setup installed Collaterale.exe" "[ -f \$P/drive_c/collaterale-inst/Collaterale.exe ]"
for b in '*' Directory; do for k in CollateraleSelect CollateraleCompare; do
  check "registry \$b/\$k" "run reg query 'HKCU\\\\Software\\\\Classes\\\\'\$b'\\\\shell\\\\'\$k'\\\\command' | grep -q Collaterale.exe"
done; done
cd /tmp; printf 'a\nb\n' > a.txt; printf 'a\nB\n' > b.txt
check "installed exe --version" "run 'C:\\\\collaterale-inst\\\\Collaterale.exe' --version | grep -q 'Collaterale $VERSION'"
check "installed exe --diff" "run 'C:\\\\collaterale-inst\\\\Collaterale.exe' --diff a.txt b.txt | grep -q '^+B'"
run 'C:\\collaterale-inst\\unins000.exe' /VERYSILENT >/dev/null; sleep 8
check "uninstall removed the folder" "[ ! -d \$P/drive_c/collaterale-inst ]"
check "uninstall removed the registry entries" "! run reg query 'HKCU\\\\Software\\\\Classes\\\\*\\\\shell\\\\CollateraleSelect' | grep -q Collaterale"
exit \$fail
EOS
docker run --rm -v "$PWD:/work" tobix/pywine bash /work/artifacts/wine-test.sh
