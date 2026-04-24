namespace Workflows.MvcUi.DisplayObject
{
    public class HomePageModel
    {
        public MainMenuDisplay Menu { get; private set; }

        internal void SetMenu()
        {
            Menu = new MainMenuDisplay
            {
                Items = new[]
                    {
                        new MainMenuItem(
                            $"Services",
                            $"/RF/Home/{PartialNames.ServicesList}"),
                        new MainMenuItem(
                            $"Resumable Workflows",
                             $"/RF/Home/{PartialNames.Workflows}"),
                        new MainMenuItem(
                            $"Method Groups",
                            $"/RF/Home/{PartialNames.MethodGroups}"),
                        new MainMenuItem(
                            $"Pushed Calls",
                            $"/RF/Home/{PartialNames.Signals}"),
                        new MainMenuItem(
                            "Logs",
                            $"/RF/Home/{PartialNames.LatestLogs}"),
                }
            };
        }
    }
}