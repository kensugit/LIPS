#!/bin/bash
set -euo pipefail
[[ $# == 0 && $EUID == 0 ]] || exit 2
previous=$(cat /var/lib/catalog-deploy/previous)
[[ $previous =~ ^[0-9a-f]{64}$ ]] || exit 2
exec /usr/local/sbin/catalogctl deploy "$previous"
