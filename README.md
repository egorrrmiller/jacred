# AI Документация
[![DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/egorrrmiller/jacred)

# JacRed

Торрент-трекер агрегатор.

## Установка
- Подготовка: положите `config.yml` рядом с `docker-compose.yml` (можно скопировать `JacRed.Api/config.yml`) и создайте `.env`. Файл будет примонтирован в контейнер как `/app/config.local.yml`.
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
- Запуск/обновление: `docker compose --env-file .env up -d --build`.
- База данных: контейнер `db` создаст пользователя/БД из `DB_*`, приложение прогонит миграции автоматически при старте. Для полной переинициализации: `docker compose down -v && docker compose up -d --build` — удалит данные тома `jacred-db`.
- Порты: приложение слушает `listen-port` из `config.yml`, наружный порт задает `APP_PORT`. Postgres доступен внутри сети compose (`db:5432`); чтобы открыть наружу, раскомментируйте `ports` у сервиса `db`.

## Пример конфига
```yaml
##### настройка сервера
listen-ip: any
listen-port: 9117
api-key: 'key'
web: true

##### настройка выдачи

# если у раздач одинаковый infohash, считаем это один торрент; объединяем их метаданные (сид/личи, размеры, названия, ссылки) в одну итоговую запись.
merge-duplicates: true

##### настройка трекеров
# список трекеров для синхронизации актуальности раздач, категорий
sync-trackers:
  - rutracker
  - aniliberty

# трекеры, результаты которых будут удалены из ответа
disable-trackers:
  - aniliberty

rutracker:
  # точечный рефреш всех торрентов рутрекера в базе
  refresh:
    enable: true # включить/выключить
    timeout: 60 # задержка в минутах 
    older-than-min: 120 # старше чем n минут
    limit: 100 # количество торрентов для обновления за раз
  
  # Обновление популярных раздач по категориям 
  popular:
    enable: true # включить/выключить
    timeout: 30 # задержка в минутах
    max-pages: 5 # глубина обхода в каждой категории
    categories: # список категорий для парсинга (например: [ 549, 22, 1666 ]) 
      []

  authorization:
    login: ''
    password: ''
```

## Переменные Docker Compose
- `APP_PORT` - внешний порт приложения.
- `HEALTHCHECK_PORT` - порт для healthcheck.
- `TZ`, `UMASK` - часовой пояс и маска прав.
- `DB_HOST`, `DB_PORT`, `DB_NAME`, `DB_USER`, `DB_PASSWORD` - параметры Postgres.
- `DB_CONNECTION` - полная строка подключения (опционально, перекрывает сборку из `DB_*`).
- `IMAGE_NAME` - тег образа приложения.
- `CONFIG_PATH` - путь к локальному `config.yml`.

## Примечания
- Миграции выполняет сама JacRed при старте.
- Конфиг монтируется как `/app/config.local.yml`.
