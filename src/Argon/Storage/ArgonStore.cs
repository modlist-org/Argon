using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using Argon.Hud;

namespace Argon.Storage;

internal sealed class ArgonStore
{
    private const int CurrentSchemaVersion = 2;
    private readonly string _path;
    private bool _readOnly;

    internal ArgonDocument Document { get; }
    internal HudLayoutData ActiveLayout
    {
        get
        {
            var layout = Document.Layouts.FirstOrDefault(candidate => candidate.Id == Document.ActiveLayoutId);
            if (layout != null)
            {
                return layout;
            }

            if (Document.Layouts.Count == 0)
            {
                Document.Layouts.Add(new HudLayoutData());
            }

            Document.ActiveLayoutId = Document.Layouts[0].Id;
            return Document.Layouts[0];
        }
    }

    private ArgonStore(string path, ArgonDocument document)
    {
        _path = path;
        Document = document;
    }

    internal static ArgonStore Load()
    {
        var directory = Path.Combine(Application.persistentDataPath, "Argon");
        var path = Path.Combine(directory, "config.json");
        ArgonDocument? document = TryRead(path);

        if (document == null)
        {
            document = TryRead(path + ".bak");
            if (document != null)
            {
                Debug.LogWarning("[Argon] Recovered settings from the backup file.");
            }
        }

        if (document == null)
        {
            document = CreateDefaultDocument();
        }

        var store = new ArgonStore(path, document);
        store.Migrate();
        if (!File.Exists(path) || !store._readOnly)
        {
            store.Save();
        }

        return store;
    }

    internal void Save()
    {
        if (_readOnly)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        var tempPath = _path + ".tmp";
        var backupPath = _path + ".bak";

        try
        {
            Directory.CreateDirectory(directory);
            var json = JsonConvert.SerializeObject(Document, Formatting.Indented);
            File.WriteAllText(tempPath, json);

            if (!File.Exists(_path))
            {
                File.Move(tempPath, _path);
                return;
            }

            try
            {
                File.Replace(tempPath, _path, backupPath, true);
            }
            catch
            {
                File.Copy(_path, backupPath, true);
                File.Delete(_path);
                File.Move(tempPath, _path);
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Argon] Could not save settings at '{_path}': {exception}");
            TryDelete(tempPath);
        }
    }

    private void Migrate()
    {
        if (Document.SchemaVersion > CurrentSchemaVersion)
        {
            _readOnly = true;
            Debug.LogWarning($"[Argon] Settings schema {Document.SchemaVersion} is newer than supported schema {CurrentSchemaVersion}; settings loaded read-only.");
            return;
        }

        if (Document.SchemaVersion < 1)
        {
            Document.SchemaVersion = 1;
        }

        Document.Layouts ??= new List<HudLayoutData>();
        Document.Preferences ??= new ArgonPreferences();
        Document.PlayRecords ??= new Dictionary<string, LevelPlayRecord>();
        foreach (var key in Document.PlayRecords.Keys.ToArray())
        {
            var record = Document.PlayRecords[key];
            if (record == null)
            {
                Document.PlayRecords[key] = new LevelPlayRecord();
                continue;
            }

            record.Attempts = Math.Max(0, record.Attempts);
            record.BestProgress = ClampFinite(record.BestProgress, 0f, 0f, 1f);
            record.LastPlayedUtcTicks = Math.Max(0L, record.LastPlayedUtcTicks);
        }

        Document.Layouts.RemoveAll(layout => layout == null);
        var layoutIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layout in Document.Layouts)
        {
            if (string.IsNullOrWhiteSpace(layout.Id) || !layoutIds.Add(layout.Id))
            {
                layout.Id = "layout." + Guid.NewGuid().ToString("N");
                layoutIds.Add(layout.Id);
            }

            layout.Name = string.IsNullOrWhiteSpace(layout.Name) ? "레이아웃" : layout.Name.Trim();
        }

