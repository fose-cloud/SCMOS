BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907092459_AiExecutionAudit'
)
BEGIN
    CREATE TABLE [ai_audit_logs] (
        [id] bigint NOT NULL IDENTITY,
        [run_id] nvarchar(32) NOT NULL,
        [sequence] int NOT NULL,
        [user_id] nvarchar(160) NOT NULL,
        [user_role] nvarchar(60) NOT NULL,
        [agent_id] nvarchar(40) NOT NULL,
        [event] nvarchar(20) NOT NULL,
        [status] nvarchar(32) NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        [team_scope] bit NOT NULL,
        [operator_id] nvarchar(20) NULL,
        [tool_name] nvarchar(60) NULL,
        [tool_call_id] nvarchar(32) NULL,
        [model] nvarchar(100) NOT NULL,
        [view] nvarchar(20) NULL,
        [result_limit] int NULL,
        [total] int NULL,
        [returned] int NULL,
        [input_tokens] int NULL,
        [output_tokens] int NULL,
        [source_keys] nvarchar(max) NOT NULL,
        [risk] nvarchar(12) NOT NULL,
        [approval_status] nvarchar(20) NOT NULL,
        [source] nvarchar(40) NOT NULL,
        [fingerprint] nvarchar(64) NOT NULL,
        CONSTRAINT [PK_ai_audit_logs] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907092459_AiExecutionAudit'
)
BEGIN
    CREATE INDEX [ai_audit_logs_at_idx] ON [ai_audit_logs] ([created_at], [id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907092459_AiExecutionAudit'
)
BEGIN
    CREATE UNIQUE INDEX [ai_audit_logs_run_sequence_idx] ON [ai_audit_logs] ([run_id], [sequence]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907092459_AiExecutionAudit'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260907092459_AiExecutionAudit', N'10.0.11');
END;

COMMIT;
GO

