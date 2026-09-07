// STUB — contract only, see DESIGN.md §7
using System.Collections.Generic;
namespace PocketRoles.Game
{
    public static class Meetings { }
    public static class AntiBlackout
    {
        public static bool Active;
        public static Dictionary<byte, bool> RealIsDead = new Dictionary<byte, bool>();
        public static void Prepare(byte exiledId) { }
        public static void Restore() { }
    }
}
