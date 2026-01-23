#!/bin/sh
set -euo pipefail

# Config (теперь монтируется напрямую в /app/config.local.yml)
CONFIG_FILE="${CONFIG_FILE:-/app/config.local.yml}"

if [ ! -f "$CONFIG_FILE" ]; then
    echo "Config not found at $CONFIG_FILE. Mount it via docker-compose (CONFIG_PATH -> /app/config.local.yml)." >&2
    exit 1
fi

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
echo "Effective config: $CONFIG_FILE"
echo "Connection string: ${ConnectionStrings__DefaultConnection}"
echo "User: $(id -u):$(id -g)"

exec "$@"
