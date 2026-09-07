using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using PocketRoles.Core;

namespace PocketRoles.Cosmetics
{
    /// <summary>
    /// Facade for the host-only cosmetics (v0.3): folder layout under BepInEx/PocketRoles, id listing for /cos ids, cache
    /// reload and the lobby-music mode switch. Nothing here is transmitted; every other player keeps seeing vanilla.
    /// Other cosmetics modules subscribe to <see cref="OnReload"/> to re-apply their overrides after /cos reload.
    /// </summary>
    public static class Cosmetics
    {
        /// <summary>Raised by <see cref="Reload"/> and <see cref="SetMusicMode"/> after the sprite cache was cleared.</summary>
        public static event Action OnReload;

        private static string _rootDir;
        private static bool _foldersEnsured;

        /// <summary>BepInEx/PocketRoles (absolute). Falls back to "&lt;cwd&gt;/BepInEx/PocketRoles" when BepInEx paths are unavailable.</summary>
        public static string RootDir
        {
            get
            {
                if (_rootDir != null) return _rootDir;
                string root = null;
                try { root = BepInEx.Paths.BepInExRootPath; } catch (Exception) { }
                if (string.IsNullOrEmpty(root))
                {
                    try { root = Path.Combine(Directory.GetCurrentDirectory(), "BepInEx"); } catch (Exception) { root = "BepInEx"; }
                }
                _rootDir = Path.Combine(root, "PocketRoles");
                return _rootDir;
            }
        }

        public static string HatsDir => Path.Combine(RootDir, "hats");
        public static string VisorsDir => Path.Combine(RootDir, "visors");
        public static string NamePlatesDir => Path.Combine(RootDir, "nameplates");
        public static string MusicDir => Path.Combine(RootDir, "music");
        public static string ImagesDir => Path.Combine(RootDir, "images");

        /// <summary>
        /// Absolute path of "&lt;folder&gt;/&lt;id&gt;&lt;suffix&gt;.png", or null when <paramref name="id"/> is not a plain file name
        /// (ids come from other players' outfits over the network, so path separators / traversal are rejected).
        /// </summary>
        public static string FileFor(string folder, string id, string suffix = "")
        {
            if (string.IsNullOrEmpty(folder) || !IsSafeId(id)) return null;
            return Path.Combine(folder, id + (suffix ?? "") + ".png");
        }

        /// <summary>True when <paramref name="id"/> can be used as a file name (no separators, no traversal, no invalid chars).</summary>
        public static bool IsSafeId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 128) return false;
            if (id == "." || id == ".." || id.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
            foreach (char c in id)
            {
                if (c == '/' || c == '\\' || c == ':' || c < ' ') return false;
                if (Array.IndexOf(InvalidChars, c) >= 0) return false;
            }
            return true;
        }

        private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

        /// <summary>
        /// Creates hats/visors/nameplates/music/images under <see cref="RootDir"/> and (re)writes README.txt (Japanese) explaining
        /// the file naming and /cos ids. Safe to call repeatedly; never throws.
        /// </summary>
        public static void EnsureFolders()
        {
            try
            {
                string root = RootDir;
                Directory.CreateDirectory(root);
                foreach (var dir in new[] { HatsDir, VisorsDir, NamePlatesDir, MusicDir, ImagesDir })
                    Directory.CreateDirectory(dir);
                string readme = Path.Combine(root, "README.txt");
                string text = ReadmeText();
                bool write = true;
                try { if (File.Exists(readme) && File.ReadAllText(readme, Encoding.UTF8) == text) write = false; } catch (Exception) { }
                if (write) File.WriteAllText(readme, text, new UTF8Encoding(true));
                if (!_foldersEnsured) PocketRolesPlugin.Logger.LogInfo($"Cosmetics: folders ready under {root}");
                _foldersEnsured = true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Cosmetics.EnsureFolders failed: {e.Message}");
            }
        }

        /// <summary>
        /// One line per connected player: "name: hat=… visor=… plate=… skin=… pet=…" (DefaultOutfit ProductIds). Also written
        /// to the log. Empty when no players exist yet. Intended for /cos ids (chat module chunks the lines).
        /// </summary>
        public static List<string> ListIds()
        {
            var lines = new List<string>();
            try
            {
                var all = PlayerControl.AllPlayerControls;
                if (all == null) return lines;
                foreach (var pc in all)
                {
                    try
                    {
                        if (pc == null || pc.Data == null) continue;
                        var outfit = pc.Data.DefaultOutfit;
                        if (outfit == null) continue;
                        string name = outfit.PlayerName;
                        if (string.IsNullOrEmpty(name)) name = pc.Data.PlayerName;
                        if (string.IsNullOrEmpty(name)) name = "#" + pc.PlayerId;
                        lines.Add($"{name}: hat={Idz(outfit.HatId)} visor={Idz(outfit.VisorId)} plate={Idz(outfit.NamePlateId)} skin={Idz(outfit.SkinId)} pet={Idz(outfit.PetId)}");
                    }
                    catch (Exception e)
                    {
                        PocketRolesPlugin.Logger.LogWarning($"Cosmetics.ListIds: player skipped: {e.Message}");
                    }
                }
                foreach (var line in lines) PocketRolesPlugin.Logger.LogInfo("Cosmetics ids: " + line);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Cosmetics.ListIds: {e}");
            }
            return lines;
        }

        private static string Idz(string id) => string.IsNullOrEmpty(id) ? "-" : id;

