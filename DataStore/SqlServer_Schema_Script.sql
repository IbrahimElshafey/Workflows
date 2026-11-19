CREATE TABLE [CommandDefinitions] (
    [CommandName] nvarchar(450) NOT NULL,
    [PayloadTypeName] nvarchar(max) NOT NULL,
    [PayloadSchema] nvarchar(max) NOT NULL,
    [ResultTypeName] nvarchar(max) NOT NULL,
    [ResultSchema] nvarchar(max) NOT NULL,
    [DefaultTimeout] time NOT NULL,
    [ExecutionMode] int NOT NULL,
    [Created] datetime2 NOT NULL,
    CONSTRAINT [PK_CommandDefinitions] PRIMARY KEY ([CommandName])
);
GO


CREATE TABLE [CommandResults] (
    [Id] int NOT NULL IDENTITY,
    [GlobalId] uniqueidentifier NOT NULL,
    [ReceivedAt] datetime2 NOT NULL,
    [ProcessedAt] datetime2 NULL,
    [Status] int NOT NULL,
    [CommandWaitId] nvarchar(450) NOT NULL,
    [ResultJson] nvarchar(max) NOT NULL,
    [IsSuccess] bit NOT NULL,
    CONSTRAINT [PK_CommandResults] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [OutboxMessages] (
    [Id] int NOT NULL IDENTITY,
    [GlobalId] uniqueidentifier NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [ProcessedAt] datetime2 NULL,
    [Status] int NOT NULL,
    [MessageType] nvarchar(max) NOT NULL,
    [Payload] nvarchar(max) NOT NULL,
    [WorkflowInstanceId] uniqueidentifier NOT NULL,
    [CommandWaitId] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_OutboxMessages] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [SignalDefinitions] (
    [SignalIdentifier] nvarchar(450) NOT NULL,
    [PayloadTypeName] nvarchar(max) NOT NULL,
    [PayloadSchema] nvarchar(max) NOT NULL,
    [Created] datetime2 NOT NULL,
    CONSTRAINT [PK_SignalDefinitions] PRIMARY KEY ([SignalIdentifier])
);
GO


CREATE TABLE [SignalInbox] (
    [MessageId] nvarchar(450) NOT NULL,
    [WorkflowInstanceId] uniqueidentifier NOT NULL,
    [ProcessedAt] datetime2 NOT NULL,
    [PayloadHash] nvarchar(max) NOT NULL,
    [Created] datetime2 NOT NULL,
    CONSTRAINT [PK_SignalInbox] PRIMARY KEY ([MessageId])
);
GO


CREATE TABLE [TemplateCache] (
    [TemplateHashKey] nvarchar(450) NOT NULL,
    [SignalExactMatchPathsJson] nvarchar(max) NOT NULL,
    [IsExactMatchFullMatch] bit NOT NULL,
    [IsGenericMatchFullMatch] bit NOT NULL,
    [GenericMatchExpressionJson] nvarchar(max) NULL,
    [InstanceExactMatchExpressionJson] nvarchar(max) NULL,
    [NormalizedMatchExpressionJson] nvarchar(max) NULL,
    [AfterMatchAction] nvarchar(max) NULL,
    [CancelAction] nvarchar(max) NULL,
    [Created] datetime2 NOT NULL,
    CONSTRAINT [PK_TemplateCache] PRIMARY KEY ([TemplateHashKey])
);
GO


CREATE TABLE [WorkerCapabilities] (
    [Id] uniqueidentifier NOT NULL,
    [DllVersion] nvarchar(450) NOT NULL,
    [WorkflowType] nvarchar(450) NOT NULL,
    [WorkflowVersion] int NOT NULL,
    [Created] datetime2 NOT NULL,
    [RegisteredAt] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_WorkerCapabilities] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [WorkflowDefinitions] (
    [WorkflowName] nvarchar(450) NOT NULL,
    [Version] int NOT NULL,
    [WorkflowTypeName] nvarchar(max) NOT NULL,
    [WorkflowTypeSchema] nvarchar(max) NOT NULL,
    [RegisteredAt] datetime2 NOT NULL,
    [Created] datetime2 NOT NULL,
    CONSTRAINT [PK_WorkflowDefinitions] PRIMARY KEY ([WorkflowName], [Version])
);
GO


CREATE TABLE [WorkflowInstances] (
    [Id] uniqueidentifier NOT NULL,
    [Created] datetime2 NOT NULL,
    [Modified] datetime2 NULL,
    [ConcurrencyToken] nvarchar(max) NOT NULL,
    [Status] int NOT NULL,
    [WorkflowType] nvarchar(450) NOT NULL,
    [WorkflowVersion] int NOT NULL,
    [LockedBy] nvarchar(max) NULL,
    [LockedAt] datetime2 NULL,
    [LockExpiresAt] datetime2 NULL,
    [CompletedAt] datetime2 NULL,
    [ActiveWaitCount] int NOT NULL,
    [ErrorMessage] nvarchar(max) NULL,
    [LastAdvanceReason] nvarchar(max) NULL,
    [StateObject] nvarchar(max) NOT NULL,
    [CancellationHistory] nvarchar(max) NOT NULL,
    [Waits] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_WorkflowInstances] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [WorkflowWaitEntity] (
    [Id] nvarchar(450) NOT NULL,
    [WorkflowInstanceId] uniqueidentifier NOT NULL,
    [Status] int NOT NULL,
    [CancelTokens] nvarchar(max) NULL,
    [Created] datetime2 NOT NULL,
    CONSTRAINT [PK_WorkflowWaitEntity] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [CommandWaits] (
    [Id] nvarchar(450) NOT NULL,
    [WorkflowInstanceId] uniqueidentifier NOT NULL,
    [Status] int NOT NULL,
    [CancelTokens] nvarchar(max) NULL,
    [Created] datetime2 NOT NULL,
    [CommandWaitId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_CommandWaits] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CommandWaits_WorkflowInstances_WorkflowInstanceId] FOREIGN KEY ([WorkflowInstanceId]) REFERENCES [WorkflowInstances] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [CompensationWaits] (
    [Id] nvarchar(450) NOT NULL,
    [WorkflowInstanceId] uniqueidentifier NOT NULL,
    [Status] int NOT NULL,
    [CancelTokens] nvarchar(max) NULL,
    [Created] datetime2 NOT NULL,
    [Token] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_CompensationWaits] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CompensationWaits_WorkflowInstances_WorkflowInstanceId] FOREIGN KEY ([WorkflowInstanceId]) REFERENCES [WorkflowInstances] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [ExternalChildWaits] (
    [Id] nvarchar(450) NOT NULL,
    [WorkflowInstanceId] uniqueidentifier NOT NULL,
    [ParentWaitId] nvarchar(450) NOT NULL,
    [ChildWaitType] int NOT NULL,
    [Status] int NOT NULL,
    [SignalPath] nvarchar(450) NULL,
    [SignalExactMatchPaths] nvarchar(450) NULL,
    [ExactMatchFilter] nvarchar(450) NULL,
    [IsFirstWait] bit NOT NULL,
    [TemplateHashKey] nvarchar(max) NULL,
    [UniqueMatchId] nvarchar(450) NULL,
    [ExecutionTime] datetime2 NULL,
    [CommandWaitId] nvarchar(450) NULL,
    [Token] nvarchar(max) NULL,
    [CancelTokens] nvarchar(max) NULL,
    [Created] datetime2 NOT NULL,
    CONSTRAINT [PK_ExternalChildWaits] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ExternalChildWaits_WorkflowInstances_WorkflowInstanceId] FOREIGN KEY ([WorkflowInstanceId]) REFERENCES [WorkflowInstances] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [SignalWaits] (
    [Id] nvarchar(450) NOT NULL,
    [WorkflowInstanceId] uniqueidentifier NOT NULL,
    [Status] int NOT NULL,
    [CancelTokens] nvarchar(max) NULL,
    [Created] datetime2 NOT NULL,
    [SignalPath] nvarchar(450) NOT NULL,
    [SignalExactMatchPaths] nvarchar(450) NOT NULL,
    [ExactMatchFilter] nvarchar(450) NOT NULL,
    [IsFirstWait] bit NOT NULL,
    [TemplateHashKey] nvarchar(max) NULL,
    CONSTRAINT [PK_SignalWaits] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SignalWaits_WorkflowInstances_WorkflowInstanceId] FOREIGN KEY ([WorkflowInstanceId]) REFERENCES [WorkflowInstances] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [TimeWaits] (
    [Id] nvarchar(450) NOT NULL,
    [WorkflowInstanceId] uniqueidentifier NOT NULL,
    [Status] int NOT NULL,
    [CancelTokens] nvarchar(max) NULL,
    [Created] datetime2 NOT NULL,
    [ExecutionTime] datetime2 NOT NULL,
    [UniqueMatchId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_TimeWaits] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_TimeWaits_WorkflowInstances_WorkflowInstanceId] FOREIGN KEY ([WorkflowInstanceId]) REFERENCES [WorkflowInstances] ([Id]) ON DELETE CASCADE
);
GO


