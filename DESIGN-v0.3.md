# HostRoles v0.3 — host-only cosmetics (implementation contract)

Everything in this version is **local to the host's screen**: no RPC, no NetworkedPlayerInfo changes, nothing transmitted. Vanilla
players keep seeing vanilla cosmetics. Research (all members verified against the 2026.8.18 dump unless marked inferred):
`%SCRATCH%/research/v03-4.txt` (hats / visors / nameplates / skins) and `%SCRATCH%/research/v03-3.txt` (lobby music, lobby
paint, main-menu background, cursor). Read the relevant file in full before coding. Rules of `DESIGN.md §0` apply.

## Folder layout (created at startup if missing)
```
<game>\BepInEx\HostRoles\
  hats\<ProductId>.png            main image; optional <ProductId>_back.png, _left.png, _left_back.png, _climb.png, _floor.png
  visors\<ProductId>.png          optional _left.png, _climb.png, _floor.png
  nameplates\<ProductId>.png
  music\*.wav | *.ogg             lobby BGM (first file, or the configured one)
  images\lobbypaint.png           lobby wall paint (optional)
  images\dropship.png             dropship decoration (optional)
  images\menu.png                 main-menu background (optional)
  images\cursor.png               mouse cursor (optional, ≤ 64×64 recommended)
  README.txt                      (written by the mod, Japanese) how to name files, how to find ids (/cos ids)
```
`ProductId` = `CosmeticData.ProductId` (e.g. `hat_pk05_Cheese`, `visor_Cat`, `nameplate_Bavarian`).

## Config `[Cosmetics]` (owner: cosmetics-core)
`Enabled=true` (`cos.enabled`), `LobbyMusic=custom|vanilla|mute` (`cos.music`, default `custom` = use file when present else vanilla),
`LobbyMusicFile=""` (`cos.musicfile`), `LobbyMusicVolume=0.07` (`cos.musicvolume`, 0..1; vanilla theme ≈ 0.07 per SNR),
`LobbyPaint=true`, `Dropship=true`, `MenuBackground=true`, `Cursor=true`. Commands: `/cos ids` (list every player's hat/visor/
nameplate/skin/pet ProductIds to the host chat + log), `/cos reload` (clear sprite/music caches and re-apply), `/cos music vanilla|mute|custom`.
Add these keys to `Options.Descriptors` (settings tab section "見た目（ホストのみ）").

## Modules (owner per file)
### `src/Cosmetics/SpriteLoader.cs` (owner: cosmetics-core)
`static Sprite Load(string absPath, Sprite template)` per research recipe (1): `Texture2D(2,2,TextureFormat.ARGB32,false)`,
`ImageConversion.LoadImage(tex, (Il2CppStructArray<byte>)bytes, false)`, pivot/PPU from `template` when its rect size equals the
PNG size, else `(0.53f,0.575f)` / `tex.width*0.375f` (hats/visors) or `(0.5f,0.5f)` / template PPU (plates/images); hideFlags
`HideAndDontSave | DontUnloadUnusedAsset` on texture and sprite; positive and negative caches keyed by path; `Clear()`.
Also `static Sprite LoadImage(string absPath, float ppu)` for decor images (pivot 0.5,0.5).

### `src/Cosmetics/CosmeticOverrides.cs` (owner: cosmetics-sprites)
Recipe (2)/(3) of v03-4: mutate the loaded view data, never the renderers.
* `HatParent.PopulateFromViewData` prefix + `HatParent.LateUpdate` prefix → `ApplyHat(hp)` (identity-guarded: return when `vd.MainImage` already is our sprite).
* `VisorLayer.PopulateFromViewData`, `SetFlipX`, `SetIdleAnim`, `SetClimbAnim`, `SetFloorAnim` prefixes → `ApplyVisor(vl)`.
* Nameplates: `PlayerVoteArea.SetCosmetics(NetworkedPlayerInfo)` postfix → `Background.sprite`; `CosmeticsCache.GetNameplate(string)` postfix mutating `__result.Image`; `PlayerVoteArea.PreviewNameplate(string)` prefix remembering the id for the lambda postfix if present.
* Skins/pets: NOT in scope (document why: animation clips). Optional stretch: `SkinViewData.IdleFrame/EjectFrame` swap only.
* All hooks: `if (!Options.CosmeticsEnabled) return;` try/catch; never throw inside LateUpdate (log once per id).

### `src/Cosmetics/LobbyMusic.cs` (owner: cosmetics-music)
Recipe of v03-3: load clip once (WAV parser supporting 16/24-bit PCM and 32-bit float, mono/stereo, chunk walking; OGG via
`UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.OGGVORBIS)` inside a `WrapToIl2Cpp()` coroutine started on
`LobbyBehaviour.Instance`, falling back to WAV parser); `LobbyBehaviour.FixedUpdate` postfix throttled to every 15 calls: stop
`"MapTheme"` (read `LobbyBehaviour.MAP_THEME_NAME`, fallback "MapTheme") and play ours via `SoundManager.Instance.PlayDynamicSound("HostRolesLobbyMusic", clip, true, volumeFunc, SoundManager.Instance.MusicChannel)` with a **static** `DynamicSound.GetDynamicsFunction` (converted from `Action<AudioSource,float>` once and kept alive); `mute` → only StopNamedSound; `vanilla` → if our sound is playing stop it and `CrossFadeSound("MapTheme", LobbyBehaviour.Instance.MapTheme, 0.07f)`.
`LobbyBehaviour.OnDestroy` prefix → stop ours. Clip kept in a static with `HideFlags.DontUnloadUnusedAsset`.

### `src/Cosmetics/LobbyDecor.cs` (owner: cosmetics-decor)
* Lobby paint: 0.25 s after `LobbyBehaviour.Start` (Scheduler): clone `GameObject.Find("Leftbox")` → `localPosition (0.042,-2.59,-10.5)`, sprite 290 ppu from `images\lobbypaint.png`.
* Dropship decoration: clone `GameObject.Find("SmallBox")` under `LobbyBehaviour.Instance.transform`, destroy children + `PolygonCollider2D`, sprite 60 ppu, `SetSiblingIndex(1)`, `localPosition (0.05, 0.8334)`.
* Main menu background: `MainMenuManager.Start` postfix → hide `mainMenuUI` child "BackgroundTexture" (null-check), add "HostRolesSplash" GameObject at (0,0,600) with a SpriteRenderer at 150 ppu from `images\menu.png`.
* Cursor: `MainMenuManager.Start` postfix → `Cursor.SetCursor(tex, new Vector2(0,0), CursorMode.ForceSoftware)` once when `images\cursor.png` exists (keep the texture static).
* All scene-object names are unverifiable: null-check every `GameObject.Find`, log once and continue.

### README section (owner: docs-v03)
「見た目のカスタマイズ（ホストの画面だけ）」: folder layout, how to get ids (`/cos ids`), adaptive colours (pure red = body colour, green = shadow, blue = visor — inferred convention), music formats (WAV/OGG, MP3 unsupported), volume, and that other players never see these.
