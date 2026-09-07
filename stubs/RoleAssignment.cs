// STUB — contract only, see DESIGN.md §4
using AmongUs.GameOptions;
namespace PocketRoles.Game
{
    public static class RoleAssignment
    {
        public static RoleTypes View(byte viewerId, byte targetId) => RoleTypes.Crewmate;
        public static void DispatchInitialRoles() { }
        public static void SendGhostRole(PlayerControl dead) { }
    }
}
