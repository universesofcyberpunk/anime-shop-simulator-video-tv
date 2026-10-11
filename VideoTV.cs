// VideoTV - Anime Shop Simulator (by Fabianens)
// Kanallar: UserData\VideoTV klasorundeki videolar = "Video TV"; her alt klasor (UserData\VideoTV\<Ad>) = ayri bir kanal ve
// magazada kendi TV'si ("Video TV - <Ad>"). Her TV kendi kanalinin videolarini dosya adi sirasiyla oynatir.
//  - TV, oyunun posterlerinden biri (mobilya / Posterler) kopyalanarak yapilir (PosterShop ile ayni yontem), 16:9 ekran + siyah cerceve.
//  - Satis: sadece ModMarket MODLAR ekraninda, kendi "TV" filtresinde (RegisterItem(id, "TV", true)).
//  - TV'ye bakinca solda tuslar: durdur / oynat, onceki, sonraki (bakilan TV'nin kanali). Ses kapat tusu her yerden calisir.
//  - Ayni kanalin butun TV'leri ayni yayini gosterir (kanal basina tek video oynatici, tek ekran dokusu). Oyuncu yakinda degilse
//    video durur, 20 sn sonra oynatici kapanir; geri gelince kaldigi yerden devam eder.
// Video: oyunun kodu VideoPlayer'in cogu metodunu kirpmis (IL2CPP), ama motor (UnityPlayer.dll) hepsini tasiyor. Bu yuzden
// VideoPlayer metotlari dogrudan motor cagrilariyla (il2cpp_resolve_icall) kullanilir.
// Kayit metotlarina ve FishNet SyncVar geri cagrilarina yama YOK.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.InputSystem;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using Il2CppFishNet;
using Il2CppFishNet.Object;
using Il2CppGPUInstancer;
using Il2CppI2.Loc;
using Il2CppProject.Code.Gameplay.Configs;
using Il2CppProject.Code.Gameplay.Definitions;
using Il2CppProject.Code.Gameplay.Player.Build;
using Il2CppProject.Code.Gameplay.Services;
using DDef = Il2CppProject.Code.Core.Utils.DataDefinition.DataDefinition;
using PickupDB = Il2CppProject.Code.Core.Utils.DataDefinition.DataDefinition<Il2CppProject.Code.Gameplay.Definitions.PickupDefinition>;
using BuildingDB = Il2CppProject.Code.Core.Utils.DataDefinition.DataDefinition<Il2CppProject.Code.Gameplay.Definitions.BuildingPieceDefinition>;

[assembly: MelonInfo(typeof(VideoTV.Core), "VideoTV", "1.2.2", "Fabianens")]
[assembly: MelonGame("OneMoreTime", "Anime Shop Simulator")]

