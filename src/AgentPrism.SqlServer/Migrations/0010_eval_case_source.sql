-- Faz 45 -- uretimden eval vakasi terfisi (F-53).
--
-- Gerekce ve sutun anlamlari icin PostgreSQL 0022_eval_case_source.sql'e bakin.

IF COL_LENGTH(N'{schema}.eval_cases', N'source_run_id') IS NULL
ALTER TABLE {schema}.eval_cases ADD source_run_id uniqueidentifier NULL;

IF COL_LENGTH(N'{schema}.eval_cases', N'source_kind') IS NULL
ALTER TABLE {schema}.eval_cases ADD source_kind smallint NULL;

IF COL_LENGTH(N'{schema}.eval_cases', N'promoted_at') IS NULL
ALTER TABLE {schema}.eval_cases ADD promoted_at datetimeoffset NULL;

-- Kismi (filtreli) benzersiz indeks: source_run_id NULL olan (elle yazilmis)
-- satirlar kisitin disindadir. Sozdizimi PostgreSQL ile aynidir (K-178 devir notu).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'eval_cases_source_run_uq' AND object_id = OBJECT_ID(N'{schema}.eval_cases'))
CREATE UNIQUE INDEX eval_cases_source_run_uq
    ON {schema}.eval_cases (suite_id, source_run_id)
    WHERE source_run_id IS NOT NULL;
