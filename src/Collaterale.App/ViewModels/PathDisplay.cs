namespace Collaterale.App.ViewModels;

public static class PathDisplay
{
    /// <summary>Last two path segments ("v1/Cart.cs"): enough to tell similarly named files apart without a wall of path.</summary>
    public static string Short(string? path, string fallback = "")
    {
        if (string.IsNullOrEmpty(path))
        {
            return fallback;
        }

        var parts = path.Replace('\\', '/').TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[^2]}/{parts[^1]}" : path;
    }
}
