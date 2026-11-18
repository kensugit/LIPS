#!/bin/bash
# Reuse catalogctl's deployment/backup/rollback implementation under the same lock.
set -euo pipefail
[[ $EUID == 0 && $# == 1 && $1 =~ ^[0-9a-f]{64}$ ]] || exit 2
requested=$1
source /usr/local/sbin/catalogctl
umask 077
exec 9> "$STATE/deployment.lock"
flock -n 9 || { echo 'Another catalog operation is running' >&2; exit 1; }
python3 - "$requested" <<'PY'
import json,pathlib,sys
state=pathlib.Path('/var/lib/catalog-deploy')
current=(state/'current').read_text().strip()
for file in (state/'codex-prepared').glob('*.json'):
    proof=json.loads(file.read_text())
    if proof['packageSha256']==sys.argv[1] and current not in (proof['expectedPackage'],sys.argv[1]):
        raise SystemExit('A different session deployed after preparation; rebase and prepare again.')
PY
if [[ $(cat "$STATE/current") == "$requested" ]]; then
 healthy "$(current)" || exit 1
 echo "Already current and healthy: $requested"
 exit 0
fi
logger -t catalog-deploy "start verified-deploy sha=$requested caller=${SUDO_USER:-root}"
trap 'result=$?; logger -t catalog-deploy "finish verified-deploy exit=$result"; exit "$result"' EXIT
deploy "$requested"
