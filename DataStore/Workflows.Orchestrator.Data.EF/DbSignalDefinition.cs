namespace Workflows.Orchestrator.Data.EF
{
    public class DbSignalDefinition
    {
        public string SignalIdentifier { get; set; }
        public string PayloadTypeName { get; set; }
        public string PayloadSchema { get; set; }
    }
}
