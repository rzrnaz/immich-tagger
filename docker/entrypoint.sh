#!/usr/bin/env bash
set -euo pipefail

PUID="${PUID:-99}"
PGID="${PGID:-100}"
UMASK_VALUE="${UMASK:-000}"
APP_USER="immich-tagger"
APP_GROUP="immich-tagger"

umask "$UMASK_VALUE"

if ! getent group "$PGID" >/dev/null 2>&1; then
  groupadd --gid "$PGID" "$APP_GROUP"
else
  APP_GROUP="$(getent group "$PGID" | cut -d: -f1)"
fi

if ! getent passwd "$PUID" >/dev/null 2>&1; then
  useradd --uid "$PUID" --gid "$PGID" --home-dir /app --no-create-home --shell /usr/sbin/nologin "$APP_USER"
else
  APP_USER="$(getent passwd "$PUID" | cut -d: -f1)"
fi

mkdir -p "${IMMICH_TAGGER__CONFIG_ROOT:-/config}" "${IMMICH_TAGGER__LOG_ROOT:-/config/logs}" "${IMMICH_TAGGER__PHOTO_ROOT:-/photos}"
chown -R "$PUID:$PGID" "${IMMICH_TAGGER__CONFIG_ROOT:-/config}" "${IMMICH_TAGGER__LOG_ROOT:-/config/logs}" || true

exec gosu "$PUID:$PGID" "$@"
