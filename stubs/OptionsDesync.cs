// STUB — contract only, see DESIGN.md §3
using AmongUs.GameOptions;
namespace PocketRoles.Net
{
    public static class OptionsDesync
    {
        public static void Capture() { }
        public static IGameOptions CloneBase() => null;
        public static IGameOptions BuildFor(byte playerId, float? killCooldownOverride = null) => null;
        public static bool NeedsCustomOptions(byte playerId) => false;
        public static void SendTo(PlayerControl pc, IGameOptions opts, bool urgent = false) { }
        public static void ResyncAll() { }
        public static void Reset() { }
    }
}