        Document.Preferences.Hud ??= new HudDisplayPreferences();
        Document.Preferences.KeyViewer ??= new KeyViewerPreferences();
        Document.Preferences.Appearance ??= new AppearancePreferences();
        var oldKeyCounts = Document.Preferences.KeyViewer.KeyCounts ?? Array.Empty<int>();
        if (oldKeyCounts.Length != 36)
        {
            Document.Preferences.KeyViewer.KeyCounts = new int[36];
            Array.Copy(oldKeyCounts, Document.Preferences.KeyViewer.KeyCounts, Math.Min(oldKeyCounts.Length, 36));
        }
        var keyViewer = Document.Preferences.KeyViewer;
        keyViewer.HandKeyCount = NormalizeHandKeyCount(keyViewer.HandKeyCount);
        keyViewer.FootKeyCount = NormalizeFootKeyCount(keyViewer.FootKeyCount);
        keyViewer.HandBindingSets ??= ReadLegacyBindingSets(keyViewer.HandBindings, keyViewer.HandKeyCount, KeyBindingData.CreateDefaultHandBindingSets());
        keyViewer.FootBindingSets ??= ReadLegacyBindingSets(keyViewer.FootBindings, keyViewer.FootKeyCount, KeyBindingData.CreateDefaultFootBindingSets());
        keyViewer.GhostHandBindingSets ??= KeyBindingData.CreateDefaultGhostHandBindingSets();
        keyViewer.GhostFootBindingSets ??= KeyBindingData.CreateDefaultGhostFootBindingSets();
        ApplyLegacyGhostBindings(keyViewer);
        NormalizeBindingSets(keyViewer.HandBindingSets, KeyBindingData.CreateDefaultHandBindingSets());
        NormalizeBindingSets(keyViewer.FootBindingSets, KeyBindingData.CreateDefaultFootBindingSets());
        NormalizeBindingSets(keyViewer.GhostHandBindingSets, KeyBindingData.CreateDefaultGhostHandBindingSets());
        NormalizeBindingSets(keyViewer.GhostFootBindingSets, KeyBindingData.CreateDefaultGhostFootBindingSets());
        Document.Preferences.KeyViewer.TotalCount = Math.Max(0, Document.Preferences.KeyViewer.TotalCount);
        var keyCounts = Document.Preferences.KeyViewer.KeyCounts!;
        for (var i = 0; i < keyCounts.Length; i++)
        {
            keyCounts[i] = Math.Max(0, keyCounts[i]);
        }
        Document.Preferences.KeyViewer.Scale = ClampFinite(Document.Preferences.KeyViewer.Scale, 1f, 0.25f, 3f);
        Document.Preferences.KeyViewer.VerticalOffset = ClampFinite(Document.Preferences.KeyViewer.VerticalOffset, 200f, 0f, 600f);
        Document.Preferences.KeyViewer.HandOffsetX = ClampFinite(Document.Preferences.KeyViewer.HandOffsetX, 0f, -1000f, 1000f);
        Document.Preferences.KeyViewer.HandOffsetY = ClampFinite(Document.Preferences.KeyViewer.HandOffsetY, 0f, -300f, 300f);
        Document.Preferences.KeyViewer.FootOffsetX = ClampFinite(Document.Preferences.KeyViewer.FootOffsetX, 0f, -1000f, 1000f);
        Document.Preferences.KeyViewer.FootOffsetY = ClampFinite(Document.Preferences.KeyViewer.FootOffsetY, 0f, -300f, 300f);
        Document.Preferences.KeyViewer.KeySize = ClampFinite(Document.Preferences.KeyViewer.KeySize, 56f, 32f, 96f);
        Document.Preferences.KeyViewer.RainSpeed = ClampFinite(Document.Preferences.KeyViewer.RainSpeed, 100f, 10f, 600f);
        Document.Preferences.KeyViewer.RainHeight = ClampFinite(Document.Preferences.KeyViewer.RainHeight, 200f, 20f, 1200f);
        Document.Preferences.HudScale = ClampFinite(Document.Preferences.HudScale, 1f, 0.25f, 3f);
        Document.Preferences.HudOpacity = ClampFinite(Document.Preferences.HudOpacity, 1f, 0f, 1f);
        Document.Preferences.Hud.AccuracyDecimals = Mathf.Clamp(Document.Preferences.Hud.AccuracyDecimals, 0, 4);
        Document.Preferences.Hud.ComboMinimumTier = Mathf.Clamp(Document.Preferences.Hud.ComboMinimumTier, 0, 2);
        Document.Preferences.Hud.ProgressDecimals = Mathf.Clamp(Document.Preferences.Hud.ProgressDecimals, 0, 4);
        Document.Preferences.Hud.BpmDecimals = Mathf.Clamp(Document.Preferences.Hud.BpmDecimals, 0, 4);
        Document.Preferences.Hud.TimingDecimals = Mathf.Clamp(Document.Preferences.Hud.TimingDecimals, 0, 5);
        Document.Preferences.Hud.TimingScaleMilliseconds = ClampFinite(Document.Preferences.Hud.TimingScaleMilliseconds, 150f, 10f, 1000f);
        Document.Preferences.Hud.ComboColorMax = Mathf.Clamp(Document.Preferences.Hud.ComboColorMax, 1, 1000000);
        Document.Preferences.Hud.FontSize = ClampFinite(Document.Preferences.Hud.FontSize, 28f, 12f, 96f);
        var hud = Document.Preferences.Hud;
        hud.TextColor ??= "#FFFFFFFF";
        hud.ProgressLowColor ??= "#E86272FF";
        hud.ProgressMidColor ??= "#E9C46AFF";
        hud.ProgressHighColor ??= "#62D996FF";
        hud.BpmLowColor ??= "#62D996FF";
        hud.BpmMidColor ??= "#E9C46AFF";
        hud.BpmHighColor ??= "#E86272FF";
        hud.TimingGoodColor ??= "#62D996FF";
        hud.TimingBadColor ??= "#E86272FF";
        hud.PurePerfectColor ??= "#62D996FF";
        hud.ComboLowColor ??= "#DFB5FFFF";
        hud.ComboHighColor ??= "#B75AFFFF";
        hud.ComboPerfectColor ??= "#62D996FF";
        hud.ComboEarlyLateColor ??= "#E9C46AFF";
        hud.ProgressBarFillColor ??= "#B984FFFF";
        hud.ProgressBarBackgroundColor ??= "#101018CC";
        hud.ProgressBarBorderColor ??= "#E6D6FFFF";
        var appearance = Document.Preferences.Appearance;
        appearance.PlanetColor ??= "#CFB4F7FF";
        appearance.TileColor ??= "#F2DEFFFF";
        appearance.LogoColor ??= "#9076A1FF";
        appearance.LogoTitle ??= "Argon";
        appearance.AutoIconResourcePath ??= string.Empty;
        keyViewer.HandBindings = ToLegacyToken(keyViewer.HandBindingSets[keyViewer.HandKeyCount]);
        if (keyViewer.FootKeyCount > 0)
        {
            keyViewer.FootBindings = ToLegacyToken(keyViewer.FootBindingSets[keyViewer.FootKeyCount]);
        }
        keyViewer.GhostBindings = ToLegacyGhostToken(keyViewer);
        if (Document.Layouts.Count == 0)
        {
            var defaults = CreateDefaultDocument();
            Document.Layouts = defaults.Layouts;
            Document.ActiveLayoutId = defaults.ActiveLayoutId;
        }

