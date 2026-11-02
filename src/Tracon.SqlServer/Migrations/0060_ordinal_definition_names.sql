-- Compares the names of definitions ordinally.
--
-- 🚨 Until now these columns took the collation of the database. SQL Server's default is case-INsensitive,
-- so two definitions whose names differ only in letter case ("Support" and "support") were ONE row here,
-- while PostgreSQL and SQLite kept them apart. The second save replaced the first without a sign (every
-- definition table is written by an upsert), a read by either spelling returned the content of the other,
-- and the runs, the sessions and the statistics of the two agents were counted as one.
--
-- The value does not change, the comparison does. Latin1_General_BIN2 compares the code points, as
-- PostgreSQL's "C" collation and the ordinal comparison of .NET do. Names that were unique under the
-- case-insensitive comparison stay unique under the ordinal one (it is the finer of the two), so no row is
-- merged, removed or rewritten.
--
-- One identity, one collation. The name of an agent lives in the table of its definition and again in every
-- table that records what ran under it (runs, sessions, conversations, voice sessions, quotas, approval
-- rules, experiments, files). SQL Server refuses to compare two columns of different collations ("Cannot
-- resolve the collation conflict"), and a filter on the one column left case-insensitive would still list
-- the runs of two agents as one. Every column that holds the name of an agent, a workflow, a skill, a skill
-- script or a tool changes together, and so do the target a schedule, a job or a trigger names and the entity an audit
-- entry names (it carries the name of a definition, such as "agent:Support", and the audit list filters on it).
--
-- Left as they are, on purpose: the labels a run or a span carries for itself (a workflow node, a span name,
-- a score name that an earlier migration already moved), the names of providers and tenants (the library
-- folds those to lower case before it stores them), and the names of handlers and lanes (the library chooses
-- them in its own code).
--
-- Every index or constraint that holds one of the columns is dropped, the column changes, and the index or
-- constraint comes back with the definition it had. The runner applies a migration in one transaction, so a
-- failure leaves the old shape. While it runs, each table is locked against schema changes and the columns
-- are rewritten: plan the upgrade of a large runs table for a quiet hour, and raise the command timeout for
-- the run that applies it.

-- ---------------------------------------------------------------------------
-- agent_definitions
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'agent_definitions_tenant_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.agent_definitions'))
ALTER TABLE {schema}.agent_definitions DROP CONSTRAINT agent_definitions_tenant_name_uq;

ALTER TABLE {schema}.agent_definitions ALTER COLUMN name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'agent_definitions_tenant_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.agent_definitions'))
ALTER TABLE {schema}.agent_definitions ADD CONSTRAINT agent_definitions_tenant_name_uq UNIQUE (tenant_id, name);

-- ---------------------------------------------------------------------------
-- agent_definition_version_counters
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'agent_definition_version_counters_pk' AND parent_object_id = OBJECT_ID(N'{schema}.agent_definition_version_counters'))
ALTER TABLE {schema}.agent_definition_version_counters DROP CONSTRAINT agent_definition_version_counters_pk;

ALTER TABLE {schema}.agent_definition_version_counters ALTER COLUMN name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'agent_definition_version_counters_pk' AND parent_object_id = OBJECT_ID(N'{schema}.agent_definition_version_counters'))
ALTER TABLE {schema}.agent_definition_version_counters ADD CONSTRAINT agent_definition_version_counters_pk PRIMARY KEY (tenant_id, name);

-- ---------------------------------------------------------------------------
-- workflows
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'workflows_tenant_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.workflows'))
ALTER TABLE {schema}.workflows DROP CONSTRAINT workflows_tenant_name_uq;

ALTER TABLE {schema}.workflows ALTER COLUMN name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'workflows_tenant_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.workflows'))
ALTER TABLE {schema}.workflows ADD CONSTRAINT workflows_tenant_name_uq UNIQUE (tenant_id, name);

-- ---------------------------------------------------------------------------
-- workflow_definition_versions
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'workflow_definition_versions_pk' AND parent_object_id = OBJECT_ID(N'{schema}.workflow_definition_versions'))
ALTER TABLE {schema}.workflow_definition_versions DROP CONSTRAINT workflow_definition_versions_pk;

ALTER TABLE {schema}.workflow_definition_versions ALTER COLUMN name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'workflow_definition_versions_pk' AND parent_object_id = OBJECT_ID(N'{schema}.workflow_definition_versions'))
ALTER TABLE {schema}.workflow_definition_versions ADD CONSTRAINT workflow_definition_versions_pk PRIMARY KEY (tenant_id, name, version);

