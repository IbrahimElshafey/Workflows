-- =============================================================================
-- Migration: AddFailureCountAndDlqMessages
-- Description: Adds FailureCount column to WorkflowInstances and creates the
--              DlqMessages table for outbox-style Dead Letter Queue storage.
-- Apply to: SQLite, SQL Server, PostgreSQL (syntax is ANSI-compatible)
-- =============================================================================

-- 1. Add FailureCount to WorkflowInstances
--    Default 0 so existing rows start with a clean slate.
ALTER TABLE "WorkflowInstances"
    ADD COLUMN "FailureCount" INTEGER NOT NULL DEFAULT 0;

-- 2. Create DlqMessages table
CREATE TABLE IF NOT EXISTS "DlqMessages" (
    "Id"                 INTEGER      NOT NULL PRIMARY KEY AUTOINCREMENT,  -- SQLite
    -- For SQL Server use: BIGINT IDENTITY(1,1) PRIMARY KEY
    -- For PostgreSQL use: BIGSERIAL PRIMARY KEY
    "WorkflowInstanceId" TEXT         NOT NULL,
    "WorkflowType"       TEXT         NOT NULL DEFAULT '',
    "FailureCount"       INTEGER      NOT NULL DEFAULT 0,
    "PayloadJson"        TEXT         NULL,
    "ErrorMessage"       TEXT         NULL,
    "CreatedAt"          TEXT         NOT NULL,   -- stored as UTC ISO-8601
    "Status"             INTEGER      NOT NULL DEFAULT 0,  -- 0=Pending, 1=Forwarded
    FOREIGN KEY ("WorkflowInstanceId") REFERENCES "WorkflowInstances" ("Id") ON DELETE CASCADE
);

-- 3. Indexes for sweeper polling and admin queries
CREATE INDEX IF NOT EXISTS "IX_DlqMessages_Status_CreatedAt"
    ON "DlqMessages" ("Status", "CreatedAt");

CREATE INDEX IF NOT EXISTS "IX_DlqMessages_WorkflowInstanceId"
    ON "DlqMessages" ("WorkflowInstanceId");

-- =============================================================================
-- SQL Server variant (replace the SQLite PRIMARY KEY line above):
--
-- CREATE TABLE [DlqMessages] (
--     [Id]                 BIGINT        NOT NULL IDENTITY(1,1) PRIMARY KEY,
--     [WorkflowInstanceId] UNIQUEIDENTIFIER NOT NULL,
--     [WorkflowType]       NVARCHAR(MAX) NOT NULL DEFAULT '',
--     [FailureCount]       INT           NOT NULL DEFAULT 0,
--     [PayloadJson]        NVARCHAR(MAX) NULL,
--     [ErrorMessage]       NVARCHAR(MAX) NULL,
--     [CreatedAt]          DATETIME2     NOT NULL,
--     [Status]             INT           NOT NULL DEFAULT 0,
--     CONSTRAINT FK_DlqMessages_WorkflowInstances
--         FOREIGN KEY ([WorkflowInstanceId]) REFERENCES [WorkflowInstances]([Id]) ON DELETE CASCADE
-- );
--
-- PostgreSQL variant:
--
-- CREATE TABLE "DlqMessages" (
--     "Id"                 BIGSERIAL     PRIMARY KEY,
--     "WorkflowInstanceId" UUID          NOT NULL,
--     "WorkflowType"       TEXT          NOT NULL DEFAULT '',
--     "FailureCount"       INT           NOT NULL DEFAULT 0,
--     "PayloadJson"        TEXT          NULL,
--     "ErrorMessage"       TEXT          NULL,
--     "CreatedAt"          TIMESTAMP     NOT NULL,
--     "Status"             INT           NOT NULL DEFAULT 0,
--     CONSTRAINT fk_dlq_instance
--         FOREIGN KEY ("WorkflowInstanceId") REFERENCES "WorkflowInstances"("Id") ON DELETE CASCADE
-- );
-- =============================================================================
