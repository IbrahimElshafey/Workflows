using System;
namespace Workflows.Handler.UiService.InOuts
{
    public class ServiceInfo
    {
        public int LogErrors { get; set; }
        public int WorkflowsCount { get; set; }
        public int MethodsCount { get; set; }
        public int SignalsCount { get; set; }
        public bool IsScanRunning { get; set; }
        public int Id { get; }
        public string Name { get; }
        public string Url { get; }
        public string[] Dlls { get; }
        public DateTime Registration { get; }
        public DateTime LastScan { get; }

        public ServiceInfo(int Id, string Name, string Url, string[] Dlls, DateTime Registration, DateTime LastScan)
        {
            this.Id = Id;
            this.Name = Name;
            this.Url = Url;
            this.Dlls = Dlls;
            this.Registration = Registration;
            this.LastScan = LastScan;
        }
    }
}