namespace VideoTV
{
    // VideoPlayer motor cagrilari (IL2CPP'de kirpilan metotlar). Ilk parametre her zaman bilesenin il2cpp nesnesi.
    static class VP
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void DVoid(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void DPtr(IntPtr self, IntPtr v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void DInt(IntPtr self, int v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void DBool(IntPtr self, byte v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void DUShort(IntPtr self, ushort v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void DDouble(IntPtr self, double v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate byte DGetBool(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DGetUInt(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate double DGetDouble(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void DTrackPtr(IntPtr self, ushort track, IntPtr v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void DTrackBool(IntPtr self, ushort track, byte v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void DTrackFloat(IntPtr self, ushort track, float v);

        public static DInt SetSource, SetRenderMode, SetAspectRatio, SetAudioOutputMode;
        public static DPtr SetUrl, SetTargetTexture;
        public static DBool SetLooping, SetPlayOnAwake, SetSkipOnDrop, SetWaitForFirstFrame;
        public static DUShort SetControlledTracks;
        public static DDouble SetTime;
        public static DTrackBool EnableAudioTrack;
        public static DTrackPtr SetTargetAudioSource;
        public static DTrackFloat SetDirectVolume;
        public static DVoid Prepare, Play, Pause, Stop;
        public static DGetBool IsPrepared, IsPlaying;
        public static DGetUInt Width, Height;
        public static DGetDouble Time, Length;

        public static bool Ok;
        public static readonly List<string> Missing = new();

        static T R<T>(string name) where T : Delegate
        {
            IntPtr p = IntPtr.Zero;
            try { p = IL2CPP.il2cpp_resolve_icall("UnityEngine.Video.VideoPlayer::" + name); } catch { }
            if (p == IntPtr.Zero) { Missing.Add(name); return null; }
            return Marshal.GetDelegateForFunctionPointer<T>(p);
        }

        public static bool Init()
        {
            Missing.Clear();
            SetSource = R<DInt>("set_source"); SetRenderMode = R<DInt>("set_renderMode");
            SetAspectRatio = R<DInt>("set_aspectRatio"); SetAudioOutputMode = R<DInt>("set_audioOutputMode");
            SetUrl = R<DPtr>("set_url"); SetTargetTexture = R<DPtr>("set_targetTexture");
            SetLooping = R<DBool>("set_isLooping"); SetPlayOnAwake = R<DBool>("set_playOnAwake");
            SetSkipOnDrop = R<DBool>("set_skipOnDrop"); SetWaitForFirstFrame = R<DBool>("set_waitForFirstFrame");
            SetControlledTracks = R<DUShort>("SetControlledAudioTrackCount");
            SetTime = R<DDouble>("set_time");
            EnableAudioTrack = R<DTrackBool>("EnableAudioTrack");
            SetTargetAudioSource = R<DTrackPtr>("SetTargetAudioSource"); SetDirectVolume = R<DTrackFloat>("SetDirectAudioVolume");
            Prepare = R<DVoid>("Prepare"); Play = R<DVoid>("Play"); Pause = R<DVoid>("Pause"); Stop = R<DVoid>("Stop");
            IsPrepared = R<DGetBool>("get_isPrepared"); IsPlaying = R<DGetBool>("get_isPlaying");
            Width = R<DGetUInt>("get_width"); Height = R<DGetUInt>("get_height");
            Time = R<DGetDouble>("get_time"); Length = R<DGetDouble>("get_length");
            Ok = SetSource != null && SetUrl != null && SetRenderMode != null && SetTargetTexture != null && Prepare != null
              && Play != null && Pause != null && Stop != null && IsPrepared != null;
            return Ok;
        }
    }

    public class Core : MelonMod
    {
        // --- sabitler (degistirme: kayit dosyalari bu ID'lere bagli) ---
        const string ModName = "VideoTV";
        const string TitleTr = "Video TV", TitleEn = "Video TV";
        const string Category = "TV";
        const int IdBase = 1820000000;
        const int Slots = 4000000;
        const string RootKey = "VideoTV";        // ana kanalin kimligi; alt klasorler "VideoTV/<klasor>" (ID bundan hesaplanir)
        const string ObjPrefix = ModName + "_";
        const string BezelPrefix = ObjPrefix + "Bezel_";
        static readonly string[] VideoExts = { ".mp4", ".m4v", ".mov", ".webm", ".avi", ".wmv", ".mpg", ".mpeg", ".ogv", ".asf" };

        static Core _inst;
        static MelonLogger.Instance Log => _inst.LoggerInstance;

        static MelonPreferences_Category _cat;
        static MelonPreferences_Entry<bool> _enabled, _sound, _loop, _glow, _messages, _controls;
        static MelonPreferences_Entry<int> _volume, _distance, _maxHeight, _level, _srcItem;
        static MelonPreferences_Entry<float> _price, _size;
        static MelonPreferences_Entry<string> _aspectCfg, _nameEn, _nameTr, _keyPause, _keyPrev, _keyNext, _keyMute;

        static string _dataDir;
        static bool _built, _failed, _building;
        static readonly List<Channel> _channels = new();
        static readonly List<NetworkObject> _netPrefabs = new();
        static IntPtr _registeredNm = IntPtr.Zero;
        static ProductsConfig _cfgCache;
        static readonly List<(string msg, bool warn)> _pendingToasts = new();
        float _nextCheck;
        bool _sceneReady;

        static float _aspect = 16f / 9f;
        static PickupDefinition _srcPickup;

        // klasordeki bir video = oynatma listesinde bir sira
        sealed class Vid
        {
            public string File, Key, NameEn, NameTr;
            public float VolumeMul = 1f;
            public bool Failed;
        }

        // bir kanal = bir klasor = magazada bir TV urunu = bir video oynatici
        sealed class Channel
        {
            public string Key, Folder, Label;         // Label: alt klasor adi (ana kanalda null)
            public string Image;                      // kanal resmi (klasordeki png / jpg)
            public string NameEn, NameTr;
            public readonly List<Vid> Videos = new();
            public int PickupId, ItemId;
            public string NameKey;
            public PickupDefinition Pickup;
            public BuildingPieceDefinition Bd;
            public GameObject Prefab;
            public Material Mat;                      // ekran malzemesi (bu kanalin tum TV'leri paylasir)
            public Texture2D Still;                   // video oynamazken ekranda gorunen resim
            public MelonPreferences_Entry<string> Current;   // en son oynayan video (oyunlar arasi)
            public readonly List<Placed> Placed = new();
            public readonly List<Vector3> PlacedPos = new();
            public TvPlayer Tv;
            public int Index;
            public bool Paused, Ended;
            public double ResumeAt;
            public Vid Cur => Videos.Count == 0 ? null : Videos[Math.Clamp(Index, 0, Videos.Count - 1)];
        }

        sealed class TvPlayer
        {
            public GameObject Go;
            public IntPtr Vp;
            public Component VpComp;
            public AudioSource Audio;
            public bool DirectAudio;
            public RenderTexture Rt;
            public int Index = -1;
            public float Created, PlayStarted, InactiveSince = -1f;
            public double ResumeAt;
            public bool Prepared, Playing, Glow;
        }

        static bool TR { get { try { var c = LocalizationManager.CurrentLanguageCode; return c != null && c.StartsWith("tr", StringComparison.OrdinalIgnoreCase); } catch { return false; } } }
        static string L(string tr, string en) => TR ? tr : en;

        public override void OnInitializeMelon()
        {
            _inst = this;
            _cat = MelonPreferences.CreateCategory(ModName);
            _enabled = _cat.CreateEntry("Enabled", true, "Play videos", "On = the TVs play their videos. Off = they show a still picture (no CPU / GPU cost). / Açık = TV'ler videoları oynatır. Kapalı = duran resim.");
            _sound = _cat.CreateEntry("Sound", true, "Sound", "Sound of all TVs on / off (also the mute key). / Bütün TV'lerin sesi açık / kapalı (sessiz tuşu da bunu değiştirir).");
            _volume = _cat.CreateEntry("Volume", 50, "Volume (0-100)", "Loudness of the TVs. Each TV's sound comes from it and gets quieter with distance. / TV ses seviyesi.");
            _loop = _cat.CreateEntry("Loop", true, "Repeat the list", "On = after the last video the first one starts again. Off = the TV stops after the last video. / Açık = son videodan sonra baştan başlar.");
            _distance = _cat.CreateEntry("PlayDistance", 25, "Play distance (m)", "A TV plays only while you are this close (metres); farther away it pauses (saves performance). / Sadece bu kadar yakındayken oynar.");
            _glow = _cat.CreateEntry("ScreenGlow", true, "Screen glow", "The playing screen glows like a real TV. / Oynayan ekran gerçek TV gibi parlar.");
            _controls = _cat.CreateEntry("ShowControls", true, "Show TV controls", "Key hints on the left while you look at a TV. / TV'ye bakınca soldaki tuş ipuçları.");
            _keyPause = _cat.CreateEntry("PauseKey", "P", "Pause / play key", "While looking at a TV. / TV'ye bakarken.");
            _keyPrev = _cat.CreateEntry("PreviousKey", "LeftArrow", "Previous video key", "While looking at a TV. / TV'ye bakarken.");
            _keyNext = _cat.CreateEntry("NextKey", "RightArrow", "Next video key", "While looking at a TV. / TV'ye bakarken.");
            _keyMute = _cat.CreateEntry("MuteKey", "M", "Mute key", "Mutes all TVs. Works anywhere (no need to look at a TV), not while a menu or the computer is open. / Bütün TV'leri susturur; her yerden çalışır.");
            _maxHeight = _cat.CreateEntry("MaxVideoHeight", 720, "Screen resolution (height px)", "Height of the screen texture in pixels (720 = HD). Lower = less GPU memory. Restart needed. / Ekran dokusunun yüksekliği.");
            _size = _cat.CreateEntry("TvSize", 1.3f, "TV size", "TV width (1 = a game poster's width, about 1.1 m). Restart needed. / TV boyu (yeniden başlatma gerekir).");
            _aspectCfg = _cat.CreateEntry("TvAspect", "16:9", "Screen shape", "16:9, 4:3, 21:9, 9:16 (phone videos)... Videos keep their shape inside (black borders). Restart needed. / Ekran şekli.");
            _price = _cat.CreateEntry("Price", 0f, "Price", "Store price of a TV (0 = automatic). Restart needed. / TV fiyatı.");
            _level = _cat.CreateEntry("Level", 0, "Level", "Unlock level (0 = same as the copied game poster). Restart needed. / Açıldığı seviye.");
            _nameEn = _cat.CreateEntry("Name", "Video TV", "Name", "Item name (all languages except Turkish). Channel TVs get \" - <folder>\" added. / Ürün adı.");
            _nameTr = _cat.CreateEntry("NameTurkish", "Video TV", "Name (Turkish)", "Item name when the game language is Turkish. / Türkçe ürün adı.");
            _srcItem = _cat.CreateEntry("SourceItemId", 0, "Source poster ID", "Pickup ID of the game poster copied (0 = automatic, see catalog.txt). / Kopyalanan oyun posteri.");
            _messages = _cat.CreateEntry("Notifications", true, "Notifications", "Show on-screen notifications. / Ekran bildirimleri.");

            _aspect = ParseAspect(_aspectCfg.Value) ?? 16f / 9f;
            _dataDir = Path.Combine(MelonEnvironment.UserDataDirectory, ModName);
            Directory.CreateDirectory(_dataDir);
            if (!VP.Init()) LoggerInstance.Error("Video functions missing in this game build: " + string.Join(", ", VP.Missing) + " - TVs will show a still picture only.");
            else LoggerInstance.Msg("Video player ready" + (VP.Missing.Count > 0 ? " (missing, not needed: " + string.Join(", ", VP.Missing) + ")" : "") + ".");
            try { ScanFolders(); } catch (Exception e) { LoggerInstance.Error("Folder scan / Klasor taramasi: " + e); }
            ApplyPatches();
            LoggerInstance.Msg($"Ready. {_channels.Count} channel(s), {_channels.Sum(c => c.Videos.Count)} video(s) in {_dataDir}");
        }

        static float? ParseAspect(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var st = System.Globalization.NumberStyles.Float;
            s = s.Trim();
            if (s.Contains(':'))
            {
                var ab = s.Split(':');
                if (ab.Length == 2 && float.TryParse(ab[0], st, ci, out var a) && float.TryParse(ab[1], st, ci, out var b) && a > 0 && b > 0) return Math.Clamp(a / b, 0.25f, 4f);
                return null;
            }
            return float.TryParse(s, st, ci, out var f) && f > 0 ? Math.Clamp(f, 0.25f, 4f) : (float?)null;
        }

        // ------------------------------------------------------------------ klasorler = kanallar

        void ScanFolders()
        {
            // kanal = UserData\VideoTV icindeki her alt klasor (videosu olmasa da: TV bos ekranla durur). Ana klasor kanal degil.
            foreach (var f in Directory.GetFiles(_dataDir).Where(f => VideoExts.Contains(Path.GetExtension(f).ToLowerInvariant())))
                LoggerInstance.Warning($"  {Path.GetFileName(f)} is in the main folder and is not played: put videos into a sub folder (one sub folder = one TV)");
            foreach (var d in Directory.GetDirectories(_dataDir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var n = Path.GetFileName(d);
                if (n.StartsWith("_") || n.StartsWith(".")) continue;   // _icons vb.
                AddChannel(d, n, true);
            }
            if (_channels.Count == 0)
            {
                // ilk kurulum: bos bir ornek kanal klasoru ac ki kullanici nereye koyacagini gorsun
                try { Directory.CreateDirectory(Path.Combine(_dataDir, "TV1")); AddChannel(Path.Combine(_dataDir, "TV1"), "TV1", true); } catch { }
            }
            var used = new HashSet<int>();
            foreach (var c in _channels)
            {
                int h = (int)(NameHash(c.Key) % Slots);
                while (used.Contains(h)) { LoggerInstance.Warning($"ID collision / ID cakismasi: {c.Key}"); h = (h + 1) % Slots; }
                used.Add(h);
                c.PickupId = IdBase + 2 * h; c.ItemId = c.PickupId + 1; c.NameKey = ModName + "/" + c.PickupId;
            }
        }

        void AddChannel(string dir, string label, bool always)
        {
            var c = new Channel { Folder = dir, Label = label, Key = label == null ? RootKey : RootKey + "/" + label.ToLowerInvariant() };
            var all = Directory.GetFiles(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var f in all)
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".mkv" || ext == ".flv" || ext == ".gif" || ext == ".3gp" || ext == ".ts")
                    LoggerInstance.Warning($"  skipped {Path.GetFileName(f)}: {ext} is not supported, convert it to .mp4 (H.264) or .webm (VP8)");
            }
            foreach (var f in all.Where(f => VideoExts.Contains(Path.GetExtension(f).ToLowerInvariant())))
            {
                var v = new Vid { File = f, Key = Path.GetFileNameWithoutExtension(f) };
                v.NameEn = v.NameTr = Pretty(v.Key);
                ReadJson(v, dir);
                c.Videos.Add(v);
            }
            if (!always && c.Videos.Count == 0) return;
            // kanal resmi: klasordeki ilk png / jpg (tv_icon.png varsa o) - magaza resmi ve TV kapaliyken ekran
            var imgs = all.Where(f => { var e = Path.GetExtension(f).ToLowerInvariant(); return e == ".png" || e == ".jpg" || e == ".jpeg"; }).ToList();
            c.Image = imgs.FirstOrDefault(f => Path.GetFileName(f).Equals("tv_icon.png", StringComparison.OrdinalIgnoreCase)) ?? imgs.FirstOrDefault();
            string pretty = label != null ? Pretty(label) : null;
            c.NameEn = label == null ? _nameEn.Value : _nameEn.Value + " - " + pretty;
            c.NameTr = label == null ? _nameTr.Value : _nameTr.Value + " - " + pretty;
            c.Current = _cat.CreateEntry(label == null ? "CurrentVideo" : "CurrentVideo_" + EntryId(label), "", "Current video", "File name of the video this TV was playing (kept between games).", true);
            c.Index = Math.Max(0, c.Videos.FindIndex(v => Path.GetFileName(v.File).Equals(c.Current.Value, StringComparison.OrdinalIgnoreCase)));
            _channels.Add(c);
            LoggerInstance.Msg($"  channel '{c.NameEn}' ({(label == null ? "main folder" : label)}): {c.Videos.Count} video(s)");
            int i = 0;
            foreach (var v in c.Videos) LoggerInstance.Msg($"    {++i}. {Path.GetFileName(v.File)} '{v.NameEn}'");
        }

        static string EntryId(string key)
        {
            var sb = new StringBuilder();
            foreach (var ch in key) sb.Append(ch < 128 && (char.IsLetterOrDigit(ch) || ch == '_') ? ch : '_');
            return sb.ToString();
        }

        static uint NameHash(string s)
        {
            uint h = 2166136261;
            foreach (var c in (s ?? "").ToLowerInvariant()) { h ^= c; h *= 16777619; }
            return h;
        }

        static string Pretty(string key)
        {
            var parts = key.Replace('_', ' ').Replace('-', ' ').Replace('.', ' ')
                           .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                           .Where(t => !(t.Length >= 16 && t.All(Uri.IsHexDigit)) && !t.Equals("sample", StringComparison.OrdinalIgnoreCase)).ToList();
            var s = string.Join(" ", parts);
            if (!s.Any(char.IsLetter)) s = "Video " + (NameHash(key) % 10000).ToString("0000");
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        // istege bagli <video>.json: name, nameTr, volume
        void ReadJson(Vid v, string dir)
        {
            string json = Path.Combine(dir, v.Key + ".json");
            if (!File.Exists(json)) return;
            try
            {
                var opts = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
                using var doc = JsonDocument.Parse(File.ReadAllText(json), opts);
                var p = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var x in doc.RootElement.EnumerateObject()) p[x.Name.Replace("_", "")] = x.Value.Clone();
                string S(params string[] ks) { foreach (var k in ks) if (p.TryGetValue(k, out var e) && e.ValueKind == JsonValueKind.String && e.GetString().Trim().Length > 0) return e.GetString().Trim(); return null; }
                var name = S("name", "nameEn", "title"); var nameTr = S("nameTr", "titleTr", "ad");
                if (name != null) v.NameEn = name;
                v.NameTr = nameTr ?? name ?? v.NameTr;
                if (p.TryGetValue("volume", out var vo) && vo.ValueKind == JsonValueKind.Number) v.VolumeMul = Math.Clamp((float)vo.GetDouble(), 0f, 2f);
            }
            catch (Exception e) { LoggerInstance.Warning($"JSON okunamadi / could not read {Path.GetFileName(json)}: {e.Message}"); }
        }

        static string VName(Vid v) => v == null ? "-" : L(v.NameTr, v.NameEn);

        // ------------------------------------------------------------------ Harmony (sadece GPU Instancer)

        void ApplyPatches()
        {
            try
            {
                var m = typeof(PoolService).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                         .FirstOrDefault(x => x.Name == nameof(PoolService.SetPrefabInstancing) && !x.IsGenericMethod);
                if (m != null) HarmonyInstance.Patch(m, prefix: new HarmonyLib.HarmonyMethod(typeof(Core).GetMethod(nameof(PrefabInstancingPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception e) { LoggerInstance.Warning("Patch SetPrefabInstancing: " + e.Message); }
            // videosuz kanal (ornegin bos ana klasor) magazada gorunmez; tanimi kayitli kalir (duvardaki TV kaybolmaz)
            try
            {
                var w = typeof(Il2CppProject.Code.Gameplay.UI.Computer.MenuOrdersWidget);
                var m = w.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                         .FirstOrDefault(x => x.Name == nameof(Il2CppProject.Code.Gameplay.UI.Computer.MenuOrdersWidget.GetContentPickups) && !x.IsGenericMethod);
                if (m != null)
                {
                    var post = new HarmonyLib.HarmonyMethod(typeof(Core).GetMethod(nameof(HideEmptyPostfix), BindingFlags.Static | BindingFlags.NonPublic)) { priority = HarmonyLib.Priority.Last };
                    HarmonyInstance.Patch(m, postfix: post);
                }
            }
            catch (Exception e) { LoggerInstance.Warning("Patch GetContentPickups: " + e.Message); }
        }

        static void HideEmptyPostfix(Il2CppSystem.Collections.Generic.List<PickupDefinition> __result)
        {
            if (!_built || __result == null) return;
            try
            {
                HashSet<int> off = null;
                foreach (var c in _channels) if (c.Label == null) (off ??= new HashSet<int>()).Add(c.PickupId);
                if (off == null) return;
                for (int i = __result.Count - 1; i >= 0; i--) { var d = __result[i]; if (d != null && off.Contains(d._id)) __result.RemoveAt(i); }
            }
            catch { }
        }

        static bool IsOurs(GameObject go)
        {
            try { return go != null && go.name != null && go.name.StartsWith(ObjPrefix, StringComparison.Ordinal); } catch { return false; }
        }
        static void PrefabInstancingPrefix(GPUInstancerPrefab gpuiPrefab, ref bool useInstancing)
        {
            if (useInstancing && gpuiPrefab != null && IsOurs(gpuiPrefab.gameObject)) useInstancing = false;
        }

        // ------------------------------------------------------------------ ModMarket + dongu

        public override void OnLateInitializeMelon() { RegisterModMarket(true); }

        static void RegisterModMarket(bool log)
        {
            try
            {
                Type t = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    if (asm.GetName().Name == "ModMarket") { t = asm.GetType("ModMarket.Core"); break; }
                var m = t?.GetMethod("RegisterItem", new[] { typeof(int), typeof(string), typeof(bool) });
                if (m == null)
                {
                    Log.Error(t == null ? "Fabianens Mod Market is REQUIRED (Mods\\ModMarket.dll not found) - TVs cannot be bought."
                                        : "Fabianens Mod Market 1.4.0 or newer is REQUIRED - please update ModMarket.");
                    _pendingToasts.Add((L(t == null ? "Mod Market gerekli! (Fabianens Mod Market kurun)" : "Mod Market 1.4+ gerekli, güncelleyin",
                                          t == null ? "Mod Market is required! (install Fabianens Mod Market)" : "Mod Market 1.4+ is required, please update"), true));
                    return;
                }
                // kategori yok: MODLAR ekraninda oyunun kendi mobilya filtresinde (kopyalanan posterin turu) listelenir, normal magazada yok
                foreach (var c in _channels) m.Invoke(null, new object[] { c.PickupId, null, true });
                if (log) Log.Msg($"Mod Market: {_channels.Count} TV(s) listed only on the MODS screen ({Category}).");
            }
            catch (Exception e) { Log.Warning("Mod Market registration: " + e.Message); }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            _sceneReady = true;
            _nextCheck = Time.unscaledTime + 3f;
            _sceneName = sceneName ?? "";
        }
        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            foreach (var c in _channels) { try { KillPlayer(c); } catch { } c.Placed.Clear(); c.PlacedPos.Clear(); }
        }
        static string _sceneName = "";

        public override void OnUpdate()
        {
            try { UpdateToasts(); } catch { _toasts.Clear(); }
            try { SyncPlaced(); } catch (Exception e) { if (_syncErr++ < 3) Log.Warning("TV sync: " + e.Message); }
            try { UpdateVideos(); } catch (Exception e) { if (_videoErr++ < 5) Log.Warning("Video: " + e); }
            try { UpdateControls(); } catch (Exception e) { if (_ctrlErr++ < 5) Log.Warning("TV controls: " + e.Message); HideHud(); }
            if (_failed || !_sceneReady || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + (_built ? 15f : 2f);
            try
            {
                if (_cfgCache == null || _cfgCache.WasCollected) _cfgCache = FindAll<ProductsConfig>().FirstOrDefault();
                if (_cfgCache == null) return;
                if (!_built) { SafeEnsure(_cfgCache); if (!_built) return; }
                EnsureRegistered(_cfgCache);
                EnsureNetworkPrefabs();
                foreach (var c in _channels) AddTerm(c.NameKey, c.NameTr, c.NameEn, SafeKey(_srcPickup), false);
                FlushToasts();
            }
            catch (Exception e)
            {
                _failed = true;
                LoggerInstance.Error("Beklenmeyen hata, mod durduruldu / unexpected error, mod stopped: " + e);
            }
        }

        void FlushToasts()
        {
            if (_pendingToasts.Count == 0) return;
            if (_sceneName == "Bootstrap" || _sceneName == "Menu" || _sceneName.Length == 0) return;
            foreach (var (m, w) in _pendingToasts) Show(m, w);
            _pendingToasts.Clear();
        }

        static void SafeEnsure(ProductsConfig cfg)
        {
            if (cfg == null || _failed || _building || _built) return;
            try
            {
                if (cfg._furnitureDefinitions == null || cfg._furnitureDefinitions.Count == 0) return;
                if (FindAll<BuildingConfig>().FirstOrDefault() == null) return;
                _building = true;
                try { Build(cfg); } finally { _building = false; }
            }
            catch (Exception e) { _failed = true; Log.Error("Kayit hatasi, mod durduruldu / registration error, mod stopped: " + e); }
        }

        // ------------------------------------------------------------------ build once

        static bool IsOurId(int id) => id != 0 && _channels.Any(c => c.PickupId == id || c.ItemId == id);

        static void Build(ProductsConfig cfg)
        {
            var t0 = DateTime.Now;
            var all = new List<PickupDefinition>();
            foreach (var l in new[] { cfg._productsDefinitions, cfg._brandDefinitions, cfg._furnitureDefinitions, cfg._decor2FloorDefinitions }) all.AddRange(ToList(l));
            var vanilla = new HashSet<int>(all.Select(SafeId));
            try { var d = BuildingDB.s_DefinitionsById; if (d != null) foreach (var k in d.Keys) vanilla.Add(k); } catch { }
            bool moved = false;
            var ours = new HashSet<int>();
            foreach (var c in _channels)
            {
                int tries = 0;
                while ((vanilla.Contains(c.PickupId) || vanilla.Contains(c.ItemId) || ours.Contains(c.PickupId)) && tries++ < 1000) { c.PickupId += 2; c.ItemId += 2; moved = true; }
                ours.Add(c.PickupId);
                c.NameKey = ModName + "/" + c.PickupId;
            }
            if (moved) { Log.Warning("TV ID collided with the game, moved"); RegisterModMarket(false); }

            var holder = new GameObject(ObjPrefix + "Prefabs");
            holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(holder);
            holder.hideFlags = HideFlags.HideAndDontSave;

            var cands = Candidates(cfg);
            WriteCatalog(cands);
            if (cands.Count == 0) { Log.Warning("Kaynak poster bulunamadi / no vanilla poster found. See catalog.txt"); _failed = true; return; }
            _srcPickup = _srcItem.Value != 0 ? cands.FirstOrDefault(c => SafeId(c.p) == _srcItem.Value).p : null;
            _srcPickup ??= cands.FirstOrDefault(c => !c.p._notAvailable && c.p._price > 0 && !IsDlcName(c.tex)).p
                        ?? cands.FirstOrDefault(c => !c.p._notAvailable).p ?? cands[0].p;
            float maxPosterPrice = cands.Where(c => !c.p._notAvailable && !IsDlcName(c.tex)).Select(c => c.p._price).DefaultIfEmpty(100f).Max();
            float price = _price.Value > 0 ? _price.Value : (float)Math.Round(Math.Max(100f, maxPosterPrice) * 3f / 10f) * 10f;

            foreach (var c in _channels)
            {
                try { BuildTv(c, holder, price); }
                catch (Exception e) { Log.Error($"TV '{c.NameEn}' could not be made / TV yapilamadi: " + e); c.Pickup = null; }
            }
            _channels.RemoveAll(c => c.Pickup == null);
            if (_channels.Count == 0) { _failed = true; return; }

            _built = true;
            EnsureInLists(cfg);
            try { cfg.RebuildDefinitions(); } catch (Exception e) { Log.Warning("RebuildDefinitions: " + e.Message); }
            RegisterBuildings();
            EnsureRegistered(cfg);
            foreach (var c in _channels) AddTerm(c.NameKey, c.NameTr, c.NameEn, SafeKey(_srcPickup), true);
            int vids = _channels.Sum(c => c.Videos.Count);
            Log.Msg($"{_channels.Count} TV(s) added in {(DateTime.Now - t0).TotalMilliseconds:0} ms (price {price})");
            _pendingToasts.Insert(0, vids > 0
                ? (_channels.Count > 1 ? L($"{_channels.Count} Video TV kanalı, {vids} video (MODLAR > Mobilya)", $"{_channels.Count} Video TV channels, {vids} video(s) (MODS > Furniture)")
                                       : L($"Video TV hazır: {vids} video (MODLAR > Mobilya)", $"Video TV ready: {vids} video(s) (MODS > Furniture)"), false)
                : (L($"Video yok: UserData\\{ModName} klasörüne mp4 / webm koyun", $"No videos: put mp4 / webm files into UserData\\{ModName}"), true));
            if (!VP.Ok) _pendingToasts.Add((L("Bu oyun sürümünde video oynatılamıyor, TV resim gösterir", "Video playback is not available in this game version, TVs show a picture"), true));
        }

        static bool IsDlcName(string s) => s != null && (s.IndexOf("DLC", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("18+", StringComparison.Ordinal) >= 0 || s.IndexOf("_old", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("_wip", StringComparison.OrdinalIgnoreCase) >= 0);

        // ------------------------------------------------------------------ kaynak posterler

        static List<(PickupDefinition p, string tex)> Candidates(ProductsConfig cfg)
        {
            var res = new List<(PickupDefinition, string)>();
            foreach (var l in new[] { cfg._furnitureDefinitions, cfg._decor2FloorDefinitions })
                foreach (var p in ToList(l))
                {
                    if (IsOurId(SafeId(p))) continue;
                    FurnitureSubtype st; try { st = p._furnitureSubtype; } catch { continue; }
                    if (st != FurnitureSubtype.Posters) continue;
                    BuildingPieceDefinition bd = null; try { bd = p.BuildingDefinition; } catch { }
                    if (bd == null || bd.Prefab == null) continue;
                    var (_, _, tex) = FindArt(bd.Prefab.gameObject);
                    if (tex == null) continue;
                    res.Add((p, Norm(tex.name)));
                }
            return res.OrderBy(c => c.Item2.IndexOf("poster", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1).ToList();
        }

        static (Renderer r, int slot, Texture tex) FindArt(GameObject go)
        {
            (Renderer, int, Texture) best = (null, -1, null); int bestArea = -1; bool bestPref = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                Il2CppReferenceArray<Material> mats; try { mats = r.sharedMaterials; } catch { continue; }
                if (mats == null) continue;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (m == null) continue;
                    var t = MainTex(m); if (t == null) continue;
                    bool pref = Norm(t.name).IndexOf("poster", StringComparison.OrdinalIgnoreCase) >= 0;
                    int area = t.width * t.height;
                    if ((pref && !bestPref) || (pref == bestPref && area > bestArea)) { best = (r, i, t); bestArea = area; bestPref = pref; }
                }
            }
            return best;
        }

        static Texture MainTex(Material m)
        {
            try
            {
                if (m.HasProperty("_BaseMap")) { var t = m.GetTexture("_BaseMap"); if (t != null) return t; }
                if (m.HasProperty("_MainTex")) { var t = m.GetTexture("_MainTex"); if (t != null) return t; }
            }
            catch { }
            return null;
        }

        static string Norm(string n) => (n ?? "").Replace("(Clone)", "").Replace("(Instance)", "").Trim();

        // ------------------------------------------------------------------ TV (poster kopyasi + cerceve)

        static void BuildTv(Channel c, GameObject holder, float price)
        {
            var srcP = _srcPickup;
            var srcBd = srcP.BuildingDefinition;
            string objKey = c.Label == null ? "TV" : "TV_" + EntryId(c.Label);
            var go = UnityEngine.Object.Instantiate<GameObject>(srcBd.Prefab.gameObject, holder.transform);
            go.name = ObjPrefix + objKey;
            foreach (var gp in go.GetComponentsInChildren<GPUInstancerPrefab>(true)) gp.prefabPrototype = null;

            c.Still = MakeStill(c);
            ApplyScreenMaterial(c, go);
            try { ReshapePoster(c, go, _aspect, _size.Value > 0 ? _size.Value : 1.3f); }
            catch (Exception e) { Log.Warning($"TV shape: {e.Message}"); }
            try { AddBezel(c, go); }
            catch (Exception e) { Log.Warning($"TV frame: {e.Message}"); }

            c.Prefab = go;
            var bd = UnityEngine.Object.Instantiate<BuildingPieceDefinition>(srcBd);
            bd.name = ObjPrefix + "Building_" + objKey;
            bd.hideFlags = HideFlags.DontUnloadUnusedAsset;
            bd._id = c.ItemId;
            var no = go.GetComponent<NetworkObject>();
            bd._prefab = no;
            var piece = go.GetComponent<BuildingPiece>();
            if (piece != null) piece._definition = bd;
            var icon = MakeIcon(c);
            if (icon != null) bd._icon = icon;
            c.Bd = bd;
            if (no != null)
            {
                try { no.SetInitializedValues(null, true); } catch (Exception e) { Log.Warning("SetInitializedValues: " + e.Message); }
                _netPrefabs.Add(no);
            }

            var p = UnityEngine.Object.Instantiate<PickupDefinition>(srcP);
            p.name = ObjPrefix + "Pickup_" + objKey;
            p.hideFlags = HideFlags.DontUnloadUnusedAsset;
            p._id = c.PickupId;
            p._nameKey = c.NameKey;
            p._buildingDefinition = bd;
            try { var box = p._boxDefinition; if (box != null && SafeId(box) == SafeId(srcBd)) p._boxDefinition = bd; }
            catch (Exception e) { Log.Warning("Box definition: " + e.Message); }
            p._notAvailable = false;
            // 1.2.1: magazada oyunun "Mobilya" filtresinde (kopyalanan posterin "Posterler" turunde degil; oradayken PosterShop'un
            // kendi POSTERLER dugmesiyle birlikte iki Posterler filtresi gorunuyordu)
            try { p._furnitureSubtype = FurnitureSubtype.Furniture; } catch (Exception e) { Log.Warning("Furniture type: " + e.Message); }
            if (price > 0) p._price = price;
            if (_level.Value > 0) p._level = _level.Value;
            if (icon != null) p._icon = icon;
            c.Pickup = p;
            Log.Msg($"TV '{c.NameEn}' (ID {c.PickupId}) copied from '{SafeName(srcP)}' ({SafeId(srcP)}), size {_size.Value}, aspect {_aspect:0.###}");
        }

        // Poster resim malzemesini kanalin ekran malzemesiyle degistir (PosterShop ApplyArt ile ayni; MaterialEffect listesi dahil).
        static void ApplyScreenMaterial(Channel c, GameObject go)
        {
            var (_, _, tmpl) = FindArt(go);
            if (tmpl == null) throw new Exception("no texture on the copied poster / kopyada doku yok");
            string tmplName = tmpl.name;
            var made = new Dictionary<IntPtr, Material>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials; if (mats == null) continue;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (m == null) continue;
                    var t = MainTex(m);
                    if (t == null || t.name != tmplName) continue;
                    if (!made.TryGetValue(m.Pointer, out var nm))
                    {
                        nm = c.Mat ?? new Material(m) { name = ObjPrefix + "Screen_" + c.PickupId };
                        nm.hideFlags = HideFlags.DontUnloadUnusedAsset;
                        try { nm.enableInstancing = false; } catch { }
                        c.Mat = nm;
                        made[m.Pointer] = nm;
                    }
                    mats[i] = nm; changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
            if (c.Mat == null) throw new Exception("texture slot not found / doku yuvasi bulunamadi");
            SetScreen(c, c.Still, false);
            foreach (var fx in go.GetComponentsInChildren<Il2CppProject.Code.Utils.MaterialEffect>(true))
            {
                try
                {
                    var sm = fx._startMaterials; if (sm == null) continue;
                    for (int i = 0; i < sm.Count; i++) { var m = sm[i]; if (m != null && made.TryGetValue(m.Pointer, out var nm2)) sm[i] = nm2; }
                }
                catch (Exception e) { Log.Warning($"MaterialEffect: {e.Message}"); }
            }
        }

        // ekran dokusu + parlama (emission). Malzeme kanalin tum TV'lerinde ortak.
        static void SetScreen(Channel c, Texture tex, bool glow)
        {
            var m = c.Mat; if (m == null) return;
            SetTex(m, "_BaseMap", tex); SetTex(m, "_MainTex", tex);
            try
            {
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
                if (m.HasProperty("_EmissionColor"))
                {
                    if (glow)
                    {
                        m.EnableKeyword("_EMISSION");
                        SetTex(m, "_EmissionMap", tex);
                        m.SetColor("_EmissionColor", new Color(0.85f, 0.85f, 0.85f, 1f));
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    }
                    else
                    {
                        m.SetColor("_EmissionColor", Color.black);
                        m.DisableKeyword("_EMISSION");
                    }
                }
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.75f);
            }
            catch { }
        }

        // PosterShop ile ayni: resmi tasiyan parcalarin transform'u olceklenir (mesh okunamaz), carpisma kutusu ve yerlesim siniri da.
        static void ReshapePoster(Channel ch, GameObject go, float aspect, float size)
        {
            var scaled = new HashSet<IntPtr>();
            Vector3? rootK = null; int parts = 0; string sizeLog = ""; bool rootScaled = false;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null || mf.sharedMesh == null || !HasMat(r, ch.Mat)) continue;
                var t = r.transform;
                if (scaled.Contains(t.Pointer)) continue;
                var ext = mf.sharedMesh.bounds.size;
                int d = ext.x <= ext.y && ext.x <= ext.z ? 0 : ext.y <= ext.z ? 1 : 2;
                var m = go.transform.worldToLocalMatrix * t.localToWorldMatrix;
                int a1 = d == 0 ? 1 : 0, a2 = d == 2 ? 1 : 2;
                Vector3 Ax(int i) { var v = Vector3.zero; v[i] = 1f; return m.MultiplyVector(v); }
                int hA = Math.Abs(Ax(a1).normalized.y) >= Math.Abs(Ax(a2).normalized.y) ? a1 : a2, wA = hA == a1 ? a2 : a1;
                float W0 = ext[wA] * Ax(wA).magnitude, H0 = ext[hA] * Ax(hA).magnitude;
                if (W0 < 1e-5f || H0 < 1e-5f) continue;
                float Lw = Math.Max(W0, H0) * size;
                float W = aspect >= 1f ? Lw : Lw * aspect, H = aspect >= 1f ? Lw / aspect : Lw;
                float kw = W / W0, kh = H / H0;
                if (rootK == null)
                {
                    var wDir = Ax(wA).normalized;
                    rootK = Math.Abs(wDir.x) >= Math.Abs(wDir.z) ? new Vector3(kw, kh, 1f) : new Vector3(1f, kh, kw);
                    sizeLog = $"{W0:0.000} x {H0:0.000} -> {W:0.000} x {H:0.000} m";
                }
                if (Math.Abs(kw - 1f) < 1e-4f && Math.Abs(kh - 1f) < 1e-4f) continue;
                var c = mf.sharedMesh.bounds.center;
                var before = t.TransformPoint(c);
                var ls = t.localScale; ls[wA] *= kw; ls[hA] *= kh; t.localScale = ls;
                var after = t.TransformPoint(c);
                if (t.parent != null) t.position += before - after;
                scaled.Add(t.Pointer); parts++;
                if (t.Pointer == go.transform.Pointer) rootScaled = true;
            }
            foreach (var lg in go.GetComponentsInChildren<LODGroup>(true)) { try { lg.RecalculateBounds(); } catch { } }
            if (rootK == null) { Log.Warning("poster mesh not found, shape unchanged"); return; }
            var k = rootK.Value;
            if (parts > 0 && !rootScaled)
            {
                foreach (var bc in go.GetComponents<BoxCollider>()) { bc.size = Vector3.Scale(bc.size, k); bc.center = Vector3.Scale(bc.center, k); }
                var border = go.transform.Find("BuildingObjects/BuildingBorder");
                if (border != null)
                {
                    border.localScale = Vector3.Scale(border.localScale, k);
                    border.localPosition = Vector3.Scale(border.localPosition, k);
                }
                var piece = go.GetComponent<BuildingPiece>();
                if (piece != null)
                {
                    var b = piece._localBounds;
                    if (b.size.sqrMagnitude > 1e-6f) piece._localBounds = new Bounds(Vector3.Scale(b.center, k), Vector3.Scale(b.size, k));
                }
            }
            Log.Msg($"screen {sizeLog} ({parts} part(s) scaled{(rootScaled ? ", root" : "")})");
        }

        static bool HasMat(Renderer r, Material mat)
        {
            try { foreach (var m in r.sharedMaterials) if (m != null && mat != null && m.Pointer == mat.Pointer) return true; } catch { }
            return false;
        }

        // Siyah TV cercevesi: ekranin dort kenarina ince cubuklar, ekran duzleminin ortasinda (ekranin ustunu ortmez).
        // 1.0.0'daki arka govde bazi posterlerde ekranin ONUNE geliyordu (ses vardi, goruntu kapaliydi) - kaldirildi.
        static Mesh _cube;
        static Material _bezelMat;
        static Mesh CubeMesh()
        {
            if (_cube != null) return _cube;
            try
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _cube = p.GetComponent<MeshFilter>().sharedMesh;
                UnityEngine.Object.DestroyImmediate(p);
            }
            catch (Exception e) { Log.Warning("Cube mesh: " + e.Message); }
            return _cube;
        }

        static void AddBezel(Channel ch, GameObject go)
        {
            Renderer scr = null;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true)) if (HasMat(r, ch.Mat)) { scr = r; break; }
            var mf = scr != null ? scr.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) { Log.Warning("screen mesh not found, no frame"); return; }
            var mesh = CubeMesh(); if (mesh == null) return;
            if (_bezelMat == null)
            {
                _bezelMat = new Material(ch.Mat) { name = ObjPrefix + "Bezel" };
                _bezelMat.hideFlags = HideFlags.DontUnloadUnusedAsset;
                SetTex(_bezelMat, "_BaseMap", null); SetTex(_bezelMat, "_MainTex", null);
                try
                {
                    var dark = new Color(0.035f, 0.035f, 0.04f, 1f);
                    if (_bezelMat.HasProperty("_BaseColor")) _bezelMat.SetColor("_BaseColor", dark);
                    if (_bezelMat.HasProperty("_Color")) _bezelMat.SetColor("_Color", dark);
                    if (_bezelMat.HasProperty("_EmissionColor")) { _bezelMat.SetColor("_EmissionColor", Color.black); _bezelMat.DisableKeyword("_EMISSION"); }
                    if (_bezelMat.HasProperty("_Smoothness")) _bezelMat.SetFloat("_Smoothness", 0.6f);
                    if (_bezelMat.HasProperty("_Metallic")) _bezelMat.SetFloat("_Metallic", 0.3f);
                }
                catch { }
            }
            var b = mf.sharedMesh.bounds;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); var max = -min;
            for (int i = 0; i < 8; i++)
            {
                var p = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                var lp = go.transform.InverseTransformPoint(scr.transform.TransformPoint(p));
                min = Vector3.Min(min, lp); max = Vector3.Max(max, lp);
            }
            var sz = max - min; var c = (min + max) * 0.5f;
            int d = sz.x <= sz.z ? 0 : 2, w = d == 0 ? 2 : 0;          // d = kalinlik (duvara dik), w = genislik
            float W = sz[w], H = sz.y;
            float bw = Math.Max(0.018f, H * 0.035f), depth = 0.05f;
            float barD = c[d];
            void Box(string name, float cw, float cy, float cd, float sw, float sh, float sd)
            {
                var g = new GameObject(BezelPrefix + name);
                g.layer = scr.gameObject.layer;
                g.transform.SetParent(go.transform, false);
                var pos = Vector3.zero; pos[w] = cw; pos.y = cy; pos[d] = cd;
                var scl = Vector3.one; scl[w] = sw; scl.y = sh; scl[d] = sd;
                g.transform.localPosition = pos; g.transform.localRotation = Quaternion.identity; g.transform.localScale = scl;
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = g.AddComponent<MeshRenderer>();
                r.sharedMaterial = _bezelMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            Box("Top", c[w], c.y + H * 0.5f + bw * 0.5f, barD, W + 2f * bw, bw, depth);
            Box("Bottom", c[w], c.y - H * 0.5f - bw * 1.1f * 0.5f, barD, W + 2f * bw, bw * 1.1f, depth);
            Box("Left", c[w] - W * 0.5f - bw * 0.5f, c.y, barD, bw, H, depth);
            Box("Right", c[w] + W * 0.5f + bw * 0.5f, c.y, barD, bw, H, depth);
        }

        // sahnedeki TV'de cerceve parcasi eksikse (oyun kopyayi kaynak posterden yeniden kurduysa) prefab'dan ekle
        static void EnsureBezel(Channel c, GameObject inst)
        {
            var src = c.Prefab.transform;
            for (int i = 0; i < src.childCount; i++)
            {
                var ch = src.GetChild(i);
                if (!ch.name.StartsWith(BezelPrefix, StringComparison.Ordinal) || inst.transform.Find(ch.name) != null) continue;
                var g = UnityEngine.Object.Instantiate<GameObject>(ch.gameObject, inst.transform);
                g.name = ch.name;
                g.transform.localPosition = ch.localPosition; g.transform.localRotation = ch.localRotation; g.transform.localScale = ch.localScale;
            }
        }

        static void RegisterBuildings()
        {
            var bcfg = FindAll<BuildingConfig>().FirstOrDefault();
            if (bcfg == null) { Log.Warning("BuildingConfig yok / not found"); return; }
            var srcBd = _srcPickup.BuildingDefinition;
            foreach (var c in _channels)
            {
                bcfg._allBuildingPieces = Append(bcfg._allBuildingPieces, c.Bd);
                if (Has(bcfg._availableStore, srcBd)) bcfg._availableStore = Append(bcfg._availableStore, c.Bd);
                if (Has(bcfg._availableStorage, srcBd)) bcfg._availableStorage = Append(bcfg._availableStorage, c.Bd);
                if (Has(bcfg._availableOutside, srcBd)) bcfg._availableOutside = Append(bcfg._availableOutside, c.Bd);
                if (Has(bcfg._available2Floor, srcBd)) bcfg._available2Floor = Append(bcfg._available2Floor, c.Bd);
                if (!Has(bcfg._availableStore, c.Bd) && !Has(bcfg._availableOutside, c.Bd) && !Has(bcfg._available2Floor, c.Bd))
                    bcfg._availableStore = Append(bcfg._availableStore, c.Bd);
            }
            try { bcfg.Initialize(); } catch (Exception e) { Log.Warning("BuildingConfig.Initialize: " + e.Message); }
        }

        // ------------------------------------------------------------------ sahnedeki TV'ler

        // Tum yapi parcalarini taramak pahali: 4 sn'de bir tam tarama (FindObjectsByType), aradaki saniyelerde sadece yerler guncellenir.
        sealed class Placed { public GameObject Go; public Transform Screen; }
        static Dictionary<int, Channel> _byItemId;
        static float _nextSync, _nextFullScan;
        static int _syncErr, _syncLogs, _videoErr, _ctrlErr;
        static void SyncPlaced()
        {
            if (!_built || Time.unscaledTime < _nextSync) return;
            _nextSync = Time.unscaledTime + 1f;
            if (_byItemId == null) { _byItemId = new Dictionary<int, Channel>(); foreach (var c in _channels) _byItemId[c.ItemId] = c; }
            if (Time.unscaledTime >= _nextFullScan)
            {
                _nextFullScan = Time.unscaledTime + 4f;
                foreach (var c in _channels) c.Placed.Clear();
                foreach (var o in UnityEngine.Object.FindObjectsByType(Il2CppType.Of<BuildingPiece>(), FindObjectsSortMode.None))
                {
                    var piece = o?.TryCast<BuildingPiece>(); if (piece == null) continue;
                    int id = 0; try { id = piece._definition != null ? piece._definition._id : 0; } catch { }
                    if (id == 0 || !_byItemId.TryGetValue(id, out var c) || c.Prefab == null) continue;
                    var go = piece.gameObject;
                    EnsureBezel(c, go);
                    int fixedParts = SyncFromPrefab(c.Prefab, go);
                    try { piece._localBounds = c.Prefab.GetComponent<BuildingPiece>()._localBounds; } catch { }
                    if (fixedParts > 0 && _syncLogs++ < 20) Log.Msg($"placed TV '{go.name}' re-applied ({fixedParts} change(s))");
                    Transform scr = null;
                    foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true)) if (HasMat(r, c.Mat)) { scr = r.transform; break; }
                    c.Placed.Add(new Placed { Go = go, Screen = scr });
                }
            }
            foreach (var c in _channels)
            {
                c.PlacedPos.Clear();
                foreach (var p in c.Placed)
                {
                    try
                    {
                        if (p.Go == null || p.Go.WasCollected || !p.Go.activeInHierarchy) continue;
                        c.PlacedPos.Add(p.Screen != null && !p.Screen.WasCollected ? p.Screen.position : p.Go.transform.position);
                    }
                    catch { }
                }
            }
        }

        static int SyncFromPrefab(GameObject prefab, GameObject inst)
        {
            int n = 0;
            var src = prefab.transform;
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
            {
                var dst = t.Pointer == src.Pointer ? inst.transform : inst.transform.Find(RelPath(t, src));
                if (dst == null) continue;
                if (t.Pointer != src.Pointer)
                {
                    if (dst.localScale != t.localScale) { dst.localScale = t.localScale; n++; }
                    if (dst.localPosition != t.localPosition) { dst.localPosition = t.localPosition; n++; }
                }
                var sr = t.GetComponent<MeshRenderer>(); var dr = dst.GetComponent<MeshRenderer>();
                if (sr != null && dr != null)
                {
                    var sm = sr.sharedMaterials; var dm = dr.sharedMaterials;
                    bool same = sm.Length == dm.Length;
                    for (int i = 0; same && i < sm.Length; i++) same = (sm[i] == null && dm[i] == null) || (sm[i] != null && dm[i] != null && sm[i].Pointer == dm[i].Pointer);
                    if (!same) { dr.sharedMaterials = sm; n++; }
                }
                var sc = t.GetComponent<BoxCollider>(); var dc = dst.GetComponent<BoxCollider>();
                if (sc != null && dc != null && (dc.size != sc.size || dc.center != sc.center)) { dc.size = sc.size; dc.center = sc.center; n++; }
            }
            return n;
        }

        static string RelPath(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var c = t; c != null && c.Pointer != root.Pointer; c = c.parent) parts.Add(c.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        // ------------------------------------------------------------------ oynatma listesi (kanal basina)

        static float _nextVideo;
        static Transform _cam;
        static float _nextCam;
        static UnityEngine.Audio.AudioMixerGroup _mixer; static bool _mixerSearched;

        static Transform Cam()
        {
            try
            {
                if ((_cam == null || _cam.WasCollected) && Time.unscaledTime >= _nextCam)
                {
                    _nextCam = Time.unscaledTime + 2f;
                    var c = Camera.main;
                    _cam = c != null ? c.transform : null;
                }
                return _cam != null && !_cam.WasCollected ? _cam : null;
            }
            catch { _cam = null; return null; }
        }

        static void UpdateVideos()
        {
            if (!_built || !VP.Ok || Time.unscaledTime < _nextVideo) return;
            _nextVideo = Time.unscaledTime + 0.25f;
            var cam = Cam();
            foreach (var c in _channels) { try { UpdateChannel(c, cam); } catch (Exception e) { if (_videoErr++ < 5) Log.Warning($"Video ({c.NameEn}): {e.Message}"); } }
        }

        static void UpdateChannel(Channel c, Transform cam)
        {
            if (c.Videos.Count == 0) { if (c.Tv != null) KillPlayer(c); return; }
            float maxD = Math.Max(2, _distance.Value);
            float nearest = float.MaxValue; Vector3 nearPos = Vector3.zero;
            foreach (var p in c.PlacedPos)
            {
                float dd = cam != null ? Vector3.Distance(cam.position, p) : 0f;
                if (dd < nearest) { nearest = dd; nearPos = p; }
            }
            bool want = _enabled.Value && c.PlacedPos.Count > 0 && nearest <= maxD && c.Videos.Any(v => !v.Failed);
            var s = c.Tv;
            if (!want)
            {
                if (s == null) return;
                if (s.InactiveSince < 0f)
                {
                    s.InactiveSince = Time.unscaledTime;
                    if (s.Playing) { VP.Pause(s.Vp); s.Playing = false; }
                    if (!_enabled.Value || c.PlacedPos.Count == 0) KillPlayer(c);
                }
                else if (Time.unscaledTime - s.InactiveSince > 20f) KillPlayer(c);
                return;
            }
            if (s == null) { s = c.Tv = CreatePlayer(c); if (s == null) return; }
            s.InactiveSince = -1f;
            if (s.Index != c.Index) OpenVideo(c, s, c.Index, 0);
            if (s.Audio != null && !s.Audio.WasCollected) { try { s.Audio.transform.position = nearPos; } catch { } }
            ApplyAudio(c, s, nearest);
            if (!s.Prepared)
            {
                if (VP.IsPrepared(s.Vp) != 0)
                {
                    s.Prepared = true;
                    if (s.ResumeAt > 0.5 && VP.SetTime != null) VP.SetTime(s.Vp, s.ResumeAt);
                    s.ResumeAt = 0;
                    Log.Msg($"{c.NameEn} {c.Index + 1}/{c.Videos.Count} {Path.GetFileName(c.Cur.File)}: {(VP.Width != null ? VP.Width(s.Vp) : 0)}x{(VP.Height != null ? VP.Height(s.Vp) : 0)}, {(VP.Length != null ? VP.Length(s.Vp) : 0):0.0} s");
                }
                else if (Time.unscaledTime - s.Created > 15f)
                {
                    var v = c.Cur; v.Failed = true;
                    Log.Warning($"{Path.GetFileName(v.File)}: video could not be opened in 15 s. Use mp4 (H.264 + AAC) or webm (VP8 + Vorbis).");
                    Show(L($"Video açılamadı: {v.NameTr} (mp4 H.264 ya da webm VP8 kullanın)", $"Video could not be opened: {v.NameEn} (use mp4 H.264 or webm VP8)"), true);
                    Step(c, 1, true);
                }
                return;
            }
            if (c.Paused || c.Ended)
            {
                if (s.Playing) { VP.Pause(s.Vp); s.Playing = false; }
                return;
            }
            if (!s.Playing)
            {
                VP.Play(s.Vp); s.Playing = true; s.PlayStarted = Time.unscaledTime;
                s.Glow = _glow.Value; SetScreen(c, s.Rt, s.Glow);
                return;
            }
            if (s.Glow != _glow.Value) { s.Glow = _glow.Value; SetScreen(c, s.Rt, s.Glow); }
            // video bitti mi: oynatici durdu ya da sona geldi -> listede sonraki
            if (Time.unscaledTime - s.PlayStarted > 1f)
            {
                bool stopped = VP.IsPlaying != null && VP.IsPlaying(s.Vp) == 0;
                double len = VP.Length != null ? VP.Length(s.Vp) : 0, tm = VP.Time != null ? VP.Time(s.Vp) : 0;
                if (stopped || (len > 0 && tm >= len - 0.12)) Step(c, 1, true);
            }
        }

        static TvPlayer CreatePlayer(Channel c)
        {
            var s = new TvPlayer { Created = Time.unscaledTime };
            try
            {
                s.Go = new GameObject(ObjPrefix + "Player_" + c.PickupId);
                UnityEngine.Object.DontDestroyOnLoad(s.Go);
                s.VpComp = s.Go.AddComponent(Il2CppType.Of<VideoPlayer>());
                if (s.VpComp == null) throw new Exception("VideoPlayer component could not be added");
                s.Vp = s.VpComp.Pointer;
                VP.SetPlayOnAwake?.Invoke(s.Vp, 0);
                VP.SetWaitForFirstFrame?.Invoke(s.Vp, 1);
                VP.SetSkipOnDrop?.Invoke(s.Vp, 1);
                VP.SetLooping?.Invoke(s.Vp, 0);
                VP.SetSource(s.Vp, 1);   // Url
                VP.SetRenderMode(s.Vp, 2);   // RenderTexture
                VP.SetAspectRatio?.Invoke(s.Vp, 3);   // FitInside: videonun orani korunur, kalan yer siyah
                int h = Math.Clamp(_maxHeight.Value, 144, 2160), w = Math.Min(4096, Math.Max(16, Mathf.RoundToInt(h * _aspect)));
                s.Rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                try { s.Rt.name = ObjPrefix + "screen_" + c.PickupId; s.Rt.wrapMode = TextureWrapMode.Clamp; } catch { }
                try { Graphics.Blit(c.Still != null ? c.Still : SolidTex(Color.black), s.Rt); } catch { }
                VP.SetTargetTexture(s.Vp, s.Rt.Pointer);
                SetupAudio(s);
                s.Glow = _glow.Value;
                SetScreen(c, s.Rt, s.Glow);
                OpenVideo(c, s, c.Index, c.ResumeAt);
                c.ResumeAt = 0;
                Log.Msg($"{c.NameEn}: player on ({(s.DirectAudio ? "direct sound" : s.Audio != null ? "3D sound" : "no sound")}, screen {w}x{h})");
                return s;
            }
            catch (Exception e)
            {
                Log.Warning($"{c.NameEn}: video player: {e.Message}");
                foreach (var v in c.Videos) v.Failed = true;
                try { if (s.Go != null) UnityEngine.Object.Destroy(s.Go); } catch { }
                return null;
            }
        }

        static void OpenVideo(Channel c, TvPlayer s, int index, double resumeAt)
        {
            var v = c.Videos[Math.Clamp(index, 0, c.Videos.Count - 1)];
            if (s.Prepared || s.Playing) { try { VP.Stop(s.Vp); } catch { } }
            s.Index = index; s.Prepared = false; s.Playing = false; s.Created = Time.unscaledTime; s.ResumeAt = resumeAt;
            VP.SetUrl(s.Vp, IL2CPP.ManagedStringToIl2Cpp(v.File));
            VP.Prepare(s.Vp);
        }

        // listede ileri / geri; auto = video kendisi bitti (dongu kapaliysa son videoda durur)
        static void Step(Channel c, int dir, bool auto)
        {
            if (c.Videos.Count == 0) return;
            int n = c.Videos.Count, i = c.Index;
            for (int k = 0; k < n; k++)
            {
                i += dir;
                if (i >= n) { if (auto && !_loop.Value) { c.Ended = true; return; } i = 0; }
                if (i < 0) i = n - 1;
                if (!c.Videos[i].Failed) break;
            }
            // ayni videoya dondu (tek videolu kanal ya da tek saglam video): oynaticiyi bastan acmaya zorla.
            // Yoksa video durmus kalir ve her kontrolde tekrar buraya gelinip ayarlar diske yazilirdi (takilma).
            if (i == c.Index && c.Tv != null) c.Tv.Index = -1;
            c.Index = i; c.Ended = false;
            if (!auto) c.Paused = false;
            try
            {
                string name = Path.GetFileName(c.Videos[c.Index].File);
                if (c.Current.Value != name) { c.Current.Value = name; MelonPreferences.Save(); }   // sadece degisince kaydet
            }
            catch { }
        }

        static void KillPlayer(Channel c)
        {
            var s = c.Tv; if (s == null) return;
            c.Tv = null;
            // kaldigi yer ve son kare: geri gelince ayni yerden devam, kapaliyken ekranda son kare
            try { if (s.Prepared && VP.Time != null) c.ResumeAt = VP.Time(s.Vp); } catch { }
            try { if (s.Rt != null && s.Prepared) KeepLastFrame(c, s.Rt); } catch (Exception e) { Log.Warning("last frame: " + e.Message); }
            try { if (s.Vp != IntPtr.Zero && s.VpComp != null && !s.VpComp.WasCollected) VP.Stop(s.Vp); } catch { }
            try { if (s.Go != null && !s.Go.WasCollected) UnityEngine.Object.Destroy(s.Go); } catch { }
            try { SetScreen(c, c.Still, false); } catch { }
            try { if (s.Rt != null) RenderTexture.ReleaseTemporary(s.Rt); } catch { }
            Log.Msg($"{c.NameEn}: player off");
        }

        static void KeepLastFrame(Channel c, RenderTexture rt)
        {
            int w = Math.Min(640, rt.width), h = Math.Max(2, Mathf.RoundToInt(w / _aspect));
            var t = NewTex(w, h, ObjPrefix + "still_" + c.PickupId);
            Blit(t, rt, new Rect(0, 0, 1, 1), new RectInt(0, 0, w, h));
            t.Apply(true, true);
            var old = c.Still; c.Still = t;
            if (old != null && old.Pointer != t.Pointer) UnityEngine.Object.Destroy(old);
        }

        // ses: 3B AudioSource (en yakin TV'nin yerinde, uzaklikla azalir). Olmazsa dogrudan ses + elle uzaklik azaltma.
        static void SetupAudio(TvPlayer s)
        {
            if (VP.SetAudioOutputMode == null) return;
            try
            {
                if (VP.SetControlledTracks == null || VP.EnableAudioTrack == null || VP.SetTargetAudioSource == null) throw new Exception("audio source calls missing");
                var a = s.Go.AddComponent<AudioSource>();
                a.playOnAwake = false;
                a.spatialBlend = 1f;
                a.rolloffMode = AudioRolloffMode.Linear;
                a.minDistance = 1.5f;
                a.maxDistance = Math.Max(4, _distance.Value);
                a.dopplerLevel = 0f;
                a.volume = 0f;
                try { var g = FindMixer(); if (g != null) a.outputAudioMixerGroup = g; } catch { }
                VP.SetAudioOutputMode(s.Vp, 1);   // AudioSource
                VP.SetControlledTracks(s.Vp, 1);
                VP.EnableAudioTrack(s.Vp, 0, 1);
                VP.SetTargetAudioSource(s.Vp, 0, a.Pointer);
                s.Audio = a;
            }
            catch (Exception e)
            {
                Log.Msg($"3D sound not available ({e.Message}), using direct sound");
                try { if (s.Audio != null) UnityEngine.Object.Destroy(s.Audio); } catch { }
                s.Audio = null;
                if (VP.SetDirectVolume != null) { VP.SetAudioOutputMode(s.Vp, 2); s.DirectAudio = true; VP.EnableAudioTrack?.Invoke(s.Vp, 0, 1); }
                else VP.SetAudioOutputMode(s.Vp, 0);
            }
        }

        // oyunun ses karistiricisi: sahnedeki bir AudioSource'un grubu (once SFX / ses efekti adlilari)
        static UnityEngine.Audio.AudioMixerGroup FindMixer()
        {
            if (_mixer != null || _mixerSearched) return _mixer;
            _mixerSearched = true;
            UnityEngine.Audio.AudioMixerGroup any = null;
            foreach (var o in UnityEngine.Object.FindObjectsByType(Il2CppType.Of<AudioSource>(), FindObjectsSortMode.None))
            {
                var a = o?.TryCast<AudioSource>(); if (a == null) continue;
                UnityEngine.Audio.AudioMixerGroup g = null; try { g = a.outputAudioMixerGroup; } catch { }
                if (g == null) continue;
                var n = (g.name ?? "").ToLowerInvariant();
                if (n.Contains("sfx") || n.Contains("sound") || n.Contains("effect")) { _mixer = g; break; }
                any ??= g;
            }
            _mixer ??= any;
            if (_mixer != null) Log.Msg($"TV sound goes through the game's '{_mixer.name}' volume");
            return _mixer;
        }

        static void ApplyAudio(Channel c, TvPlayer s, float dist)
        {
            float vol = _sound.Value ? Math.Clamp(_volume.Value, 0, 100) / 100f * (c.Cur?.VolumeMul ?? 1f) : 0f;
            vol = Math.Clamp(vol, 0f, 1f);
            try
            {
                float md = Math.Max(4, _distance.Value);
                if (s.Audio != null && !s.Audio.WasCollected)
                {
                    if (Math.Abs(s.Audio.volume - vol) > 0.001f) s.Audio.volume = vol;
                    if (Math.Abs(s.Audio.maxDistance - md) > 0.01f) s.Audio.maxDistance = md;
                }
                else if (s.DirectAudio)
                {
                    float k = dist <= 1.5f ? 1f : Math.Clamp(1f - (dist - 1.5f) / (md - 1.5f), 0f, 1f);
                    VP.SetDirectVolume(s.Vp, 0, vol * k);
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------ tuslar + ipuclari (solda)

        static readonly Dictionary<string, Key> _keys = new(StringComparer.OrdinalIgnoreCase);
        static bool TryKey(string name, out Key k)
        {
            k = Key.None;
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (!_keys.TryGetValue(name, out k)) { if (!Enum.TryParse(name.Trim(), true, out k)) k = Key.None; _keys[name] = k; }
            return k != Key.None;
        }
        static bool Pressed(Keyboard kb, string name)
        {
            if (!TryKey(name, out var k)) return false;
            try { return kb[k].wasPressedThisFrame; } catch { return false; }
        }
        static string KeyLabel(string name)
        {
            if (!TryKey(name, out var k)) return "-";
            switch (k)
            {
                case Key.LeftArrow: return "<";
                case Key.RightArrow: return ">";
                case Key.UpArrow: return "^";
                case Key.DownArrow: return "v";
                case Key.Space: return "Space";
                case Key.Comma: return ",";
                case Key.Period: return ".";
                case Key.Minus: return "-";
                case Key.Equals: return "=";
                default:
                    var s = k.ToString();
                    if (s.StartsWith("Digit")) return s.Substring(5);
                    if (s.StartsWith("Numpad")) return "Num" + s.Substring(6);
                    return s.Length <= 3 ? s.ToUpperInvariant() : s;
            }
        }

        static void UpdateControls()
        {
            if (!_built || _channels.All(c => c.Placed.Count == 0)) { HideHud(); return; }
            var kb = Keyboard.current;
            bool locked = true; try { locked = Cursor.lockState == CursorLockMode.Locked; } catch { }
            // sessiz tusu her yerden calisir (TV'ye bakmak gerekmez); menu / bilgisayar acikken (fare serbest) calismaz
            if (kb != null && locked && Pressed(kb, _keyMute.Value))
            {
                _sound.Value = !_sound.Value; try { MelonPreferences.Save(); } catch { }
                _nextVideo = 0f; _hudSig = null;
                Show(_sound.Value ? L("TV sesi açık", "TV sound on") : L("TV sesi kapalı", "TV sound off"));
            }
            if (!locked) { HideHud(); return; }
            var cam = Cam(); if (cam == null) { HideHud(); return; }
            // yakinda TV yoksa isin bile atilmaz
            var cp = cam.position;
            bool near = false;
            foreach (var c in _channels) { foreach (var p in c.PlacedPos) if ((p - cp).sqrMagnitude < 49f) { near = true; break; } if (near) break; }
            if (!near) { HideHud(); return; }
            var hits = Physics.RaycastAll(cp, cam.forward, 5f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; Transform hitT = null;
            foreach (var h in hits) if (h.distance < best && h.collider != null) { best = h.distance; hitT = h.collider.transform; }
            var ch = hitT != null ? TvOf(hitT) : null;
            if (ch == null) { HideHud(); return; }
            if (kb != null && ch.Videos.Count > 0)
            {
                if (Pressed(kb, _keyPause.Value))
                {
                    if (ch.Ended) { ch.Ended = false; ch.Paused = false; ch.Index = Math.Max(0, ch.Videos.FindIndex(v => !v.Failed)); if (ch.Tv != null) ch.Tv.Index = -1; }
                    else ch.Paused = !ch.Paused;
                    _nextVideo = 0f;
                }
                if (Pressed(kb, _keyNext.Value)) { Step(ch, 1, false); _nextVideo = 0f; }
                if (Pressed(kb, _keyPrev.Value))
                {
                    // ilk 3 sn'den sonra "onceki" once videonun basina doner (oynaticilardaki gibi)
                    bool restart = false;
                    try { restart = ch.Tv != null && ch.Tv.Prepared && VP.Time != null && VP.SetTime != null && VP.Time(ch.Tv.Vp) > 3.0; } catch { }
                    if (restart) { VP.SetTime(ch.Tv.Vp, 0); ch.Paused = false; ch.Ended = false; }
                    else Step(ch, -1, false);
                    _nextVideo = 0f;
                }
            }
            if (_controls.Value) ShowHud(ch); else HideHud();
        }

        static Channel TvOf(Transform t)
        {
            for (var x = t; x != null; x = x.parent)
                foreach (var c in _channels)
                    foreach (var p in c.Placed) { try { if (p.Go != null && !p.Go.WasCollected && x.Pointer == p.Go.transform.Pointer) return c; } catch { } }
            return null;
        }

        // ipucu kutulari: oyunun sagdaki "E Tasi / C Paketle" kutularinin solda aynasi
        static GameObject _hud;
        static TextMeshProUGUI _hudTitle;
        static readonly List<(TextMeshProUGUI key, TextMeshProUGUI label)> _hudRows = new();
        static string _hudSig;

        static void HideHud() { if (_hud != null && _hud.activeSelf) _hud.SetActive(false); }

        static void ShowHud(Channel c)
        {
            if (_hud == null) BuildHud();
            if (_hud == null) return;
            string state = c.Videos.Count == 0 ? L("Video yok", "No videos")
                         : c.Ended ? L("Liste bitti", "List finished")
                         : c.Paused ? L("Durduruldu", "Paused")
                         : !_enabled.Value ? L("Kapalı (F10)", "Off (F10)") : "";
            string chan = _channels.Count > 1 ? (c.Label != null ? Pretty(c.Label) : L("Ana", "Main")) + " · " : "";
            string title = c.Videos.Count == 0 ? chan + state : $"{chan}{c.Index + 1}/{c.Videos.Count}  {VName(c.Cur)}" + (state.Length > 0 ? $"  ({state})" : "");
            var rows = new[]
            {
                (KeyLabel(_keyPause.Value), c.Paused || c.Ended ? L("Oynat", "Play") : L("Durdur", "Pause")),
                (KeyLabel(_keyPrev.Value), L("Önceki", "Previous")),
                (KeyLabel(_keyNext.Value), L("Sonraki", "Next")),
                (KeyLabel(_keyMute.Value), _sound.Value ? L("Sesi kapat", "Mute") : L("Sesi aç", "Unmute")),
            };
            string sig = title + "|" + string.Join("|", rows.Select(r => r.Item1 + r.Item2));
            if (sig != _hudSig)
            {
                _hudSig = sig;
                _hudTitle.text = title;
                for (int i = 0; i < rows.Length && i < _hudRows.Count; i++) { _hudRows[i].key.text = rows[i].Item1; _hudRows[i].label.text = rows[i].Item2; }
            }
            if (!_hud.activeSelf) _hud.SetActive(true);
        }

        static readonly Color CHudBox = new Color(0.05f, 0.05f, 0.07f, 0.72f), CHudKey = new Color(0.97f, 0.97f, 0.97f, 1f), CHudKeyText = new Color(0.08f, 0.08f, 0.1f, 1f);

        static void BuildHud()
        {
            FindFont();
            _hud = new GameObject(ObjPrefix + "Controls");
            UnityEngine.Object.DontDestroyOnLoad(_hud);
            var canvas = _hud.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30001;
            var sc = _hud.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            const float W = 300f, H = 56f, Gap = 10f, Left = 26f;
            float top = 0.5f * 1080f - 2f * (H + Gap);   // dort satir ekranin dikey ortasinda (oyunun ipuclariyla ayni hizada)
            var tg = new GameObject("Title"); tg.transform.SetParent(_hud.transform, false);
            var tr = tg.AddComponent<RectTransform>(); tr.anchorMin = tr.anchorMax = new Vector2(0, 1); tr.pivot = new Vector2(0, 1);
            tr.sizeDelta = new Vector2(W + 200f, 34f); tr.anchoredPosition = new Vector2(Left, -(top - 40f));
            _hudTitle = tg.AddComponent<TextMeshProUGUI>();
            if (_font != null) _hudTitle.font = _font;
            _hudTitle.fontSize = 22; _hudTitle.color = Color.white; _hudTitle.fontStyle = FontStyles.Bold;
            _hudTitle.alignment = TextAlignmentOptions.MidlineLeft; _hudTitle.enableWordWrapping = false; _hudTitle.overflowMode = TextOverflowModes.Ellipsis; _hudTitle.raycastTarget = false;
            try { _hudTitle.outlineWidth = 0.2f; _hudTitle.outlineColor = new Color32(0, 0, 0, 200); } catch { }
            _hudRows.Clear();
            for (int i = 0; i < 4; i++)
            {
                var box = new GameObject("Row" + i); box.transform.SetParent(_hud.transform, false);
                var br = box.AddComponent<RectTransform>(); br.anchorMin = br.anchorMax = new Vector2(0, 1); br.pivot = new Vector2(0, 1);
                br.sizeDelta = new Vector2(W, H); br.anchoredPosition = new Vector2(Left, -(top + i * (H + Gap)));
                var bg = box.AddComponent<Image>(); bg.color = CHudBox; bg.raycastTarget = false;
                var kg = new GameObject("Key"); kg.transform.SetParent(box.transform, false);
                var kr = kg.AddComponent<RectTransform>(); kr.anchorMin = kr.anchorMax = new Vector2(0, 0.5f); kr.pivot = new Vector2(0, 0.5f);
                kr.sizeDelta = new Vector2(44f, 44f); kr.anchoredPosition = new Vector2(6f, 0f);
                var ki = kg.AddComponent<Image>(); ki.color = CHudKey; ki.raycastTarget = false;
                var kt = HudText(kg.transform, "", 24, CHudKeyText, TextAlignmentOptions.Center, 0f);
                var lt = HudText(box.transform, "", 26, Color.white, TextAlignmentOptions.MidlineLeft, 62f);
                _hudRows.Add((kt, lt));
            }
            _hudSig = null;
            _hud.SetActive(false);
        }

        static TextMeshProUGUI HudText(Transform parent, string s, float size, Color c, TextAlignmentOptions al, float left)
        {
            var g = new GameObject("T"); g.transform.SetParent(parent, false);
            var r = g.AddComponent<RectTransform>(); r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(left, 0); r.offsetMax = new Vector2(-6, 0);
            var t = g.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.text = s; t.fontSize = size; t.color = c; t.fontStyle = FontStyles.Bold; t.alignment = al;
            t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Ellipsis; t.raycastTarget = false;
            return t;
        }

        // ------------------------------------------------------------------ resimler

        // TV kapaliyken ekran: kanal klasorundeki resim (varsa, ekrana sigdirilir), yoksa koyu ekran + oynat isareti.
        // Oynatici kapaninca ekranda son kare kalir.
        static Texture2D MakeStill(Channel c)
        {
            int h = 360, w = Math.Max(16, Mathf.RoundToInt(h * _aspect));
            var dst = NewTex(w, h, ObjPrefix + "still_" + c.PickupId);
            var pic = c.Image != null ? LoadTexture(c.Image) : null;
            if (pic != null)
            {
                h = 720; w = Math.Max(16, Mathf.RoundToInt(h * _aspect));
                UnityEngine.Object.Destroy(dst);
                dst = NewTex(w, h, ObjPrefix + "still_" + c.PickupId);
                Blit(dst, SolidTex(Color.black), new Rect(0, 0, 1, 1), new RectInt(0, 0, w, h));
                Blit(dst, pic, new Rect(0, 0, 1, 1), FitRect(pic, new RectInt(0, 0, w, h)));
                UnityEngine.Object.Destroy(pic);
            }
            else dst.SetPixels32(new Il2CppStructArray<Color32>(DrawIdleScreen(w, h)));
            dst.Apply(true, true);
            return dst;
        }

        static RectInt FitRect(Texture src, RectInt box)
        {
            float sa = src.width / (float)src.height, da = box.width / (float)box.height;
            int w = box.width, h = box.height;
            if (sa > da) h = Math.Max(1, Mathf.RoundToInt(w / sa)); else w = Math.Max(1, Mathf.RoundToInt(h * sa));
            return new RectInt(box.x + (box.width - w) / 2, box.y + (box.height - h) / 2, w, h);
        }

        static Texture2D LoadTexture(string p)
        {
            try
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!File.Exists(p) || !ImageConversion.LoadImage(t, File.ReadAllBytes(p), false)) { Log.Warning("Resim okunamadi / image could not be read: " + p); return null; }
                t.hideFlags = HideFlags.DontUnloadUnusedAsset; t.wrapMode = TextureWrapMode.Clamp;
                return t;
            }
            catch (Exception e) { Log.Warning("Image: " + e.Message); return null; }
        }

        static Color32[] DrawIdleScreen(int w, int h)
        {
            var px = new Color32[w * h];
            float cx = w * 0.5f, cy = h * 0.5f, tr = h * 0.16f;
            float lx = cx - tr * 0.6f, rx = cx + tr * 0.8f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float g = 1f - Math.Abs(y - cy) / h;
                    bool tri = x >= lx && x <= rx && Math.Abs(y - cy) <= tr * (rx - x) / (rx - lx);
                    px[y * w + x] = tri ? new Color32(235, 235, 245, 255) : new Color32((byte)(10 + 14 * g), (byte)(12 + 18 * g), (byte)(22 + 30 * g), 255);
                }
            return px;
        }

        // Magaza resmi: kanal klasorunde tv_icon.png varsa aynen o; yoksa 512x512 seffaf zeminde TV cizimi,
        // ekraninda kanal klasorundeki resim (varsa) ya da oynat isareti.
        static Sprite MakeIcon(Channel c)
        {
            var own = Path.Combine(c.Folder, "tv_icon.png");
            if (File.Exists(own)) { var s = LoadSprite(own, "icon_" + c.PickupId); if (s != null) return s; }
            var pic = c.Image != null ? LoadTexture(c.Image) : null;
            if (pic == null && _defaultIcon != null) return _defaultIcon;
            try
            {
                const int S = 512;
                int bodyW = 470, bezel = 14;
                int scrW = bodyW - 2 * bezel, scrH = Mathf.RoundToInt(scrW / _aspect);
                if (scrH > 380) { scrH = 380; scrW = Mathf.RoundToInt(scrH * _aspect); bodyW = scrW + 2 * bezel; }
                int bodyH = scrH + 2 * bezel + 4;
                int bx = (S - bodyW) / 2, by = (S - bodyH) / 2 + 18;
                var px = new Color32[S * S];
                var frameC = new Color32(22, 22, 26, 255); var edgeC = new Color32(48, 48, 56, 255);
                void Fill(int x0, int y0, int x1, int y1, Color32 col) { for (int y = Math.Max(0, y0); y < Math.Min(S, y1); y++) for (int x = Math.Max(0, x0); x < Math.Min(S, x1); x++) px[y * S + x] = col; }
                Fill(bx - 2, by - 2, bx + bodyW + 2, by + bodyH + 2, edgeC);
                Fill(bx, by, bx + bodyW, by + bodyH, frameC);
                Fill(S / 2 - 22, by - 26, S / 2 + 22, by, frameC);
                Fill(S / 2 - 90, by - 36, S / 2 + 90, by - 24, edgeC);
                var scr = DrawIdleScreen(scrW, scrH);
                int sx = bx + bezel, sy = by + bezel + 4;
                for (int y = 0; y < scrH; y++) for (int x = 0; x < scrW; x++) px[(sy + y) * S + sx + x] = scr[y * scrW + x];
                var t = NewTex(S, S, ObjPrefix + "icon");
                t.SetPixels32(new Il2CppStructArray<Color32>(px));
                if (pic != null)
                {
                    var box = new RectInt(sx, sy, scrW, scrH);
                    Blit(t, SolidTex(Color.black), new Rect(0, 0, 1, 1), box);
                    Blit(t, pic, new Rect(0, 0, 1, 1), FitRect(pic, box));
                    UnityEngine.Object.Destroy(pic);
                }
                t.Apply(true, true);
                var sp = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
                sp.name = ObjPrefix + "icon_" + c.PickupId;
                sp.hideFlags = HideFlags.DontUnloadUnusedAsset;
                if (pic == null) _defaultIcon = sp;
                return sp;
            }
            catch (Exception e) { Log.Warning("Icon: " + e.Message); return null; }
        }
        static Sprite _defaultIcon;

        static Texture2D SolidTex(Color c)
        {
            string k = "__solid_" + c.ToString();
            if (_solid.TryGetValue(k, out var t) && t != null && !t.WasCollected) return t;
            t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.SetPixels(new Il2CppStructArray<Color>(new[] { c, c, c, c }));
            t.Apply(false);
            t.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _solid[k] = t;
            return t;
        }
        static readonly Dictionary<string, Texture2D> _solid = new();

        static Texture2D NewTex(int w, int h, string name)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true);
            t.name = name; t.hideFlags = HideFlags.DontUnloadUnusedAsset;
            t.wrapMode = TextureWrapMode.Clamp; t.anisoLevel = 4;
            return t;
        }

        static void Blit(Texture2D dst, Texture src, Rect uv, RectInt to)
        {
            int w = to.width, h = to.height;
            if (to.x < 0 || to.y < 0 || to.x + w > dst.width || to.y + h > dst.height) return;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt, new Vector2(uv.width, uv.height), new Vector2(uv.x, uv.y));
                RenderTexture.active = rt;
                dst.ReadPixels(new Rect(0, 0, w, h), to.x, to.y, false);
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static Sprite LoadSprite(string path, string name)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path), false)) { Log.Warning("Ikon okunamadi / icon could not be read: " + path); return null; }
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.Apply(true, true);
                var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                sp.name = ObjPrefix + name;
                sp.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return sp;
            }
            catch (Exception e) { Log.Warning("Icon: " + e.Message); return null; }
        }

        static void SetTex(Material m, string n, Texture t) { if (m.HasProperty(n)) m.SetTexture(n, t); }

        // ------------------------------------------------------------------ katalog

        static void WriteCatalog(List<(PickupDefinition p, string tex)> cands)
        {
            try
            {
                var sb = new StringBuilder($"{ModName} - game posters that can be copied (SourceItemId in F10)\n");
                sb.Append("pickup id, name, price, level, texture\n");
                foreach (var (p, tex) in cands)
                    sb.Append($"  {SafeId(p)}  '{SafeName(p)}'  price={p._price}  level={p._level}  texture={tex ?? "-"}{(p._notAvailable ? "  (not available)" : "")}\n");
                File.WriteAllText(Path.Combine(_dataDir, "catalog.txt"), sb.ToString());
            }
            catch (Exception e) { Log.Warning("Catalog: " + e.Message); }
        }

        // ------------------------------------------------------------------ kayitli tut

        static Il2CppSystem.Collections.Generic.List<PickupDefinition> ListOf(ProductsConfig cfg)
        {
            int sid = SafeId(_srcPickup);
            if (ListHasId(cfg._decor2FloorDefinitions, sid) && !ListHasId(cfg._furnitureDefinitions, sid)) return cfg._decor2FloorDefinitions;
            return cfg._furnitureDefinitions;
        }

        static IntPtr _listPtr; static int _listCount = -1;
        static bool EnsureInLists(ProductsConfig cfg)
        {
            if (!_built) return false;
            var list = ListOf(cfg); if (list == null) return false;
            int n = list.Count;
            if (list.Pointer == _listPtr && n == _listCount) return false;
            bool changed = false;
            foreach (var c in _channels) if (c.Pickup != null && !ListHasId(list, c.PickupId)) { list.Add(c.Pickup); changed = true; }
            _listPtr = list.Pointer; _listCount = list.Count;
            return changed;
        }

        static void EnsureRegistered(ProductsConfig cfg)
        {
            if (!_built || cfg == null) return;
            if (EnsureInLists(cfg)) { try { cfg.RebuildDefinitions(); } catch { } }
            try { var pd = cfg._pickupDict; if (pd != null) foreach (var c in _channels) if (!pd.ContainsKey(c.PickupId)) pd.Add(c.PickupId, c.Pickup); }
            catch (Exception e) { Log.Warning("Config dictionaries: " + e.Message); }
            try { var d = PickupDB.s_DefinitionsById; if (d != null) foreach (var c in _channels) if (!d.ContainsKey(c.PickupId)) d.Add(c.PickupId, c.Pickup); } catch { }
            try { var d = BuildingDB.s_DefinitionsById; if (d != null) foreach (var c in _channels) if (!d.ContainsKey(c.ItemId)) d.Add(c.ItemId, c.Bd); } catch { }
        }

        static void EnsureNetworkPrefabs()
        {
            if (_netPrefabs.Count == 0) return;
            var nm = InstanceFinder.NetworkManager;
            if (nm == null || nm.Pointer == _registeredNm) return;
            var objs = nm.SpawnablePrefabs;
            if (objs == null) return;
            foreach (var no in _netPrefabs) objs.AddObject(no, true, true);
            _registeredNm = nm.Pointer;
            Log.Msg($"Network prefabs registered ({_netPrefabs.Count})");
        }

        static void AddTerm(string key, string tr, string en, string refKey, bool log)
        {
            try
            {
                LanguageSourceData src = null;
                if (!string.IsNullOrEmpty(refKey)) LocalizationManager.GetTermData(refKey, out src);
                if (src == null) { var all = LocalizationManager.Sources; if (all != null && all.Count > 0) src = all[0]; }
                if (src == null) return;
                var term = src.GetTermData(key, false) ?? src.AddTerm(key);
                if (term == null) return;
                var langs = src.mLanguages;
                int n = langs != null ? langs.Count : 0;
                if (term.Languages == null || term.Languages.Length < n) term.Languages = new Il2CppStringArray(n);
                for (int i = 0; i < n; i++)
                {
                    string code = langs[i].Code ?? "";
                    string want = code.StartsWith("tr", StringComparison.OrdinalIgnoreCase) ? tr : en;
                    if (term.Languages[i] != want) term.Languages[i] = want;
                }
                var dict = src.mDictionary;
                if (dict != null && !dict.ContainsKey(key)) dict.Add(key, term);
                if (log) Log.Msg($"Name added: {en}");
            }
            catch (Exception e) { if (log) Log.Warning("Translation: " + e.Message); }
        }

        // ------------------------------------------------------------------ bildirim (PosterShop ile ayni)

        static void Show(string m, bool warn = false)
        {
            if (warn) Log.Warning(m); else Log.Msg(m);
            if (!_messages.Value) return;
            try { Toast(m, warn); } catch (Exception e) { Log.Warning("Toast: " + e.Message); }
        }

        const float ToastSeconds = 4.5f, ToastFade = 0.35f, ToastWidth = 460f, ToastHeight = 64f, ToastGap = 10f, ToastTop = 100f;
        static readonly Color CToast = new Color32(42, 26, 64, 242), CPink = new Color32(255, 115, 184, 255), CYellow = new Color32(255, 217, 102, 255);
        static readonly Color CText = new Color32(250, 244, 255, 255);

        sealed class ToastItem { public RectTransform Rect; public CanvasGroup Group; public float Born; }
        static readonly List<ToastItem> _toasts = new();
        static GameObject _toastRoot;
        static TMP_FontAsset _font;

        static void FindFont()
        {
            if (_font != null) return;
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<TMP_FontAsset>())) { var f = o.TryCast<TMP_FontAsset>(); if (f != null) { _font = f; break; } }
        }

        static void Toast(string msg, bool warn)
        {
            if (_toastRoot == null)
            {
                _toastRoot = new GameObject(ObjPrefix + "Toasts");
                UnityEngine.Object.DontDestroyOnLoad(_toastRoot);
                var canvas = _toastRoot.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30002;
                var sc = _toastRoot.AddComponent<CanvasScaler>();
                sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            }
            FindFont();

            while (_toasts.Count >= 4) { UnityEngine.Object.Destroy(_toasts[0].Rect.gameObject); _toasts.RemoveAt(0); }

            var box = new GameObject("Toast"); box.transform.SetParent(_toastRoot.transform, false);
            var br = box.AddComponent<RectTransform>();
            br.anchorMin = br.anchorMax = new Vector2(1f, 1f); br.pivot = new Vector2(1f, 1f);
            br.sizeDelta = new Vector2(ToastWidth, ToastHeight);
            var bg = box.AddComponent<Image>(); bg.color = CToast; bg.raycastTarget = false;
            var group = box.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false; group.alpha = 0f;

            var bar = new GameObject("Bar"); bar.transform.SetParent(box.transform, false);
            var barR = bar.AddComponent<RectTransform>(); barR.anchorMin = new Vector2(0, 0); barR.anchorMax = new Vector2(0, 1); barR.pivot = new Vector2(0, 0.5f); barR.sizeDelta = new Vector2(6, 0);
            var barI = bar.AddComponent<Image>(); barI.color = warn ? CYellow : CPink; barI.raycastTarget = false;

            var head = Text(box.transform, L(TitleTr, TitleEn), 15, warn ? CYellow : CPink, 8, 20);
            head.fontStyle = FontStyles.Bold;
            Text(box.transform, msg, 19, CText, 29, 28);

            _toasts.Add(new ToastItem { Rect = br, Group = group, Born = Time.unscaledTime });
            LayoutToasts();
        }

        static TextMeshProUGUI Text(Transform parent, string s, float size, Color c, float top, float height)
        {
            var g = new GameObject("T"); g.transform.SetParent(parent, false);
            var r = g.AddComponent<RectTransform>();
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0, 1);
            r.offsetMin = new Vector2(20, -top - height); r.offsetMax = new Vector2(-14, -top);
            var t = g.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.text = s; t.fontSize = size; t.color = c; t.alignment = TextAlignmentOptions.MidlineLeft;
            t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Ellipsis; t.raycastTarget = false;
            return t;
        }

        static void LayoutToasts()
        {
            for (int i = 0; i < _toasts.Count; i++)
            {
                int slot = _toasts.Count - 1 - i;
                _toasts[i].Rect.anchoredPosition = new Vector2(-24f, -(ToastTop + slot * (ToastHeight + ToastGap)));
            }
        }

        static void UpdateToasts()
        {
            if (_toasts.Count == 0) return;
            float now = Time.unscaledTime;
            bool changed = false;
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var t = _toasts[i];
                if (t.Rect == null) { _toasts.RemoveAt(i); changed = true; continue; }
                float age = now - t.Born;
                if (age >= ToastSeconds) { UnityEngine.Object.Destroy(t.Rect.gameObject); _toasts.RemoveAt(i); changed = true; continue; }
                t.Group.alpha = Mathf.Min(1f, age / ToastFade, (ToastSeconds - age) / ToastFade);
            }
            if (changed) LayoutToasts();
        }

        // ------------------------------------------------------------------ helpers

        static List<PickupDefinition> ToList(Il2CppSystem.Collections.Generic.List<PickupDefinition> l)
        {
            var r = new List<PickupDefinition>();
            if (l == null) return r;
            for (int i = 0; i < l.Count; i++) if (l[i] != null) r.Add(l[i]);
            return r;
        }

        static bool ListHasId(Il2CppSystem.Collections.Generic.List<PickupDefinition> l, int id)
        {
            if (l == null) return false;
            for (int i = 0; i < l.Count; i++) { var e = l[i]; if (e != null && SafeId(e) == id) return true; }
            return false;
        }

        static bool Has(Il2CppReferenceArray<BuildingPieceDefinition> arr, BuildingPieceDefinition item)
        {
            if (arr == null || item == null) return false;
            for (int i = 0; i < arr.Length; i++) if (arr[i] != null && SafeId(arr[i]) == SafeId(item)) return true;
            return false;
        }

        static Il2CppReferenceArray<BuildingPieceDefinition> Append(Il2CppReferenceArray<BuildingPieceDefinition> arr, BuildingPieceDefinition item)
        {
            int len = arr != null ? arr.Length : 0;
            for (int i = 0; i < len; i++) if (arr[i] != null && SafeId(arr[i]) == SafeId(item)) return arr;
            var res = new Il2CppReferenceArray<BuildingPieceDefinition>(len + 1);
            for (int i = 0; i < len; i++) res[i] = arr[i];
            res[len] = item;
            return res;
        }

        static int SafeId(DDef d) { try { return d != null ? d._id : 0; } catch { return 0; } }
        static string SafeKey(DDef d) { try { return d?._nameKey; } catch { return null; } }
        static string SafeName(PickupDefinition d) { try { return d.Name; } catch { try { return d.name; } catch { return "?"; } } }

        static IEnumerable<T> FindAll<T>() where T : Il2CppObjectBase
        {
            var arr = Resources.FindObjectsOfTypeAll(Il2CppType.Of<T>());
            foreach (var o in arr)
            {
                var t = o != null ? o.TryCast<T>() : null;
                if (t != null) yield return t;
            }
        }
    }
}
