namespace Workflows.Handler.Abstraction.Serialization
{
    /// <summary>
    /// Abstraction for navigating object properties using path notation (e.g., "Order.RegionId")
    /// </summary>
    public interface IObjectNavigator
    {
        /// <summary>
        /// Gets a value from an object using a property path (e.g., "Order.RegionId")
        /// </summary>
        /// <param name="obj">The object to navigate</param>
        /// <param name="path">The property path (dot-separated)</param>
        /// <returns>The value as a string, or null if not found</returns>
        string GetValue(object obj, string path);
    }
}
