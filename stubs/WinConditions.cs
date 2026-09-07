// STUB — contract only, see DESIGN.md §8
namespace PocketRoles.Game
{
    public static class WinConditions
    {
        public enum WinKind { Crew, Impostor, Jackal, Jester, Terrorist }
        public static void Check() { }
        public static bool WouldContinue(byte exiledId) => true;
        public static void EndGame(WinKind kind, byte soloId = 255) { }
    }
}
