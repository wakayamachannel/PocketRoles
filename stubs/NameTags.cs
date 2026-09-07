// STUB — contract only, see DESIGN.md §5
namespace PocketRoles.Game
{
    public static class NameTags
    {
        public static string NameFor(byte viewerId, byte targetId, bool meeting = false) => "";
        public static void RefreshAll(bool force = false, bool meeting = false) { }
        public static void RestoreAll() { }
        public static void ApplyLocal(byte targetId, string name) { }
        public static void ClearCache() { }
    }
}
