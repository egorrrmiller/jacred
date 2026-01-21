#!/bin/sh
set -euo pipefail

CONFIG_FILE="${CONFIG_FILE:-/app/config.yml}"
CONFIG_TEMPLATE="/app/config.template.yml"

if [ ! -f "$CONFIG_FILE" ]; then
    echo "Config not found, seeding default to $CONFIG_FILE"
    cp "$CONFIG_TEMPLATE" "$CONFIG_FILE"
    chmod 640 "$CONFIG_FILE"
else
    echo "Using existing config at $CONFIG_FILE"
fi

# Приложение читает config.local.yml в /app; копируем актуальный config.yml рядом с бинарём
APP_CONFIG_LOCAL="/app/config.local.yml"
cp "$CONFIG_FILE" "$APP_CONFIG_LOCAL"
chmod 640 "$APP_CONFIG_LOCAL"

UMASK_VALUE="${UMASK:-0027}"
umask "$UMASK_VALUE" 2>/dev/null || umask 0027

echo "JacRed starting at $(date)"
echo "Effective config: $APP_CONFIG_LOCAL"
echo "Connection string: ${ConnectionStrings__DefaultConnection:-<default from appsettings.json>}"
echo "User: $(id -u):$(id -g)"

exec "$@"
