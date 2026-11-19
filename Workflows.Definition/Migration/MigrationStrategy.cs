namespace Workflows.Definition
{
    /// <summary>
    /// Defines the execution strategy for version migration.
    /// </summary>
    public enum MigrationStrategy
    {
        InPlace,
        CancelAndRespawn
    }
}