-- ---------------------------------------------------------------------------
-- agent_skills
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'agent_skills_tenant_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.agent_skills'))
ALTER TABLE {schema}.agent_skills DROP CONSTRAINT agent_skills_tenant_name_uq;

ALTER TABLE {schema}.agent_skills ALTER COLUMN name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'agent_skills_tenant_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.agent_skills'))
ALTER TABLE {schema}.agent_skills ADD CONSTRAINT agent_skills_tenant_name_uq UNIQUE (tenant_id, name);

-- ---------------------------------------------------------------------------
-- agent_skill_resources
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'agent_skill_resources_skill_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.agent_skill_resources'))
ALTER TABLE {schema}.agent_skill_resources DROP CONSTRAINT agent_skill_resources_skill_name_uq;

ALTER TABLE {schema}.agent_skill_resources ALTER COLUMN name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'agent_skill_resources_skill_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.agent_skill_resources'))
ALTER TABLE {schema}.agent_skill_resources ADD CONSTRAINT agent_skill_resources_skill_name_uq UNIQUE (skill_id, name);

-- ---------------------------------------------------------------------------
-- agent_skill_scripts
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'agent_skill_scripts_skill_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.agent_skill_scripts'))
ALTER TABLE {schema}.agent_skill_scripts DROP CONSTRAINT agent_skill_scripts_skill_name_uq;

ALTER TABLE {schema}.agent_skill_scripts ALTER COLUMN name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'agent_skill_scripts_skill_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.agent_skill_scripts'))
ALTER TABLE {schema}.agent_skill_scripts ADD CONSTRAINT agent_skill_scripts_skill_name_uq UNIQUE (skill_id, name);

-- ---------------------------------------------------------------------------
-- skill_script_grants
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'skill_script_grants_uq' AND parent_object_id = OBJECT_ID(N'{schema}.skill_script_grants'))
ALTER TABLE {schema}.skill_script_grants DROP CONSTRAINT skill_script_grants_uq;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'skill_script_grants_lookup_idx' AND object_id = OBJECT_ID(N'{schema}.skill_script_grants'))
DROP INDEX skill_script_grants_lookup_idx ON {schema}.skill_script_grants;

ALTER TABLE {schema}.skill_script_grants ALTER COLUMN skill_name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

ALTER TABLE {schema}.skill_script_grants ALTER COLUMN script_name nvarchar(200) COLLATE Latin1_General_BIN2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'skill_script_grants_uq' AND parent_object_id = OBJECT_ID(N'{schema}.skill_script_grants'))
ALTER TABLE {schema}.skill_script_grants ADD CONSTRAINT skill_script_grants_uq UNIQUE (tenant_id, skill_name, script_name);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'skill_script_grants_lookup_idx' AND object_id = OBJECT_ID(N'{schema}.skill_script_grants'))
CREATE INDEX skill_script_grants_lookup_idx ON {schema}.skill_script_grants (tenant_id, skill_name)
    WHERE revoked_at IS NULL;

-- ---------------------------------------------------------------------------
-- eval_suites
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'eval_suites_tenant_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.eval_suites'))
ALTER TABLE {schema}.eval_suites DROP CONSTRAINT eval_suites_tenant_name_uq;

ALTER TABLE {schema}.eval_suites ALTER COLUMN name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

ALTER TABLE {schema}.eval_suites ALTER COLUMN agent_name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'eval_suites_tenant_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.eval_suites'))
ALTER TABLE {schema}.eval_suites ADD CONSTRAINT eval_suites_tenant_name_uq UNIQUE (tenant_id, name);

-- ---------------------------------------------------------------------------
-- experiments
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'experiments_tenant_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.experiments'))
ALTER TABLE {schema}.experiments DROP CONSTRAINT experiments_tenant_name_uq;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'experiments_running_agent_uq' AND object_id = OBJECT_ID(N'{schema}.experiments'))
DROP INDEX experiments_running_agent_uq ON {schema}.experiments;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'experiments_tenant_agent_idx' AND object_id = OBJECT_ID(N'{schema}.experiments'))
DROP INDEX experiments_tenant_agent_idx ON {schema}.experiments;

ALTER TABLE {schema}.experiments ALTER COLUMN name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

ALTER TABLE {schema}.experiments ALTER COLUMN agent_name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'experiments_tenant_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.experiments'))
ALTER TABLE {schema}.experiments ADD CONSTRAINT experiments_tenant_name_uq UNIQUE (tenant_id, name);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'experiments_running_agent_uq' AND object_id = OBJECT_ID(N'{schema}.experiments'))
CREATE UNIQUE INDEX experiments_running_agent_uq ON {schema}.experiments (tenant_id, agent_name)
    WHERE status = 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'experiments_tenant_agent_idx' AND object_id = OBJECT_ID(N'{schema}.experiments'))
