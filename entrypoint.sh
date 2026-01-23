#!/bin/sh
set -euo pipefail

# Config handling
CONFIG_FILE="${CONFIG_FILE:-/app/config.yml}"
CONFIG_TEMPLATE="/app/config.template.yml"

if [ ! -f "$CONFIG_FILE" ]; then
    echo "Config not found, seeding default to $CONFIG_FILE"
    cp "$CONFIG_TEMPLATE" "$CONFIG_FILE"
    chmod 640 "$CONFIG_FILE"
else
    echo "Using existing config at $CONFIG_FILE"
fi

APP_CONFIG_LOCAL="/app/config.local.yml"
cp "$CONFIG_FILE" "$APP_CONFIG_LOCAL"
chmod 640 "$APP_CONFIG_LOCAL"

# Umask
UMASK_VALUE="${UMASK:-0027}"
umask "$UMASK_VALUE" 2>/dev/null || umask 0027

# DB settings (fallback if env не задан)
DB_HOST=${DB_HOST:-db}
DB_PORT=${DB_PORT:-5432}
DB_NAME=${DB_NAME:-jacred}
DB_USER=${DB_USER:-jacred}
DB_PASSWORD=${DB_PASSWORD:-jacred}

CONN_RAW_FALLBACK="Host=${DB_HOST};Port=${DB_PORT};Database=${DB_NAME};Username=${DB_USER};Password=${DB_PASSWORD};Timeout=30;CommandTimeout=60;"
CONN_RAW="${ConnectionStrings__DefaultConnection:-$CONN_RAW_FALLBACK}"
export ConnectionStrings__DefaultConnection="$CONN_RAW"

echo "JacRed starting at $(date)"
echo "Effective config: $APP_CONFIG_LOCAL"
echo "Connection string: ${ConnectionStrings__DefaultConnection}"
echo "User: $(id -u):$(id -g)"

exec "$@"
