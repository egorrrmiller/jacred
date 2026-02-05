# AI Документация
[![DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/egorrrmiller/jacred)

# JacRed

Торрент-трекер агрегатор.

---

## 🚀 Быстрый старт

### 1. Подготовка окружения
Создайте файл `.env` рядом с `docker-compose.yml`:

```env
APP_PORT=9117
HEALTHCHECK_PORT=9117
CONFIG_PATH=./config.yml

DB_HOST=db
DB_PORT=5432
DB_NAME=jacred
DB_USER=jacred
DB_PASSWORD=jacred
# DB_CONNECTION=Host=db;Port=5432;Database=jacred;Username=jacred;Password=jacred;Timeout=30;CommandTimeout=60;

IMAGE_NAME=ghcr.io/egorrrmiller/jacred:latest
```

### 2. Конфигурация
Скопируйте пример конфига в файл `config.yml` рядом с `docker-compose.yml`.
Файл будет автоматически примонтирован в контейнер как `/app/config.local.yml`.

### 3. Запуск
```bash
# Стандартный запуск (если .env лежит рядом)
docker-compose up -d --build

# Если .env файл находится в другой директории
docker-compose --env-file /path/to/.env up -d --build
```

---

## 🐳 Docker Compose

Пример файла `docker-compose.yml` для развертывания:

```yaml
name: jacred

services:
  jacred:
    image: ${IMAGE_NAME:-ghcr.io/egorrrmiller/jacred:latest}
    container_name: jacred
    restart: unless-stopped
    depends_on:
      db:
        condition: service_healthy
    environment:
      TZ: ${TZ:-UTC}
      UMASK: ${UMASK:-0027}
      HEALTHCHECK_PORT: ${HEALTHCHECK_PORT:-9117}
      ConnectionStrings__DefaultConnection: ${DB_CONNECTION:-Host=db;Port=${DB_PORT:-5432};Database=${DB_NAME:-jacred};Username=${DB_USER:-jacred};Password=${DB_PASSWORD:-jacred};Timeout=30;CommandTimeout=60;}
    ports:
      - "${APP_PORT:-9117}:${APP_PORT:-9117}"
    volumes:
      - ${CONFIG_PATH:-./config.yml}:/app/config.local.yml:ro
    healthcheck:
      test: ["CMD-SHELL", "wget --quiet --spider http://127.0.0.1:${HEALTHCHECK_PORT:-9117}"]
      interval: 30s
      timeout: 15s
      retries: 3
      start_period: 45s

  db:
    image: postgres:16-alpine
    container_name: jacred-db
    restart: unless-stopped
    environment:
      POSTGRES_DB: ${DB_NAME:-jacred}
      POSTGRES_USER: ${DB_USER:-jacred}
      POSTGRES_PASSWORD: ${DB_PASSWORD:-jacred}
    expose:
      - "5432"
    # Раскомментируйте для доступа к БД снаружи
    # ports:
    #   - "${DB_PORT:-5432}:5432"
    volumes:
      - jacred-db:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${DB_USER:-jacred} -d ${DB_NAME:-jacred}"]
      interval: 10s
      timeout: 5s
      retries: 5
      start_period: 30s

volumes:
  jacred-db:
```

### Переменные окружения
| Переменная | Описание |
|------------|----------|
| `APP_PORT` | Внешний порт приложения |
| `HEALTHCHECK_PORT` | Порт для проверки здоровья контейнера |
| `TZ`, `UMASK` | Часовой пояс и маска прав доступа |
| `DB_*` | Параметры подключения к Postgres (Host, Port, Name, User, Password) |
| `DB_CONNECTION` | Полная строка подключения (перекрывает параметры `DB_*`) |
| `IMAGE_NAME` | Тег Docker-образа |
| `CONFIG_PATH` | Путь к локальному файлу конфигурации |

---

## ⚙️ Конфигурация (config.yml)

Полный список категорий RuTracker доступен [здесь](https://github.com/egorrrmiller/jacred/tree/main/JacRed.Infrastructure/Services/Trackers/RuTracker/RuTrackers_categories.md).

```yaml
##### Настройка сервера
listen-ip: any          # IP-адрес для прослушивания (any = 0.0.0.0)
listen-port: 9117       # Внутренний порт веб-сервера
api-key: 'key'          # API-ключ для доступа к методам
web: true               # Включить веб-интерфейс

##### Настройка выдачи
max-result-count: 250   # Максимальное количество результатов в ответе
merge-duplicates: true  # Объединять дубликаты по InfoHash
merge-num-duplicates: true # Объединять дубликаты с разными суффиксами в названии

# Интеграция с TorrServer (ffprobe/языки)
ffprobe:
  enable: true          # Включить получение метаданных
  timeout: 10           # Таймаут запроса в минутах
  tsuri: 'http://localhost:5665' # Адрес TorrServer
  batch-size: 5         # Размер пакета обработки
  attempts: 3           # Количество попыток
  authorization:
    login: 'login'      # Логин TorrServer
    password: 'password' # Пароль TorrServer

##### Настройка трекеров

rutracker:
  enable-search: true   # Включить поиск по трекеру
  enable-sync: true     # Включить фоновую синхронизацию (новинки, популярное)

  # Точечный рефреш торрентов в базе
  refresh:
    enable: true        # Включить обновление
    timeout: 60         # Интервал обновления в минутах
    older-than-min: 120 # Обновлять записи старше N минут
    limit: 100          # Лимит записей за один проход
  
  # Обновление популярных раздач
  popular:
    enable: true        # Включить парсинг популярных
    timeout: 30         # Интервал обновления в минутах
    max-pages: 5        # Глубина парсинга страниц
    categories:         # ID категорий для парсинга
      [ 111, 222, 333 ]

  authorization:
    login: ''           # Логин на трекере
    password: ''        # Пароль на трекере

animelayer:
  enable-search: false  # Включить поиск по трекеру
  authorization:
    login: ''
    password: ''

nnmclub:
  enable-search: true   # Включить поиск по трекеру

aniliberty:
  enable-search: true   # Включить поиск по трекеру

rutor:
  enable-search: true   # Включить поиск по трекеру
```

---

## 📝 Примечания
- **Миграции БД**: Выполняются автоматически при старте контейнера.
- **Сброс данных**: Команда для полной переинициализации (с удалением базы данных):
  ```bash
  docker-compose down -v && docker-compose up -d --build
  ```
