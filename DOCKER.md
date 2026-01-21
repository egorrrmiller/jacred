# Руководство по Docker для JacRed

## Быстрый старт
- Использовать готовый образ: `docker compose up -d` (стянет `ghcr.io/egorrrmiller/jacred:latest`)
- Если нужно собрать локально (например, при разработке): `docker build -t jacred:local .` и запустить `IMAGE_NAME=jacred:local docker compose up -d --build`
- Конфиг: используйте `config.yml` рядом с `docker-compose.yml` (монтируется в контейнер как `/app/config.yml`, копируется в рабочий `/app/config.local.yml` рядом с `JacRed.Api.dll`). Если файла нет — скопируйте `JacRed.Api/config.yml` и подправьте.
- Порт приложения берётся из `listen-port` в `config.yml`. Наружный порт задаётся `APP_PORT` (по умолчанию `9117`, должен совпадать с `listen-port`).
- Строка подключения: переменная `ConnectionStrings__DefaultConnection` (в compose — `DB_CONNECTION`, по умолчанию `Host=db;Port=5432;Database=jacred;Username=jacred;Password=jacred;Timeout=30;CommandTimeout=60;`). Postgres по умолчанию только внутри сети compose; чтобы открыть наружу — раскомментируйте `ports` у сервиса `db` и при необходимости задайте `DB_PORT`.

## Переменные compose
- `APP_PORT` — внешний порт, должен совпадать с `listen-port` в `config.yml`.
- `HEALTHCHECK_PORT` — порт для healthcheck (по умолчанию `APP_PORT`/`9117`).
- `TZ`, `UMASK` — часовой пояс и маска прав в контейнере.
- `DB_NAME`, `DB_USER`, `DB_PASSWORD` — учётные данные Postgres (используются и в init SQL).
- `DB_CONNECTION` — полная строка подключения для приложения.
- `IMAGE_NAME` — тег образа для загрузки/сборки (по умолчанию `ghcr.io/egorrrmiller/jacred:latest`).
- `CONFIG_PATH` — путь к локальному `config.yml` (`./config.yml` по умолчанию).

### Пример .env для docker-compose
Создайте `.env` рядом с `docker-compose.yml` (можно начать с `.env.example`):
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
Запуск с `.env`:
```bash
docker compose --env-file .env up -d --build
```
Если `--env-file` не указать, compose автоматически прочитает `.env` из каталога с `docker-compose.yml`.

## GitHub Actions
- `CI` — собирает .NET и делает docker build (без push) на пушах/PR.
- `Publish Docker Image` — запускается вручную через "Run workflow", требует `version` (semver). Делает мультиарх сборку, пушит в GHCR, создаёт git-тег `v<version>` и GitHub Release с тем же именем. Теги образа: `v<version>`, `<version>`, `latest`.

## Томa
- `jacred-db` — каталог данных Postgres.
