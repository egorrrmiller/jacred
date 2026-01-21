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

# Одноразовая инициализация схемы БД (идемпотентно)
if [ "${INIT_DB:-true}" = "true" ] && [ -f /app/database.sql ]; then
    echo "Checking database schema..."
    CONN_RAW="${ConnectionStrings__DefaultConnection:-Host=db;Port=5432;Database=jacred;Username=jacred;Password=jacred;}"
    # Превращаем semicolon-connstring в формат key=value для psql
    CONN_PARSED=$(echo "$CONN_RAW" | tr ';' ' ' | sed 's/[Hh]ost/host/g; s/[Pp]ort/port/g; s/[Dd]atabase/dbname/g; s/[Uu]sername/user/g; s/[Pp]assword/password/g; s/[Tt]imeout=[^ ]*//g; s/[Cc]ommand[Tt]imeout=[^ ]*//g' | xargs)

    # Ждём доступности Postgres (до 60 секунд)
    for i in $(seq 1 30); do
        if psql $CONN_PARSED -Atqc "select 1" >/dev/null 2>&1; then
            READY=1
            break
        fi
        echo "Postgres not ready, retry $i/30..."
        sleep 2
    done

    if [ "${READY:-0}" -eq 1 ]; then
        TABLES=$(psql $CONN_PARSED -Atqc "select count(*) from information_schema.tables where table_schema='public';" 2>/dev/null || echo 0)
        if [ "${TABLES:-0}" -eq 0 ]; then
            echo "Applying database.sql..."
            psql $CONN_PARSED -f /app/database.sql || echo "Database init failed (ignored)"
        else
            echo "Database already initialized (tables: $TABLES)"
        fi
    else
        echo "Database not reachable after waiting, skipping init."
    fi
fi

exec "$@"
