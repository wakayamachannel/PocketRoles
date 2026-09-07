// STUB — contract only, see DESIGN.md §9
namespace PocketRoles.Chat
{
    public static class Chat
    {
        public const string Title = "PocketRoles";
        public static void Local(string title, string text) { }
        public static void To(byte playerId, string title, string text) { }
        public static void All(string title, string text) { }
        public static void SendRoleInfo(byte playerId, bool meeting) { }
        public static void SendRoleInfoToAll(bool meeting) { }
        public static void Welcome(int clientId) { }
        public static void SendSummary() { }
    }
    public static class Commands
    {
        public static bool Handle(PlayerControl sender, string text) => false;
    }
}
