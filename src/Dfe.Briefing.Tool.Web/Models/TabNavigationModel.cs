namespace Dfe.Briefing.Tool.Web.Models;

public class TabNavigationModel(string currentTab)
{
    public const string ViewDataKey = "TabNavigationModel"; 
    public const string Main = "main";

    public string CurrentTab { get; } = currentTab;
}
