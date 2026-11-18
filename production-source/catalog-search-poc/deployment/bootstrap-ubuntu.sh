#!/usr/bin/env bash
set -euo pipefail
if [[ ${1:-} != --apply ]]; then
 echo 'PLAN: Dedicated Ubuntu 24.04 amd64 VM only. Install Docker Engine/Compose + OpenSSH; create catalogdeploy (no docker group); install approved-package controller; generate isolated DB secret. No app deployment.'
 echo 'Apply: sudo bash bootstrap-ubuntu.sh --apply /path/operator-ed25519.pub <source-IP>'
 exit 0
fi
[[ $EUID == 0 && $# == 3 ]] || exit 2
key=$2; sourceIp=$3
[[ "$sourceIp" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]] || exit 2
[[ -f "$key" && $(wc -l < "$key") -le 1 ]] || exit 2
read -r keyType keyValue comment < "$key"
[[ "$keyType" == ssh-ed25519 && "$keyValue" =~ ^[A-Za-z0-9+/=]+$ ]] || exit 2
. /etc/os-release
[[ "$ID" == ubuntu && "$VERSION_ID" == 24.04 && $(dpkg --print-architecture) == amd64 ]] || { echo 'Dedicated Ubuntu 24.04 amd64 required' >&2; exit 2; }
[[ ! -e /etc/catalog && ! -e /opt/catalog && ! -e /var/lib/catalog-deploy ]] || { echo 'Existing setup found; inspect, do not overwrite' >&2; exit 2; }
if id catalogdeploy >/dev/null 2>&1; then echo 'Existing deployment account; inspect manually' >&2; exit 2; fi
for package in docker.io docker-ce podman-docker containerd runc; do
 if dpkg-query -W -f='${Status}' "$package" 2>/dev/null | grep -q 'install ok installed'; then echo "Existing $package: clean dedicated VM required" >&2; exit 2; fi
done
apt-get update
apt-get install -y ca-certificates curl python3 openssh-server sudo
install -m 0755 -d /etc/apt/keyrings
curl --proto '=https' --tlsv1.2 -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
chmod a+r /etc/apt/keyrings/docker.asc
cat > /etc/apt/sources.list.d/docker.sources <<'EOF'
Types: deb
URIs: https://download.docker.com/linux/ubuntu
Suites: noble
Components: stable
Architectures: amd64
Signed-By: /etc/apt/keyrings/docker.asc
EOF
apt-get update
apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
systemctl enable --now docker ssh
install -d -m 0700 /etc/catalog /etc/catalog/secrets /opt/catalog /opt/catalog/releases /var/lib/catalog-deploy /var/lib/catalog-deploy/approved /var/lib/catalog-deploy/backups
install -d -m 0755 /var/lib/catalog /var/lib/catalog/inbox
python3 -c "import secrets,pathlib; pathlib.Path('/etc/catalog/secrets/db_password').write_text(secrets.token_hex(32))"
# Secret directory is root-only; the read-only container bind is readable by app UID.
chmod 0444 /etc/catalog/secrets/db_password
install -o root -g root -m 0755 "$(dirname "$0")/catalogctl" /usr/local/sbin/catalogctl
useradd -m -s /bin/bash catalogdeploy
install -d -o catalogdeploy -g catalogdeploy -m 0700 /home/catalogdeploy/.ssh /home/catalogdeploy/incoming
printf 'from="%s",no-agent-forwarding,no-X11-forwarding,no-pty,permitopen="127.0.0.1:55440" %s %s\n' "$sourceIp" "$keyType" "$keyValue" > /home/catalogdeploy/.ssh/authorized_keys
chown catalogdeploy:catalogdeploy /home/catalogdeploy/.ssh/authorized_keys
chmod 0600 /home/catalogdeploy/.ssh/authorized_keys
printf 'catalogdeploy ALL=(root) NOPASSWD: /usr/local/sbin/catalogctl status, /usr/local/sbin/catalogctl deploy *\n' > /etc/sudoers.d/catalogdeploy
chmod 0440 /etc/sudoers.d/catalogdeploy
visudo -cf /etc/sudoers.d/catalogdeploy
sshd -t
echo 'Bootstrap complete, app NOT deployed. Verify SSH host key out-of-band, then build/upload/review the release. Initialize manually as VM administrator.'
