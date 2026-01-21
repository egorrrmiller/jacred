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

# DB settings
DB_HOST=${DB_HOST:-db}
DB_PORT=${DB_PORT:-5432}
DB_NAME=${DB_NAME:-jacred}
DB_USER=${DB_USER:-jacred}
DB_PASSWORD=${DB_PASSWORD:-jacred}

CONN_RAW_FALLBACK="Host=${DB_HOST};Port=${DB_PORT};Database=${DB_NAME};Username=${DB_USER};Password=${DB_PASSWORD};Timeout=30;CommandTimeout=60;"
CONN_RAW="${ConnectionStrings__DefaultConnection:-$CONN_RAW_FALLBACK}"
export ConnectionStrings__DefaultConnection="$CONN_RAW"

# Разбираем строку подключения для psql URI
parse_field() {
    key="$1"
    echo "$CONN_RAW" | tr ';' '\n' | awk -F= -v k="$key" 'BEGIN{IGNORECASE=1} tolower($1)==tolower(k){print $2}'
}

HOST_PARSED="$(parse_field Host)"
PORT_PARSED="$(parse_field Port)"
DB_PARSED="$(parse_field Database)"
USER_PARSED="$(parse_field Username)"
PASS_PARSED="$(parse_field Password)"

HOST_PARSED=${HOST_PARSED:-$DB_HOST}
PORT_PARSED=${PORT_PARSED:-$DB_PORT}
DB_PARSED=${DB_PARSED:-$DB_NAME}
USER_PARSED=${USER_PARSED:-$DB_USER}
PASS_PARSED=${PASS_PARSED:-$DB_PASSWORD}

PSQL_URI="postgresql://${USER_PARSED}:${PASS_PARSED}@${HOST_PARSED}:${PORT_PARSED}/${DB_PARSED}"

echo "JacRed starting at $(date)"
echo "Effective config: $APP_CONFIG_LOCAL"
echo "Connection string: ${ConnectionStrings__DefaultConnection}"
echo "User: $(id -u):$(id -g)"

# DB init (idempotent)
if [ "${INIT_DB:-true}" = "true" ] && [ -f /app/database.sql ]; then
    echo "Checking database schema..."

    for i in $(seq 1 60); do
        if psql "$PSQL_URI" -Atqc "select 1" >/dev/null 2>/tmp/psql.err; then
            READY=1
            break
        fi
        if [ $i -eq 1 ]; then
            echo "Postgres not ready, first error: $(cat /tmp/psql.err 2>/dev/null)"
        fi
        echo "Postgres not ready, retry $i/60..."
        sleep 2
    done

    if [ "${READY:-0}" -eq 1 ]; then
        TABLES=$(psql "$PSQL_URI" -Atqc "select count(*) from information_schema.tables where table_schema='public';" 2>/dev/null || echo 0)
        if [ "${TABLES:-0}" -eq 0 ]; then
            echo "Applying database.sql..."
            psql "$PSQL_URI" -f /app/database.sql || echo "Database init failed (ignored)"
        else
            echo "Database already initialized (tables: $TABLES)"
        fi
    else
        echo "Database not reachable after waiting, skipping init."
    fi
fi

exec "$@"
