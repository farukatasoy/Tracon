-- Phase 142 -- tool-approval presentation.
--
-- For the column rationale see PostgreSQL 0045_pending_approval_presentation.sql.
-- presentation is nvarchar(max) (K-182 pattern, no ISJSON constraint -- only the
-- application writes it).

IF COL_LENGTH(N'{schema}.pending_approvals', N'presentation') IS NULL
ALTER TABLE {schema}.pending_approvals ADD presentation nvarchar(max) NULL;
