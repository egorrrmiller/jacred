# Руководство по Docker для JacRed

## Быстрый старт
- Использовать готовый образ: `docker compose up -d` (стянет `ghcr.io/egorrrmiller/jacred:latest`).
- Если нужно собрать локально (разработка): `docker build -t jacred:local .` и запустить `IMAGE_NAME=jacred:local docker compose up -d --build`.
- Конфиг: используйте `config.yml` рядом с `docker-compose.yml` (монтируется как `/app/config.yml`, копируется в рабочий `/app/config.local.yml` рядом с `JacRed.Api.dll`). Если файла нет — скопируйте `JacRed.Api/config.yml` и подправьте.
- Порт приложения берется из `listen-port` в `config.yml`. Наружный порт задается `APP_PORT` (по умолчанию 9117, должен совпадать с `listen-port`).
- Строка подключения: `ConnectionStrings__DefaultConnection` (в compose — `DB_CONNECTION`, по умолчанию `Host=db;Port=5432;Database=jacred;Username=jacred;Password=jacred;Timeout=30;CommandTimeout=60;`). Postgres по умолчанию только внутри сети compose; чтобы открыть наружу — раскомментируйте `ports` у сервиса `db` и при необходимости задайте `DB_PORT`.
- SQL-инициализация Postgres: файл `database.sql` уже в образе приложения и выполняется автоматически из `entrypoint.sh`, если база пуста (идемпотентно). Нужен только `postgresql-client` внутри образа (уже установлен).

## Переменные compose
- `APP_PORT` — внешний порт, должен совпадать с `listen-port` в `config.yml`.
- `HEALTHCHECK_PORT` — порт для healthcheck (по умолчанию `APP_PORT`/`9117`).
- `TZ`, `UMASK` — часовой пояс и маска прав в контейнере.
- `DB_NAME`, `DB_USER`, `DB_PASSWORD` — учетные данные Postgres (используются и в init SQL).
- `DB_CONNECTION` — полная строка подключения для приложения.
- `IMAGE_NAME` — тег образа для загрузки/сборки (по умолчанию `ghcr.io/egorrrmiller/jacred:latest`).
- `CONFIG_PATH` — путь к локальному `config.yml` (`./config.yml` по умолчанию).
- `INIT_DB` — выполнять ли инициализацию схемы при старте (true/false, по умолчанию true).

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
- `Publish Docker Image` — вручную через "Run workflow". Если указать `version` (semver) — пушит теги `v<version>`, `<version>`, `latest`, создает git-тег и GitHub Release. Если оставить `version` пустым — пушит диагностические теги `sha` и `<extra_tag>-<sha>` (по умолчанию `diag-<sha>`), без релиза и git-тега.

## Томa
- `jacred-db` — каталог данных Postgres (применение SQL — только на пустом томе).