        foreach (var layout in Document.Layouts)
        {
            layout.Elements ??= new List<HudElementLayoutData>();
            for (var i = 0; i < layout.Elements.Count; i++)
            {
                var element = layout.Elements[i];
                if (element == null)
                {
                    layout.Elements.RemoveAt(i--);
                    continue;
                }
                if (element.Configuration == null)
                {
                    element.Configuration = new Newtonsoft.Json.Linq.JObject();
                }

                element.X = ClampFinite(element.X, 0f, -10000f, 10000f);
                element.Y = ClampFinite(element.Y, 0f, -10000f, 10000f);
                element.Width = ClampFinite(element.Width, 200f, 16f, 10000f);
                element.Height = ClampFinite(element.Height, 40f, 16f, 10000f);
                element.Scale = ClampFinite(element.Scale, 1f, 0.1f, 4f);
                element.Opacity = ClampFinite(element.Opacity, 1f, 0f, 1f);
                element.Anchor = string.IsNullOrWhiteSpace(element.Anchor) ? "TopLeft" : element.Anchor;
            }
        }

        if (Document.Layouts.All(layout => layout.Id != Document.ActiveLayoutId))
        {
            Document.ActiveLayoutId = Document.Layouts[0].Id;
        }
    }

    internal LevelPlayRecord RecordAttempt(string levelKey)
    {
        var record = GetRecord(levelKey);
        record.Attempts++;
        record.LastPlayedUtcTicks = DateTime.UtcNow.Ticks;
        Save();
        return record;
    }

    internal LevelPlayRecord RecordProgress(string levelKey, float progress)
    {
        var record = GetRecord(levelKey);
        var safeProgress = Mathf.Clamp01(float.IsNaN(progress) || float.IsInfinity(progress) ? 0f : progress);
        if (safeProgress > record.BestProgress)
        {
            record.BestProgress = safeProgress;
            Save();
        }

        return record;
    }

    internal LevelPlayRecord GetRecord(string levelKey)
    {
        if (!Document.PlayRecords.TryGetValue(levelKey, out var record))
        {
            record = new LevelPlayRecord();
            Document.PlayRecords.Add(levelKey, record);
        }

        return record;
    }

    private static ArgonDocument? TryRead(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<ArgonDocument>(json, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.None,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
            });
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Argon] Could not read settings file '{path}': {exception.Message}");
            return null;
        }
    }

    private static ArgonDocument CreateDefaultDocument()
    {
        var layout = new HudLayoutData
        {
            Id = "default",
            Name = "기본",
            BuiltInDefaultsAdded = true,
            Elements = new List<HudElementLayoutData>
            {
                DefaultElement("argon.builtin.fps.1", "argon.builtin.fps", "TopLeft", 24f, -24f, 180f, 42f, 0),
                DefaultElement("argon.builtin.progress.1", "argon.builtin.progress", "TopLeft", 24f, -64f, 360f, 42f, 1),
                DefaultElement("argon.builtin.accuracy.1", "argon.builtin.accuracy", "TopLeft", 24f, -112f, 380f, 220f, 2),
                DefaultElement("argon.builtin.bpm.1", "argon.builtin.bpm", "TopRight", -24f, -24f, 280f, 100f, 3),
                DefaultElement("argon.builtin.status.1", "argon.builtin.status", "TopLeft", 420f, -24f, 460f, 300f, 4),
                DefaultElement("argon.builtin.judgement.1", "argon.builtin.judgement", "BottomCenter", 0f, 44f, 420f, 76f, 5),
                DefaultElement("argon.builtin.combo.1", "argon.builtin.combo", "TopCenter", 0f, -112f, 260f, 88f, 6),
                DefaultElement("argon.builtin.progress-bar.1", "argon.builtin.progress-bar", "TopCenter", 0f, -18f, 640f, 14f, 7),
            },
        };

        return new ArgonDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            ActiveLayoutId = layout.Id,
            Layouts = new List<HudLayoutData> { layout },
            Preferences = new ArgonPreferences(),
        };
    }

    private static HudElementLayoutData DefaultElement(
        string instanceId,
        string elementId,
        string anchor,
        float x,
        float y,
        float width,
        float height,
        int order)
    {
        return new HudElementLayoutData
        {
            InstanceId = instanceId,
            ElementId = elementId,
            Enabled = true,
            Anchor = anchor,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Order = order,
            Scale = 1f,
            Opacity = 1f,
        };
    }

    private static int NormalizeHandKeyCount(int value)
    {
        return value == 10 || value == 12 || value == 20 ? value : 16;
    }

    private static float ClampFinite(float value, float fallback, float min, float max)
    {
        return Mathf.Clamp(float.IsNaN(value) || float.IsInfinity(value) ? fallback : value, min, max);
    }

    private static void NormalizeBindingSets(Dictionary<int, KeyBindingData[]> bindings, Dictionary<int, KeyBindingData[]> defaults)
    {
        foreach (var pair in defaults)
        {
            if (!bindings.TryGetValue(pair.Key, out var values) || values == null || values.Length != pair.Value.Length)
            {
                bindings[pair.Key] = pair.Value;
                continue;
            }

            for (var i = 0; i < values.Length; i++)
            {
                values[i] ??= new KeyBindingData { KeyCode = pair.Value[i].KeyCode };
                if (!Enum.IsDefined(typeof(KeyCode), values[i].KeyCode))
                {
                    values[i].KeyCode = pair.Value[i].KeyCode;
                }
            }
        }
    }

    private static Dictionary<int, KeyBindingData[]> ReadLegacyBindingSets(
        Newtonsoft.Json.Linq.JToken? token,
        int selectedCount,
        Dictionary<int, KeyBindingData[]> defaults)
    {
        if (token is Newtonsoft.Json.Linq.JObject)
        {
            try
            {
                var parsed = token.ToObject<Dictionary<int, KeyBindingData[]>>();
                if (parsed != null) return parsed;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Argon] Could not migrate legacy key binding map: {exception.Message}");
            }
        }

        if (token is not Newtonsoft.Json.Linq.JArray)
        {
            return defaults;
        }

        try
        {
            var legacy = token.ToObject<KeyBindingData[]>() ?? Array.Empty<KeyBindingData>();
            if (!defaults.TryGetValue(selectedCount, out var baseline) || legacy.Length == 0)
            {
                return defaults;
            }

            var merged = CloneBindings(baseline);
            for (var i = 0; i < Math.Min(legacy.Length, merged.Length); i++)
            {
                if (legacy[i] != null) merged[i] = legacy[i];
            }

            defaults[selectedCount] = merged;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Argon] Could not migrate legacy key bindings: {exception.Message}");
        }

        return defaults;
    }

    private static void ApplyLegacyGhostBindings(KeyViewerPreferences preferences)
    {
        if (preferences.GhostBindings is not Newtonsoft.Json.Linq.JArray token) return;
        try
        {
            var legacy = token.ToObject<KeyBindingData[]>() ?? Array.Empty<KeyBindingData>();
            if (legacy.All(binding => binding == null || binding.KeyCode == 0)) return;

            if (preferences.GhostHandBindingSets!.TryGetValue(preferences.HandKeyCount, out var hand))
            {
                for (var i = 0; i < Math.Min(hand.Length, legacy.Length); i++)
                {
                    if (legacy[i] != null) hand[i] = legacy[i];
                }
            }

            if (preferences.FootKeyCount > 0 && preferences.GhostFootBindingSets!.TryGetValue(preferences.FootKeyCount, out var foot))
            {
                for (var i = 0; i < foot.Length && i + 20 < legacy.Length; i++)
                {
                    if (legacy[i + 20] != null) foot[i] = legacy[i + 20];
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Argon] Could not migrate legacy ghost key bindings: {exception.Message}");
        }
    }

    private static KeyBindingData[] CloneBindings(KeyBindingData[] source)
    {
        return source.Select(binding => binding == null
            ? new KeyBindingData()
            : new KeyBindingData { KeyCode = binding.KeyCode, Label = binding.Label }).ToArray();
    }

    private static Newtonsoft.Json.Linq.JToken ToLegacyToken(KeyBindingData[] bindings)
    {
        return Newtonsoft.Json.Linq.JArray.FromObject(bindings);
    }

    private static Newtonsoft.Json.Linq.JToken ToLegacyGhostToken(KeyViewerPreferences preferences)
    {
        var legacy = new KeyBindingData[36];
        for (var i = 0; i < legacy.Length; i++) legacy[i] = new KeyBindingData();
        if (preferences.GhostHandBindingSets!.TryGetValue(preferences.HandKeyCount, out var hand))
        {
            Array.Copy(hand, legacy, Math.Min(20, hand.Length));
        }

        if (preferences.FootKeyCount > 0 && preferences.GhostFootBindingSets!.TryGetValue(preferences.FootKeyCount, out var foot))
        {
            Array.Copy(foot, 0, legacy, 20, Math.Min(16, foot.Length));
        }

        return Newtonsoft.Json.Linq.JArray.FromObject(legacy);
    }

    private static int NormalizeFootKeyCount(int value)
    {
        return value == 0 || value == 2 || value == 4 || value == 6 || value == 8 || value == 16 ? value : 4;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup; preserve the original settings file.
        }
    }
}
