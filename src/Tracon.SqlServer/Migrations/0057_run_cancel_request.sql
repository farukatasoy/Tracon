-- Cross-instance run cancellation.
--
-- For the rationale and the column meaning see PostgreSQL 0069_run_cancel_request.sql.

IF COL_LENGTH(N'{schema}.runs', N'cancel_requested_at') IS NULL
ALTER TABLE {schema}.runs ADD cancel_requested_at datetimeoffset NULL;