        /// <summary>
        /// Clears the sprite cache, restores the vanilla sprites on every mutated view data (so the identity guards
        /// re-apply from disk), re-reads the lobby / menu images and asks every other cosmetics module (via
        /// <see cref="OnReload"/>, e.g. the lobby music) to re-apply.
        /// </summary>
        public static void Reload()
        {
            try
            {
                EnsureFolders();
                SpriteLoader.Clear();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Cosmetics.Reload: {e}");
            }
            // Integration (v0.3): the sprite and decor modules do not subscribe to OnReload themselves.
            try { CosmeticOverrides.Reset(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Cosmetics.Reload (CosmeticOverrides.Reset): {e}"); }
            try { LobbyDecor.Reload(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Cosmetics.Reload (LobbyDecor.Reload): {e}"); }
            RaiseReload();
        }

        /// <summary>
        /// Sets [Cosmetics] LobbyMusic to "custom" | "vanilla" | "mute" (case-insensitive) and raises <see cref="OnReload"/>.
        /// Returns false (and changes nothing) for any other value.
        /// </summary>
        public static bool SetMusicMode(string mode)
        {
            string m = (mode ?? "").Trim().ToLowerInvariant();
            string chosen = null;
            foreach (var c in Options.LobbyMusicChoices)
            {
                if (c == m) { chosen = c; break; }
            }
            if (chosen == null) return false;
            try
            {
                Options.LobbyMusic = chosen;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Cosmetics.SetMusicMode({mode}): {e}");
                return false;
            }
            RaiseReload();
            return true;
        }

        private static void RaiseReload()
        {
            var handlers = OnReload;
            if (handlers == null) return;
            foreach (var d in handlers.GetInvocationList())
            {
                try { ((Action)d)(); }
                catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Cosmetics.OnReload handler {d.Method.DeclaringType?.Name}.{d.Method.Name}: {e}"); }
            }
        }

        private static string ReadmeText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("PocketRoles 見た目カスタマイズ（ホストの画面だけに表示されます）");
            sb.AppendLine("================================================================");
            sb.AppendLine();
            sb.AppendLine("このフォルダに画像や音楽を置くと、ホスト（あなた）の画面だけ見た目が変わります。");
            sb.AppendLine("他の参加者には何も送信されず、参加者側はバニラのままです。");
            sb.AppendLine("設定は BepInEx/config の [Cosmetics] セクション、またはゲーム内の設定タブ「見た目（ホストのみ）」で変更できます。");
            sb.AppendLine();
            sb.AppendLine("■ フォルダ構成");
            sb.AppendLine("  hats\\<ProductId>.png         帽子のメイン画像");
            sb.AppendLine("     省略可: <ProductId>_back.png / _left.png / _left_back.png / _climb.png / _floor.png");
            sb.AppendLine("  visors\\<ProductId>.png       バイザーのメイン画像（省略可: _left.png / _climb.png / _floor.png）");
            sb.AppendLine("  nameplates\\<ProductId>.png   会議画面のネームプレート");
            sb.AppendLine("  music\\*.wav | *.ogg           ロビーBGM（最初に見つかったファイル、または LobbyMusicFile で指定したもの。MP3 は不可）");
            sb.AppendLine("  images\\lobbypaint.png        ロビーの壁の絵（省略可）");
            sb.AppendLine("  images\\dropship.png          ドロップシップの飾り（省略可）");
            sb.AppendLine("  images\\menu.png              メインメニューの背景（省略可）");
            sb.AppendLine("  images\\cursor.png            マウスカーソル（省略可、64x64 以下推奨）");
            sb.AppendLine();
            sb.AppendLine("■ ProductId（ファイル名）の調べ方");
            sb.AppendLine("  ロビーでホストとしてチャットに /cos ids と入力すると、参加者全員の");
            sb.AppendLine("  帽子・バイザー・ネームプレート・スキン・ペットの ProductId が表示されます（ログにも出ます）。");
            sb.AppendLine("  例: hat_pk05_Cheese → hats\\hat_pk05_Cheese.png");
            sb.AppendLine("      visor_Cat       → visors\\visor_Cat.png");
            sb.AppendLine("      nameplate_Bavarian → nameplates\\nameplate_Bavarian.png");
            sb.AppendLine("  スキンとペットはアニメーションのため差し替え対象外です（ID の確認だけできます）。");
            sb.AppendLine();
            sb.AppendLine("■ 画像のコツ");
            sb.AppendLine("  ・元の画像と同じピクセルサイズで描くと、位置がそのまま一致します。");
            sb.AppendLine("  ・プレイヤー色に合わせて変わる帽子は、純粋な赤 (255,0,0) が本体色、緑 (0,255,0) が影、青 (0,0,255) がバイザー色に置き換わります。");
            sb.AppendLine("  ・PNG（透過あり）を推奨します。");
            sb.AppendLine();
            sb.AppendLine("■ チャットコマンド（ホストのみ）");
            sb.AppendLine("  /cos ids                       参加者全員の ProductId を表示");
            sb.AppendLine("  /cos reload                    画像・音楽を読み直して再適用");
            sb.AppendLine("  /cos music custom|vanilla|mute ロビーBGMの切り替え");
            sb.AppendLine("  音量は設定 LobbyMusicVolume（0〜1、バニラのテーマは約 0.07）で調整できます。");
            sb.AppendLine();
            sb.AppendLine("このファイルは PocketRoles が起動時に書き出します（編集しても次回上書きされます）。");
            return sb.ToString();
        }
    }

    /// <summary>Creates the cosmetics folders (and README.txt) once the main menu is up; runs on every client, local files only.</summary>
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
    internal static class Cosmetics_EnsureFoldersPatch
    {
        private static bool _done;

        private static void Postfix()
        {
            try
            {
                if (_done) return;
                _done = true;
                Cosmetics.EnsureFolders();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Cosmetics_EnsureFoldersPatch: {e}");
            }
        }
    }
}
