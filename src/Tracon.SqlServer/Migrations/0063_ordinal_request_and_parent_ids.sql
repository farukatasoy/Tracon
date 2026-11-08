-- Compares the id of an approval request and the parent of a workflow checkpoint ordinally.
--
-- Until now both columns took the collation of the database. SQL Server's default is case-INsensitive, while
-- the keys beside them compare by their characters: the parent of a checkpoint names the checkpoint_id of an
-- earlier row, and the request id of an approval is the one the run, the stream and the answer name. PostgreSQL,
-- SQLite and the in-memory store compare these ids by their characters too, so a comparison of the two
-- columns of one id would otherwise stop with "Cannot resolve the collation conflict", and a lookup by one
-- of them would answer differently on SQL Server than anywhere else.
--
-- The value does not change, the comparison does. Latin1_General_BIN2 compares the code points, as
-- PostgreSQL's "C" collation and the ordinal comparison of .NET do. Neither column is part of a key, a
-- constraint, an index or a view, so the comparison being finer can merge nothing: no row is merged,
-- removed or rewritten.
--
-- Trailing blanks still do not count: SQL Server ignores them in a comparison of any collation.
--
-- The change takes a schema lock on each table for as long as the statement runs. Start the new nodes after
-- this migration has been applied.

ALTER TABLE {schema}.pending_approvals ALTER COLUMN request_id nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

ALTER TABLE {schema}.workflow_checkpoints ALTER COLUMN parent_id nvarchar(200) COLLATE Latin1_General_BIN2 NULL;
