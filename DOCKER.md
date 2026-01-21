# Руководство по Docker для JacRed

## Быстрый старт
- Локальная сборка: `docker build -t jacred:local .`
- Запуск с Postgres: `docker compose up -d --build`
- Конфигурация: по умолчанию берётся `config.yml` из корня рядом с `docker-compose.yml` (монтируется в `/app/config.yml`, то есть рядом с `JacRed.Api.dll`). Контейнер сам копирует его в `config.local.yml`, откуда читает приложение. Если файла нет — скопируйте `JacRed.Api/config.yml` и подправьте под себя.
- Приложение слушает порт из `listen-port` в `config.yml`. Вне контейнера порт задаётся переменной `APP_PORT` (по умолчанию `9117`; значение должно совпадать с `listen-port`).
- Строка подключения берётся из `ConnectionStrings__DefaultConnection` (в compose переменная `DB_CONNECTION`, по умолчанию `Host=db;Port=5432;Database=jacred;Username=jacred;Password=jacred;Timeout=30;CommandTimeout=60;`). Postgres по умолчанию доступен только внутри сети compose; чтобы открыть наружу — раскомментируйте `ports` у сервиса `db` и при необходимости задайте `DB_PORT`.

## Переменные compose
- `APP_PORT` — внешний порт приложения; ставьте такое же значение, как `listen-port` в `config.yml`.
- `HEALTHCHECK_PORT` — порт для healthcheck контейнера (по умолчанию `APP_PORT`/`9117`).
- `TZ`, `UMASK` — часовой пояс и маска прав в контейнере приложения.
- `DB_NAME`, `DB_USER`, `DB_PASSWORD` — учётные данные Postgres (используются и в init SQL).
- `DB_CONNECTION` — переопределение строки подключения ADO.NET, передаваемой приложению.
- `IMAGE_NAME` — тег образа для локальной сборки/загрузки (по умолчанию `ghcr.io/egorrrmiller/jacred:latest`).
- `CONFIG_PATH` — путь к локальному `config.yml`, который монтируется в контейнер (`./config.yml` по умолчанию).

### Пример .env для docker-compose
Сохраните `.env` рядом с `docker-compose.yml` (можно начать с `.env.example`):
```env
APP_PORT=9117
HEALTHCHECK_PORT=9117
TZ=UTC
UMASK=0027
CONFIG_PATH=./config.yml

DB_NAME=jacred
DB_USER=jacred
DB_PASSWORD=jacred
DB_CONNECTION=Host=db;Port=5432;Database=jacred;Username=jacred;Password=jacred;Timeout=30;CommandTimeout=60;

IMAGE_NAME=ghcr.io/egorrrmiller/jacred:latest
```
Запуск с использованием `.env`:
```bash
docker compose --env-file .env up -d --build
```
При отсутствии `--env-file` docker-compose автоматически читает `.env` из каталога с `docker-compose.yml`.

## GitHub Actions
- `CI` — собирает .NET и делает docker build (без push) на пушах/PR.
- `Publish Docker Image` — собирает и публикует мультиарх образ в GHCR. Запуск вручную (опционально `version`) или на GitHub Release. Теги: `latest` для `main`/release, теги веток и `sha` для остальных, плюс semver при заданном `version`.

## Томa
- `jacred-db` → каталог данных Postgres.
