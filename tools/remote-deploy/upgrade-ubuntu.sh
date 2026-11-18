#!/bin/bash
# One-time administrator installation of the fixed Web-only preparation command.
set -euo pipefail
PATH=/usr/sbin:/usr/bin:/sbin:/bin
export PATH
[[ $EUID == 0 && $# == 1 && $1 == --apply ]] || exit 2
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
sha256sum --check --quiet SHA256SUMS
[[ -x /usr/local/sbin/catalogctl && -f /var/lib/catalog-deploy/current ]]
[[ -f /etc/lips-codex/sshd_config && -f /etc/sudoers.d/lips-codex ]]
[[ ! -L /etc/sudoers.d/lips-codex && ! -L /usr/local/lib/lips-codex ]]
[[ $(id -u catalogdeploy) != 0 ]]
! id -nG catalogdeploy | tr ' ' '\n' | grep -Eq '^(docker|sudo|adm)$'
cmp operator.pub /etc/lips-codex/operator.pub
sshd -t -f /etc/lips-codex/sshd_config
python3 -m py_compile dispatch.py prepare.py
backup=/var/lib/catalog-deploy/codex-setup-$(date -u +%Y%m%dT%H%M%S)
install -d -m 0700 "$backup"
cp /etc/sudoers.d/lips-codex "$backup/sudoers"
cp /usr/local/lib/lips-codex/dispatch.py "$backup/dispatch.py"
if [[ -f /usr/local/sbin/lips-codex-prepare ]]; then cp /usr/local/sbin/lips-codex-prepare "$backup/prepare.py"; fi
if [[ -f /usr/local/sbin/lips-codex-deploy ]]; then cp /usr/local/sbin/lips-codex-deploy "$backup/deploy.sh"; fi
cp /etc/sudoers.d/lips-codex "$backup/sudoers.new"
if ! grep -q '/usr/local/sbin/lips-codex-prepare' "$backup/sudoers.new"; then
 printf '\ncatalogdeploy ALL=(root) NOPASSWD: /usr/local/sbin/lips-codex-prepare *\n' >> "$backup/sudoers.new"
fi
if ! grep -q '/usr/local/sbin/lips-codex-deploy' "$backup/sudoers.new"; then
 printf '\ncatalogdeploy ALL=(root) NOPASSWD: /usr/local/sbin/lips-codex-deploy *\n' >> "$backup/sudoers.new"
fi
visudo -cf "$backup/sudoers.new"
install -o root -g root -m 0755 prepare.py /usr/local/sbin/lips-codex-prepare
install -o root -g root -m 0755 deploy-verified.sh /usr/local/sbin/lips-codex-deploy
install -o root -g root -m 0755 dispatch.py /usr/local/lib/lips-codex/dispatch.py
install -o root -g root -m 0440 "$backup/sudoers.new" /etc/sudoers.d/lips-codex
systemctl enable --now lips-codex-sshd
systemctl is-active lips-codex-sshd
echo "Installed Web-only prepare command. Backup: $backup"
/usr/local/sbin/catalogctl status
echo 'Existing images:'
docker inspect --format '{{.Name}} {{.Image}} {{.State.Health.Status}}' catalog-web-1 catalog-postgres-1
echo 'SSH host fingerprint (unchanged):'
ssh-keygen -lf /etc/lips-codex/ssh_host_ed25519_key.pub
