#!/bin/sh
# Runs INSIDE a disposable, privileged distro container on a free GitHub-hosted VM (see .github/workflows/linux-distros.yml).
# Installs the tools, runs the python recovery tests (unit + REAL loop devices + Partclone round trip) and the migration CLI
# against this distro's real package manager. Never touches real disks: loop devices are created here by losetup.
set -eu
cd /w
. /etc/os-release
echo "=== $PRETTY_NAME ($ID) kernel $(uname -r) ==="
case "$ID" in
  debian|ubuntu)  apt-get update -qq && DEBIAN_FRONTEND=noninteractive apt-get install -y -qq python3 util-linux lvm2 e2fsprogs partclone curl ca-certificates tar gzip procps ;;
  fedora)         dnf install -y -q python3 util-linux lvm2 e2fsprogs partclone curl ca-certificates tar gzip procps-ng ;;
  arch)           pacman -Sy --noconfirm --needed python util-linux lvm2 e2fsprogs partclone curl ca-certificates tar gzip procps-ng ;;
  opensuse*)      zypper --non-interactive --gpg-auto-import-keys refresh && zypper --non-interactive install -y python3 util-linux lvm2 e2fsprogs partclone curl ca-certificates tar gzip procps ;;
  alpine)         apk add --no-cache python3 util-linux lvm2 e2fsprogs partclone curl ca-certificates tar gzip bash procps icu-libs libstdc++ libgcc krb5-libs ;;
  *) echo "unsupported distro $ID"; exit 2 ;;
esac
python3 --version; lsblk --version | head -1; command -v partclone.ext4 || echo "partclone.ext4 NOT installed (partclone tests will skip)"

echo "=== python unit tests (no devices) ==="
python3 -m unittest discover -s tools/recovery/tests -v

echo "=== real loop-device tests + Partclone round trip ==="
CI=1 python3 tools/recovery/ci_loop_device_test.py

echo "=== migration CLI on this distro's real package manager (.NET 11) ==="
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 HOME=/tmp/home
mkdir -p "$HOME"
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 11.0 --install-dir /opt/dotnet >/dev/null
export PATH="/opt/dotnet:$PATH"
dotnet --version
dotnet run --project migration/GearWin.Migration.Cli -c Release -- inspect | tee /tmp/inspect.json
dotnet run --project migration/GearWin.Migration.Cli -c Release -- inventory > /tmp/inventory.json
python3 - <<'PY'
import json
d = json.load(open('/tmp/inventory.json'))
apps = d.get('applications') or d.get('Applications') or []
print('inventory applications:', len(apps))
assert len(apps) > 0, 'inventory returned no applications on a real distro'
PY
echo "=== OK: $PRETTY_NAME ==="