CREATE UNIQUE INDEX [IX_CommandResults_CommandWaitId] ON [CommandResults] ([CommandWaitId]);
GO


CREATE UNIQUE INDEX [IX_CommandResults_GlobalId] ON [CommandResults] ([GlobalId]);
GO


CREATE INDEX [IX_CommandResults_Status_ReceivedAt] ON [CommandResults] ([Status], [ReceivedAt]);
GO


CREATE INDEX [IX_CommandWaits_CommandWaitId] ON [CommandWaits] ([CommandWaitId]);
GO


CREATE INDEX [IX_CommandWaits_WorkflowInstanceId] ON [CommandWaits] ([WorkflowInstanceId]);
GO


CREATE INDEX [IX_CompensationWaits_Token] ON [CompensationWaits] ([Token]);
GO


CREATE INDEX [IX_CompensationWaits_WorkflowInstanceId] ON [CompensationWaits] ([WorkflowInstanceId]);
GO


CREATE INDEX [IX_ExternalChildWaits_CommandWaitId] ON [ExternalChildWaits] ([CommandWaitId]);
GO


CREATE INDEX [IX_ExternalChildWaits_ParentWaitId] ON [ExternalChildWaits] ([ParentWaitId]);
GO


CREATE INDEX [IX_ExternalChildWaits_SignalPath_Status_SignalExactMatchPaths_ExactMatchFilter] ON [ExternalChildWaits] ([SignalPath], [Status], [SignalExactMatchPaths], [ExactMatchFilter]);
GO


