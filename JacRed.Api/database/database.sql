-- JacRed schema (PostgreSQL)
-- Поиск: произвольный текст по Title/Name/OriginalName (FTS + trigram)

-- UUID генерация
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- Для быстрого ILIKE '%...%' и похожих строк
CREATE EXTENSION IF NOT EXISTS pg_trgm;

--------------------------------------------------------------------------------
-- masterDb: Dictionary<string, TorrentInfo>
-- key = "search_name:search_originalname"
--------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.master_db
(
    key         text        PRIMARY KEY,     -- "банковский...:themillionpoundnote"
    update_time timestamptz NOT NULL,         -- TorrentInfo.updateTime
    file_time   bigint      NOT NULL          -- TorrentInfo.fileTime
);

COMMENT ON TABLE public.master_db IS 'Справочник masterDb: key="search_name:search_originalname" -> (updateTime, fileTime).';
COMMENT ON COLUMN public.master_db.key IS 'Ключ вида "search_name:search_originalname".';
COMMENT ON COLUMN public.master_db.file_time IS 'FileTimeUtc (long) из старого формата.';

CREATE INDEX IF NOT EXISTS ix_master_db_update_time
    ON public.master_db (update_time DESC);

CREATE INDEX IF NOT EXISTS ix_master_db_file_time
    ON public.master_db (file_time DESC);

--------------------------------------------------------------------------------
-- TorrentDetails (включая TorrentBaseDetails)
-- 1 строка = 1 раздача. Url уникальный (в твоём JSON он ключ словаря).
--------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.torrents
(
    id                  uuid        PRIMARY KEY DEFAULT gen_random_uuid(),

    tracker_name        text        NOT NULL,             -- TrackerName
    types               text[]      NOT NULL,             -- Types

    url                 text        NOT NULL UNIQUE,      -- Url (ключ в JSON)
    title               text        NOT NULL,             -- Title

    sid                 integer     NOT NULL DEFAULT 0,   -- Sid
    pir                 integer     NOT NULL DEFAULT 0,   -- Pir

    size_name           text        NULL,                 -- SizeName

    create_time         timestamptz NOT NULL,             -- CreateTime
    update_time         timestamptz NOT NULL,             -- UpdateTime
    check_time          timestamptz NOT NULL,             -- CheckTime

    magnet              text        NULL,                 -- Magnet

    name                text        NULL,                 -- Name
    original_name       text        NULL,                 -- OriginalName

    relased             integer     NOT NULL DEFAULT 0,   -- Relased (как в модели)

    languages           text[]      NULL,                 -- Languages (HashSet<string>)

    ffprobe             jsonb       NULL,                 -- Ffprobe (List<ffStream>)
    ffprobe_try_count   integer     NOT NULL DEFAULT 0,   -- FfprobeTryCount

    source_season_number text       NULL,                 -- SourceSeasonNumber
    source_season_order  text       NULL,                 -- SourceSeasonOrder

-- TorrentDetails
    size                double precision NOT NULL DEFAULT 0, -- Size (GB)
    quality             integer          NOT NULL DEFAULT 0, -- Quality
    video_type          text             NULL,               -- VideoType

    voices              text[]           NULL,               -- Voices (HashSet<string>)
    seasons             integer[]        NULL,               -- Seasons (HashSet<int>)

-- Полнотекстовый индексируемый столбец (для произвольного поиска)
    search_tsv          tsvector         NULL
    );

COMMENT ON TABLE public.torrents IS 'Раздачи (TorrentDetails). Поиск по произвольному тексту через search_tsv + trigram.';
COMMENT ON COLUMN public.torrents.types IS 'Types из модели (например: {serial,hd}).';
COMMENT ON COLUMN public.torrents.ffprobe IS 'Ffprobe (List<ffStream>) в jsonb без нормализации.';

-- Индексы под сортировки/фильтры
CREATE INDEX IF NOT EXISTS ix_torrents_sid
    ON public.torrents (sid DESC);

CREATE INDEX IF NOT EXISTS ix_torrents_tracker_sid
    ON public.torrents (tracker_name, sid DESC);

CREATE INDEX IF NOT EXISTS ix_torrents_update_time
    ON public.torrents (update_time DESC);

CREATE INDEX IF NOT EXISTS ix_torrents_check_time
    ON public.torrents (check_time DESC);

-- Trigram для быстрых '%текст%' по строкам
CREATE INDEX IF NOT EXISTS ix_torrents_title_trgm
    ON public.torrents USING gin (title gin_trgm_ops);

CREATE INDEX IF NOT EXISTS ix_torrents_name_trgm
    ON public.torrents USING gin (name gin_trgm_ops);

CREATE INDEX IF NOT EXISTS ix_torrents_original_name_trgm
    ON public.torrents USING gin (original_name gin_trgm_ops);

-- FTS индекс
CREATE INDEX IF NOT EXISTS ix_torrents_search_tsv
    ON public.torrents USING gin (search_tsv);

--------------------------------------------------------------------------------
-- Автогенерация search_tsv при вставке/обновлении Title/Name/OriginalName
--------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION public.torrents_update_search_tsv()
RETURNS trigger AS
$$
BEGIN
    NEW.search_tsv =
        setweight(to_tsvector('russian', coalesce(NEW.title, '')), 'A') ||
        setweight(to_tsvector('russian', coalesce(NEW.name, '')), 'B') ||
        setweight(to_tsvector('simple',  coalesce(NEW.original_name, '')), 'C');

RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_torrents_search_tsv ON public.torrents;

CREATE TRIGGER trg_torrents_search_tsv
    BEFORE INSERT OR UPDATE OF title, name, original_name
                     ON public.torrents
                         FOR EACH ROW EXECUTE FUNCTION public.torrents_update_search_tsv();

--------------------------------------------------------------------------------
-- Tracks (ffprobe)
--------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.tracks
(
    infohash   text        PRIMARY KEY,
    ffprobe    jsonb       NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE public.tracks IS 'ffprobe-���������� �� infohash.';

CREATE INDEX IF NOT EXISTS ix_tracks_updated_at
    ON public.tracks (updated_at DESC);

--------------------------------------------------------------------------------
-- Tracker stats
--------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.tracker_stats
(
    tracker_name text PRIMARY KEY,
    last_new_tor timestamptz NOT NULL,
    new_tor      integer     NOT NULL,
    update_count integer     NOT NULL,
    check_count  integer     NOT NULL,
    all_torrents integer     NOT NULL,
    tr_wait      integer     NOT NULL,
    tr_confirm   integer     NOT NULL,
    tr_error     integer     NOT NULL,
    updated_at   timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE public.tracker_stats IS '�������������� ���������� �� ��������.';

--------------------------------------------------------------------------------
-- Sync state
--------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.sync_state
(
    id         text PRIMARY KEY,
    last_sync  bigint      NOT NULL,
    start_sync bigint      NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE public.sync_state IS '��������� ������������� (last_sync/start_sync).';

CREATE INDEX IF NOT EXISTS ix_tracker_stats_updated_at
    ON public.tracker_stats (updated_at DESC);

CREATE INDEX IF NOT EXISTS ix_tracker_stats_last_new_tor
    ON public.tracker_stats (last_new_tor DESC);

CREATE INDEX IF NOT EXISTS ix_sync_state_updated_at
    ON public.sync_state (updated_at DESC);
