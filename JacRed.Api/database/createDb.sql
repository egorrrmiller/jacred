-- ============================
-- JacRed: create database
-- ============================

DO
$$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_database
        WHERE datname = 'jacred'
    ) THEN
        CREATE DATABASE jacred
            WITH
            OWNER = postgres
            ENCODING = 'UTF8'
            LC_COLLATE = 'ru_RU.UTF-8'
            LC_CTYPE = 'ru_RU.UTF-8'
            TEMPLATE = template0;
END IF;
END
$$;