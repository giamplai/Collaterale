#!/usr/bin/env bash
# Build release packages into artifacts/packages.
#   scripts/package.sh                 -> every target
#   scripts/package.sh linux-x64 win-x64
# Linux: .tar.gz + .deb · Windows: .zip · macOS: unsigned .app in a .zip. SHA256SUMS is written at the end.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1

VERSION=$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' Directory.Build.props | head -1)
NAME=Collaterale
OUT="$PWD/artifacts/packages"
TARGETS=("$@"); [ ${#TARGETS[@]} -eq 0 ] && TARGETS=(linux-x64 linux-arm64 osx-arm64 osx-x64 win-x64)
rm -rf "$OUT"; mkdir -p "$OUT"

for rid in "${TARGETS[@]}"; do
  echo "== $rid"
  pub="artifacts/publish/$rid"
  rm -rf "$pub"
  dotnet publish src/Collaterale.App -c Release -r "$rid" --self-contained -p:PublishSingleFile=true -o "$pub" --nologo -v q
  find "$pub" -name '*.pdb' -delete
  base="$NAME-$VERSION-$rid"
  case "$rid" in
    linux-*)
      stage=$(mktemp -d)/"$base"; mkdir -p "$stage"
      cp "$pub/$NAME" "$stage/"; cp src/Collaterale.App/Assets/icon.png "$stage/collaterale.png"
      cp packaging/linux/collaterale.desktop packaging/linux/collaterale-dolphin.desktop packaging/linux/collaterale-nautilus-compare "$stage/"
      cat > "$stage/install.sh" <<'EOS'
#!/usr/bin/env sh
# Per-user install: ~/.local/opt + launcher entry. Remove with: rm -r ~/.local/opt/collaterale ~/.local/bin/collaterale ~/.local/share/applications/collaterale.desktop ~/.local/share/kio/servicemenus/collaterale-dolphin.desktop "~/.local/share/nautilus/scripts/Compare with Collaterale"
set -e
here=$(cd "$(dirname "$0")" && pwd)
dest="$HOME/.local/opt/collaterale"
mkdir -p "$dest" "$HOME/.local/bin" "$HOME/.local/share/applications" "$HOME/.local/share/icons/hicolor/256x256/apps"
cp "$here/Collaterale" "$dest/"; chmod +x "$dest/Collaterale"
ln -sf "$dest/Collaterale" "$HOME/.local/bin/collaterale"
cp "$here/collaterale.png" "$HOME/.local/share/icons/hicolor/256x256/apps/"
sed "s|^Exec=.*|Exec=$dest/Collaterale %F|" "$here/collaterale.desktop" > "$HOME/.local/share/applications/collaterale.desktop"
mkdir -p "$HOME/.local/share/kio/servicemenus" "$HOME/.local/share/nautilus/scripts"
cp "$here/collaterale-dolphin.desktop" "$HOME/.local/share/kio/servicemenus/"; chmod +x "$HOME/.local/share/kio/servicemenus/collaterale-dolphin.desktop"
cp "$here/collaterale-nautilus-compare" "$HOME/.local/share/nautilus/scripts/Compare with Collaterale"; chmod +x "$HOME/.local/share/nautilus/scripts/Compare with Collaterale"
echo "Installed. Start it from your application menu or run: collaterale"
EOS
      chmod +x "$stage/install.sh" "$stage/$NAME"
      tar -C "$(dirname "$stage")" -czf "$OUT/$base.tar.gz" "$base"

      arch=amd64; [ "$rid" = linux-arm64 ] && arch=arm64
      deb=$(mktemp -d)
      mkdir -p "$deb/DEBIAN" "$deb/opt/collaterale" "$deb/usr/bin" "$deb/usr/share/applications" "$deb/usr/share/icons/hicolor/256x256/apps" "$deb/usr/share/kio/servicemenus"
      cp "$pub/$NAME" "$deb/opt/collaterale/"; chmod 755 "$deb/opt/collaterale/$NAME"
      ln -s /opt/collaterale/$NAME "$deb/usr/bin/collaterale"
      cp src/Collaterale.App/Assets/icon.png "$deb/usr/share/icons/hicolor/256x256/apps/collaterale.png"
      cp packaging/linux/collaterale.desktop "$deb/usr/share/applications/"
      cp packaging/linux/collaterale-dolphin.desktop "$deb/usr/share/kio/servicemenus/"
      # Debian policy: root dir and all dirs 0755, plain files 0644, the executable 0755
      find "$deb" -type d -exec chmod 755 {} +
      find "$deb" -type f ! -path "*/opt/*" -exec chmod 644 {} +
      size=$(du -sk "$deb/opt" | cut -f1)
      cat > "$deb/DEBIAN/control" <<EOC
Package: collaterale
Version: $VERSION
Section: devel
Priority: optional
Architecture: $arch
Depends: libfontconfig1, libx11-6
Installed-Size: $size
Maintainer: Giampiero Lai <giampiero.lai@gmail.com>
Description: File, folder and merge comparison tool
 Compare text files side by side, synchronize folders and resolve
 three-way merges, on Linux, macOS and Windows.
EOC
      dpkg-deb --build --root-owner-group "$deb" "$OUT/collaterale_${VERSION}_${arch}.deb" >/dev/null
      ;;
    win-*)
      stage=$(mktemp -d)/"$base"; mkdir -p "$stage"
      cp "$pub/$NAME.exe" "$stage/"
      printf 'Collaterale %s
Run Collaterale.exe directly (portable). For the Start menu entry and the Explorer context menu use the Collaterale-%s-win-x64-setup.exe installer.
' "$VERSION" "$VERSION" > "$stage/README.txt"
      (cd "$(dirname "$stage")" && zip -qr "$OUT/$base.zip" "$base")
      ;;
    osx-*)
      stage=$(mktemp -d); app="$stage/Collaterale.app"
      mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
      cp "$pub/$NAME" "$app/Contents/MacOS/"; chmod +x "$app/Contents/MacOS/$NAME"
      cp src/Collaterale.App/Assets/icon.icns "$app/Contents/Resources/"
      sed "s/@VERSION@/$VERSION/g" packaging/macos-Info.plist > "$app/Contents/Info.plist"
      # Apple silicon refuses to run unsigned code: an ad-hoc signature (no certificate) is enough for a local launch.
      # Only possible on macOS; packages built elsewhere are signed by the release workflow's macOS job instead.
      if command -v codesign >/dev/null; then codesign --force --deep -s - "$app"; fi
      (cd "$stage" && zip -qry "$OUT/$base.zip" "Collaterale.app")
      ;;
  esac
done

SUMS=${SUMS_NAME:-SHA256SUMS}
if command -v sha256sum >/dev/null; then (cd "$OUT" && sha256sum $(ls | grep -v '^SHA256SUMS') > "$SUMS"); else (cd "$OUT" && shasum -a 256 $(ls | grep -v '^SHA256SUMS') > "$SUMS"); fi
echo; echo "Packages in $OUT:"; ls -lh "$OUT"