CREATE INDEX experiments_tenant_agent_idx ON {schema}.experiments (tenant_id, agent_name);

-- ---------------------------------------------------------------------------
-- inbound_triggers
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'inbound_triggers_tenant_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.inbound_triggers'))
ALTER TABLE {schema}.inbound_triggers DROP CONSTRAINT inbound_triggers_tenant_name_uq;

ALTER TABLE {schema}.inbound_triggers ALTER COLUMN name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

ALTER TABLE {schema}.inbound_triggers ALTER COLUMN target_name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'inbound_triggers_tenant_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.inbound_triggers'))
ALTER TABLE {schema}.inbound_triggers ADD CONSTRAINT inbound_triggers_tenant_name_uq UNIQUE (tenant_id, name);

-- ---------------------------------------------------------------------------
-- job_schedules
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'job_schedules_tenant_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.job_schedules'))
ALTER TABLE {schema}.job_schedules DROP CONSTRAINT job_schedules_tenant_name_uq;

ALTER TABLE {schema}.job_schedules ALTER COLUMN name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

ALTER TABLE {schema}.job_schedules ALTER COLUMN target_name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'job_schedules_tenant_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.job_schedules'))
ALTER TABLE {schema}.job_schedules ADD CONSTRAINT job_schedules_tenant_name_uq UNIQUE (tenant_id, name);

-- ---------------------------------------------------------------------------
-- mcp_servers
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'mcp_servers_tenant_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.mcp_servers'))
ALTER TABLE {schema}.mcp_servers DROP CONSTRAINT mcp_servers_tenant_name_uq;

ALTER TABLE {schema}.mcp_servers ALTER COLUMN name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'mcp_servers_tenant_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.mcp_servers'))
ALTER TABLE {schema}.mcp_servers ADD CONSTRAINT mcp_servers_tenant_name_uq UNIQUE (tenant_id, name);

-- ---------------------------------------------------------------------------
-- webhook_subscriptions
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'webhook_subscriptions_tenant_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.webhook_subscriptions'))
ALTER TABLE {schema}.webhook_subscriptions DROP CONSTRAINT webhook_subscriptions_tenant_name_uq;

ALTER TABLE {schema}.webhook_subscriptions ALTER COLUMN name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'webhook_subscriptions_tenant_name_uq' AND parent_object_id = OBJECT_ID(N'{schema}.webhook_subscriptions'))
ALTER TABLE {schema}.webhook_subscriptions ADD CONSTRAINT webhook_subscriptions_tenant_name_uq UNIQUE (tenant_id, name);

-- ---------------------------------------------------------------------------
-- agent_files
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'agent_files_tenant_agent_path_uq' AND parent_object_id = OBJECT_ID(N'{schema}.agent_files'))
ALTER TABLE {schema}.agent_files DROP CONSTRAINT agent_files_tenant_agent_path_uq;

ALTER TABLE {schema}.agent_files ALTER COLUMN agent_name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'agent_files_tenant_agent_path_uq' AND parent_object_id = OBJECT_ID(N'{schema}.agent_files'))
ALTER TABLE {schema}.agent_files ADD CONSTRAINT agent_files_tenant_agent_path_uq UNIQUE (tenant_id, agent_name, path);

-- ---------------------------------------------------------------------------
-- quotas
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'quotas_scope_uq' AND parent_object_id = OBJECT_ID(N'{schema}.quotas'))
ALTER TABLE {schema}.quotas DROP CONSTRAINT quotas_scope_uq;

ALTER TABLE {schema}.quotas ALTER COLUMN agent_name nvarchar(200) COLLATE Latin1_General_BIN2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'quotas_scope_uq' AND parent_object_id = OBJECT_ID(N'{schema}.quotas'))
ALTER TABLE {schema}.quotas ADD CONSTRAINT quotas_scope_uq UNIQUE (tenant_id, agent_name, period);

-- ---------------------------------------------------------------------------
-- quota_usage
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'quota_usage_pk' AND parent_object_id = OBJECT_ID(N'{schema}.quota_usage'))
ALTER TABLE {schema}.quota_usage DROP CONSTRAINT quota_usage_pk;

ALTER TABLE {schema}.quota_usage ALTER COLUMN agent_name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'quota_usage_pk' AND parent_object_id = OBJECT_ID(N'{schema}.quota_usage'))
ALTER TABLE {schema}.quota_usage ADD CONSTRAINT quota_usage_pk PRIMARY KEY (tenant_id, agent_name, period, period_start);