CREATE INDEX [IX_ExternalChildWaits_UniqueMatchId] ON [ExternalChildWaits] ([UniqueMatchId]);
GO


CREATE INDEX [IX_ExternalChildWaits_WorkflowInstanceId] ON [ExternalChildWaits] ([WorkflowInstanceId]);
GO


CREATE UNIQUE INDEX [IX_OutboxMessages_GlobalId] ON [OutboxMessages] ([GlobalId]);
GO


CREATE INDEX [IX_OutboxMessages_Status_CreatedAt] ON [OutboxMessages] ([Status], [CreatedAt]);
GO


CREATE INDEX [IX_SignalInbox_WorkflowInstanceId] ON [SignalInbox] ([WorkflowInstanceId]);
GO


CREATE INDEX [IX_SignalWaits_SignalPath_Status_SignalExactMatchPaths_ExactMatchFilter] ON [SignalWaits] ([SignalPath], [Status], [SignalExactMatchPaths], [ExactMatchFilter]);
GO


CREATE INDEX [IX_SignalWaits_WorkflowInstanceId] ON [SignalWaits] ([WorkflowInstanceId]);
GO


CREATE INDEX [IX_TimeWaits_Status_ExecutionTime] ON [TimeWaits] ([Status], [ExecutionTime]);
GO


CREATE INDEX [IX_TimeWaits_UniqueMatchId] ON [TimeWaits] ([UniqueMatchId]);
GO


CREATE INDEX [IX_TimeWaits_WorkflowInstanceId] ON [TimeWaits] ([WorkflowInstanceId]);
GO


CREATE INDEX [IX_WorkerCapabilities_DllVersion_IsActive] ON [WorkerCapabilities] ([DllVersion], [IsActive]);
GO


CREATE INDEX [IX_WorkerCapabilities_WorkflowType_WorkflowVersion_IsActive] ON [WorkerCapabilities] ([WorkflowType], [WorkflowVersion], [IsActive]);
GO


CREATE INDEX [IX_WorkflowInstances_CompletedAt] ON [WorkflowInstances] ([CompletedAt]);
GO


CREATE INDEX [IX_WorkflowInstances_Created] ON [WorkflowInstances] ([Created]);
GO


CREATE INDEX [IX_WorkflowInstances_Status] ON [WorkflowInstances] ([Status]);
GO


CREATE INDEX [IX_WorkflowInstances_WorkflowType_Status] ON [WorkflowInstances] ([WorkflowType], [Status]);
GO


