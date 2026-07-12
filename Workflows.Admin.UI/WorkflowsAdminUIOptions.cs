namespace Workflows.Admin.UI
{
    /// <summary>
    /// Configuration options for the Workflows Admin UI module.
    /// </summary>
    public class WorkflowsAdminUIOptions
    {
        /// <summary>
        /// Route prefix for the admin UI. Default is "admin".
        /// </summary>
        public string RoutePrefix { get; set; } = "admin";

        /// <summary>
        /// Default page size for list views. Default is 25.
        /// </summary>
        public int PageSize { get; set; } = 25;

        /// <summary>
        /// Maximum page size allowed. Default is 250.
        /// </summary>
        public int MaxPageSize { get; set; } = 250;

        /// <summary>
        /// If true, the admin UI will expose write actions (start, cancel, signal, compensate).
        /// Requires <see cref="IOrchestrator"/> to be registered in DI.
        /// Default is true.
        /// </summary>
        public bool EnableWriteActions { get; set; } = true;

        /// <summary>
        /// Database provider to use for the admin UI's own DbContext.
        /// Default is SQLite.
        /// </summary>
        public AdminDbProvider Provider { get; set; } = AdminDbProvider.Sqlite;
    }

    public enum AdminDbProvider
    {
        Sqlite,
        SqlServer,
        Postgres
    }
}