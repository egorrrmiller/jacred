# AI Документация
[![DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/egorrrmiller/jacred)

# JacRed

Торрент-трекер агрегатор с API (Torznab и REST) на .NET.

## Установка
- Подготовка: положите `config.yml` рядом с `docker-compose.yml` (можно скопировать `JacRed.Api/config.yml`) и создайте `.env`.
- Пример `.env`:
  ```env
  APP_PORT=9117
  HEALTHCHECK_PORT=9117
  CONFIG_PATH=./config.yml

  DB_HOST=db
  DB_PORT=5432
  DB_NAME=jacred
  DB_USER=jacred
  DB_PASSWORD=jacred
  # при необходимости: DB_CONNECTION=Host=db;Port=5432;Database=jacred;Username=jacred;Password=jacred;Timeout=30;CommandTimeout=60;

  IMAGE_NAME=ghcr.io/egorrrmiller/jacred:latest
  ```
- Запуск/обновление: `docker compose --env-file .env up -d --build` (без `--env-file` compose берет `.env` из каталога).
- База данных: контейнер `db` создаст пользователя/БД из `DB_*`; `database.sql` применится автоматически при первом старте пустого тома. Для переинициализации: `docker compose down -v && docker compose up -d --build` (удалит данные тома `jacred-db`).
- Порты: приложение слушает `listen-port` из `config.yml`, наружный порт задает `APP_PORT`. Postgres доступен внутри сети compose (`db:5432`); чтобы открыть наружу, раскомментируйте `ports` у сервиса `db`.

## Переменные Docker Compose
- `APP_PORT` — внешний порт приложения.
- `HEALTHCHECK_PORT` — порт для healthcheck.
- `TZ`, `UMASK` — часовой пояс и маска прав.
- `DB_HOST`, `DB_PORT`, `DB_NAME`, `DB_USER`, `DB_PASSWORD` — параметры Postgres.
- `DB_CONNECTION` — полная строка подключения (опционально, перекрывает сборку из `DB_*`).
- `IMAGE_NAME` — тег образа приложения.
- `CONFIG_PATH` — путь к локальному `config.yml`.

## Примечания
- SQL применится только на пустом томе БД.
- Конфиг монтируется как `/app/config.yml`, рабочая копия — `/app/config.local.yml`.

