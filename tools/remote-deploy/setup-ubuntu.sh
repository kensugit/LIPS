#!/bin/bash
set -euo pipefail
PATH=/usr/sbin:/usr/bin:/sbin:/bin
export PATH
[[ $EUID == 0 && $# == 1 && $1 == --apply ]] || { echo 'Usage: sudo bash setup-ubuntu.sh --apply'; exit 2; }
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
sha256sum --check --quiet SHA256SUMS
[[ -x /usr/local/sbin/catalogctl && -f /var/lib/catalog-deploy/current ]] || { echo 'Existing initialized catalog controller required'; exit 1; }
[[ ! -e /etc/lips-codex && ! -e /etc/systemd/system/lips-codex-sshd.service && ! -e /etc/sudoers.d/lips-codex ]] || { echo 'Existing remote deployment installation: inspect before changing'; exit 1; }
source /etc/os-release
[[ $ID == ubuntu && $VERSION_ID == 24.04 ]] || exit 1
python3 - <<'PY'
import base64,pathlib,struct
parts=pathlib.Path('operator.pub').read_text().split()
assert len(parts) in (2,3) and parts[0]=='ssh-ed25519'
raw=base64.b64decode(parts[1],validate=True)
assert raw[:4]==struct.pack('>I',11) and raw[4:15]==b'ssh-ed25519' and len(raw)==51
PY
if ss -ltnH 'sport = :22223' | grep -q .; then echo 'SSH backend port 22223 is occupied'; exit 1; fi
if ! command -v sshd >/dev/null; then
  # Prevent package installation from starting the general SSH listener on port 22.
  # Existing service-start policy is respected and never overwritten.
  policy_created=0
  if [[ ! -e /usr/sbin/policy-rc.d && ! -L /usr/sbin/policy-rc.d ]]; then
    (set -o noclobber; printf '#!/bin/sh\nexit 101\n' > /usr/sbin/policy-rc.d)
    chmod 0755 /usr/sbin/policy-rc.d
    policy_created=1
  fi
  cleanup_policy() { if [[ $policy_created == 1 ]]; then rm -- /usr/sbin/policy-rc.d; fi; }
  trap cleanup_policy EXIT
  apt-get update
  DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends openssh-server
  systemctl disable --now ssh.socket ssh.service
  cleanup_policy
  policy_created=0
  trap - EXIT
fi
if ! id catalogdeploy >/dev/null 2>&1; then useradd -m -s /bin/bash catalogdeploy; fi
[[ $(id -u catalogdeploy) != 0 ]] || exit 1
if id -nG catalogdeploy | tr ' ' '\n' | grep -Eq '^(docker|sudo|adm)$'; then echo 'catalogdeploy has unexpected privileged groups'; exit 1; fi
# Use PAM account checks, with public-key-only authentication. No password is enabled.
[[ ! -L /home/catalogdeploy && ! -L /home/catalogdeploy/incoming ]] || { echo 'Unexpected deployment home symlink'; exit 1; }
install -d -o catalogdeploy -g catalogdeploy -m 0700 /home/catalogdeploy/incoming
install -d -o root -g root -m 0755 /etc/lips-codex /usr/local/lib/lips-codex /run/sshd
install -o root -g root -m 0644 operator.pub /etc/lips-codex/operator.pub
install -o root -g root -m 0755 dispatch.py /usr/local/lib/lips-codex/dispatch.py
install -o root -g root -m 0755 rollback.sh /usr/local/sbin/lips-codex-rollback
ssh-keygen -q -t ed25519 -N '' -f /etc/lips-codex/ssh_host_ed25519_key
cat > /etc/lips-codex/sshd_config <<'EOF'
Port 22223
ListenAddress 127.0.0.1
HostKey /etc/lips-codex/ssh_host_ed25519_key
PidFile /run/lips-codex-sshd.pid
AllowUsers catalogdeploy
AuthorizedKeysFile /etc/lips-codex/operator.pub
AuthenticationMethods publickey
PubkeyAuthentication yes
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitEmptyPasswords no
PermitRootLogin no
UsePAM yes
PermitTTY no
PermitUserRC no
PermitUserEnvironment no
DisableForwarding yes
AllowAgentForwarding no
X11Forwarding no
MaxSessions 1
MaxAuthTries 3
LoginGraceTime 30
ForceCommand /usr/bin/python3 -I /usr/local/lib/lips-codex/dispatch.py
LogLevel VERBOSE
EOF
cat > /etc/sudoers.d/lips-codex <<'EOF'
catalogdeploy ALL=(root) NOPASSWD: /usr/local/sbin/catalogctl status, /usr/local/sbin/catalogctl deploy *, /usr/local/sbin/lips-codex-rollback ""
EOF
chmod 0440 /etc/sudoers.d/lips-codex
visudo -cf /etc/sudoers.d/lips-codex
sshd -t -f /etc/lips-codex/sshd_config
cat > /etc/systemd/system/lips-codex-sshd.service <<'EOF'
[Unit]
Description=LIPS restricted Codex deployment SSH
After=network.target docker.service
[Service]
ExecStartPre=/usr/bin/mkdir -p /run/sshd
ExecStart=/usr/sbin/sshd -D -e -f /etc/lips-codex/sshd_config
Restart=on-failure
RestartSec=5
[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable --now lips-codex-sshd
systemctl is-active lips-codex-sshd
echo 'HOST PUBLIC KEY (send this line and fingerprint back to Codex):'
cat /etc/lips-codex/ssh_host_ed25519_key.pub
ssh-keygen -lf /etc/lips-codex/ssh_host_ed25519_key.pub
echo 'No application deployment performed. Windows forwarding and host-key verification are still required.'
