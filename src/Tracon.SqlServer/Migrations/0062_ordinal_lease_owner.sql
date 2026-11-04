-- Compares the owner of a job lease and the owner of a singleton lease ordinally.
--
-- 🚨 Until now both columns took the collation of the database. SQL Server's default is case-INsensitive,
-- so a worker named "worker-a" held the lease of a worker named "Worker-A": it renewed it, started the job,
-- completed it and released it. The caller chooses the name (JobLeaseRequest.Owner and the owner of
-- ISingletonLeaseStore.TryAcquireAsync are public), and PostgreSQL, SQLite and the in-memory store compare
-- the name by its characters, so the same system gave two answers to the question "is this lease yours".
--
-- The value does not change, the comparison does. Latin1_General_BIN2 compares the code points, as
-- PostgreSQL's "C" collation and the ordinal comparison of .NET do. Neither column is part of a key, a
-- constraint, an index or a view, and neither is compared with another column, so the comparison being
-- finer can merge nothing: no row is merged, removed or rewritten, and a lease that is running keeps its
-- owner. (The earlier identity migration left the singleton owner alone because the library writes that name
-- itself; the name is a parameter of a public method, so that reason did not hold.)
--
-- Trailing blanks still do not count: SQL Server ignores them in a comparison of any collation, so "w" and
-- "w " are one owner.
--
-- The change takes a schema lock on each table for as long as the statement runs. It does not rewrite the
-- rows (measured: about ten milliseconds for a million jobs), but it waits for the transactions that hold
-- the table, and the workers wait for it in turn. Start the new nodes after this migration has been applied.

ALTER TABLE {schema}.jobs ALTER COLUMN lease_owner nvarchar(200) COLLATE Latin1_General_BIN2 NULL;

ALTER TABLE {schema}.singleton_leases ALTER COLUMN owner_id nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;