-- ---------------------------------------------------------------------------
-- tool_approval_rules
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'tool_approval_rules_scope_uq' AND parent_object_id = OBJECT_ID(N'{schema}.tool_approval_rules'))
ALTER TABLE {schema}.tool_approval_rules DROP CONSTRAINT tool_approval_rules_scope_uq;

ALTER TABLE {schema}.tool_approval_rules ALTER COLUMN agent_name nvarchar(200) COLLATE Latin1_General_BIN2 NULL;

ALTER TABLE {schema}.tool_approval_rules ALTER COLUMN tool_name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'tool_approval_rules_scope_uq' AND parent_object_id = OBJECT_ID(N'{schema}.tool_approval_rules'))
ALTER TABLE {schema}.tool_approval_rules ADD CONSTRAINT tool_approval_rules_scope_uq UNIQUE (tenant_id, agent_name, tool_name, arguments_hash, conditions_hash, source_fingerprint);

-- ---------------------------------------------------------------------------
-- audit_log
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.audit_log ALTER COLUMN entity nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

-- ---------------------------------------------------------------------------
-- runs
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_agent_version_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
DROP INDEX runs_agent_version_idx ON {schema}.runs;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_tenant_agent_started_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
DROP INDEX runs_tenant_agent_started_idx ON {schema}.runs;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_workflow_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
DROP INDEX runs_workflow_idx ON {schema}.runs;

ALTER TABLE {schema}.runs ALTER COLUMN agent_name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

ALTER TABLE {schema}.runs ALTER COLUMN workflow_name nvarchar(200) COLLATE Latin1_General_BIN2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_agent_version_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
CREATE INDEX runs_agent_version_idx ON {schema}.runs (tenant_id, agent_name, agent_version, started_at DESC)
    WHERE agent_version IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_tenant_agent_started_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
CREATE INDEX runs_tenant_agent_started_idx ON {schema}.runs (tenant_id, agent_name, started_at DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_workflow_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
CREATE INDEX runs_workflow_idx ON {schema}.runs (tenant_id, workflow_name, started_at DESC)
    WHERE kind = 1;

-- ---------------------------------------------------------------------------
-- sessions
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'sessions_tenant_agent_updated_idx' AND object_id = OBJECT_ID(N'{schema}.sessions'))
DROP INDEX sessions_tenant_agent_updated_idx ON {schema}.sessions;

ALTER TABLE {schema}.sessions ALTER COLUMN agent_name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'sessions_tenant_agent_updated_idx' AND object_id = OBJECT_ID(N'{schema}.sessions'))
CREATE INDEX sessions_tenant_agent_updated_idx ON {schema}.sessions (tenant_id, agent_name, updated_at DESC);

-- ---------------------------------------------------------------------------
-- conversations
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.conversations ALTER COLUMN agent_name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

-- ---------------------------------------------------------------------------
-- voice_sessions
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.voice_sessions ALTER COLUMN agent_name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

-- ---------------------------------------------------------------------------
-- workflow_requests
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.workflow_requests ALTER COLUMN workflow_name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

-- ---------------------------------------------------------------------------
-- jobs
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.jobs ALTER COLUMN target_name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

-- ---------------------------------------------------------------------------
-- pending_approvals
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.pending_approvals ALTER COLUMN tool_name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

-- ---------------------------------------------------------------------------
-- tool_invocations
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'tool_invocations_tool_created_idx' AND object_id = OBJECT_ID(N'{schema}.tool_invocations'))
DROP INDEX tool_invocations_tool_created_idx ON {schema}.tool_invocations;

ALTER TABLE {schema}.tool_invocations ALTER COLUMN tool_name nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'tool_invocations_tool_created_idx' AND object_id = OBJECT_ID(N'{schema}.tool_invocations'))
CREATE INDEX tool_invocations_tool_created_idx ON {schema}.tool_invocations (tool_name, created_at DESC);

-- ---------------------------------------------------------------------------
-- run_events
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.run_events ALTER COLUMN tool_name nvarchar(200) COLLATE Latin1_General_BIN2 NULL;
-- ---------------------------------------------------------------------------
-- runs_v1
--
-- A view that is not schema-bound keeps the collation its columns had when it was created, so after the
-- change above runs_v1.agent_name would still say the collation of the database. Refreshing it makes the
-- metadata that tools and drivers read tell the truth. A query that compares the column with a column of
-- another collation (a join on your own table) still needs COLLATE on one side; that is the same rule as
-- for the tables.
-- ---------------------------------------------------------------------------

IF OBJECT_ID(N'{schema}.runs_v1', N'V') IS NOT NULL
EXEC sp_refreshview N'{schema}.runs_v1';
