namespace Dfe.Briefing.Tool.Web.Models
{
    public static class Links
    {
        private static readonly List<LinkItem> _links = [];

        private static LinkItem AddLinkItem(string page, string backText = "Back")
        {
            LinkItem item = new() { Page = page, BackText = backText };
            _links.Add(item);
            return item;
        }

        public static class Public
        {
            public static readonly LinkItem Accessibility = AddLinkItem(page: "/Public/AccessibilityStatement");
            public static readonly LinkItem CookiePreferences = AddLinkItem(page: "/Public/Cookies");
            public static readonly LinkItem Privacy = AddLinkItem(page: "/Public/Privacy");
        }
    }

    public class LinkItem
    {
        public string Page { get; set; }
        public string BackText { get; set; } = "Back";
    }
}
