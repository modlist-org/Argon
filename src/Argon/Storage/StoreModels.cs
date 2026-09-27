using System.Collections.Generic;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Argon.Storage;

internal sealed class ArgonDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string ActiveLayoutId { get; set; } = "default";
    public List<HudLayoutData> Layouts { get; set; } = new List<HudLayoutData>();
    public ArgonPreferences Preferences { get; set; } = new ArgonPreferences();
    public Dictionary<string, LevelPlayRecord> PlayRecords { get; set; } = new Dictionary<string, LevelPlayRecord>();

    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtensionData { get; set; }
}

internal sealed class ArgonPreferences
{
    public float HudScale { get; set; } = 1f;
    public float HudOpacity { get; set; } = 1f;
    public bool KeyViewerEnabled { get; set; } = true;
    public KeyViewerPreferences KeyViewer { get; set; } = new KeyViewerPreferences();
    public HudDisplayPreferences Hud { get; set; } = new HudDisplayPreferences();
    public AppearancePreferences Appearance { get; set; } = new AppearancePreferences();
}

internal sealed class HudDisplayPreferences
{
    public bool ShowPotentialValues { get; set; } = true;
    public bool ShowAbsoluteAccuracy { get; set; } = true;
    public bool ShowXScore { get; set; } = true;
    public bool ShowMusicTime { get; set; } = true;
    public bool ShowMapTime { get; set; } = true;
    public bool ShowCheckpoint { get; set; } = true;
    public bool ShowBestProgress { get; set; } = true;
    public bool ShowAttempts { get; set; } = true;
    public bool ShowCombo { get; set; } = true;
    public bool CountAutoInCombo { get; set; } = true;
    public int ComboMinimumTier { get; set; } = 1;
    public int ComboColorMax { get; set; } = 1000;
    public bool ShowAuthor { get; set; } = true;
    public bool ShowState { get; set; } = true;
    public bool ShowDeath { get; set; } = true;
    public bool ShowStart { get; set; } = true;
    public bool UsePseudoBpm { get; set; } = true;
    public bool ShowJudgement { get; set; } = true;
    public bool ShowTiming { get; set; } = true;
    public bool ShowTimingScale { get; set; } = true;
    public bool ShowTileInfo { get; set; }
    public bool HideDebugText { get; set; }
    public bool HideHudDuringAuto { get; set; }
    public bool ShowPurePerfectCombo { get; set; } = true;
    public bool UseColorGradients { get; set; } = true;
    public float FontSize { get; set; } = 28f;
    public string TextColor { get; set; } = "#FFFFFFFF";
    public string ProgressLowColor { get; set; } = "#E86272FF";
    public string ProgressMidColor { get; set; } = "#E9C46AFF";
    public string ProgressHighColor { get; set; } = "#62D996FF";
    public string BpmLowColor { get; set; } = "#62D996FF";
    public string BpmMidColor { get; set; } = "#E9C46AFF";
    public string BpmHighColor { get; set; } = "#E86272FF";
    public string TimingGoodColor { get; set; } = "#62D996FF";
    public string TimingBadColor { get; set; } = "#E86272FF";
    public string PurePerfectColor { get; set; } = "#62D996FF";
    public string ComboLowColor { get; set; } = "#DFB5FFFF";
    public string ComboHighColor { get; set; } = "#B75AFFFF";
    public string ComboPerfectColor { get; set; } = "#62D996FF";
    public string ComboEarlyLateColor { get; set; } = "#E9C46AFF";
    public string ProgressBarFillColor { get; set; } = "#B984FFFF";
    public string ProgressBarBackgroundColor { get; set; } = "#101018CC";
    public string ProgressBarBorderColor { get; set; } = "#E6D6FFFF";
    public int AccuracyDecimals { get; set; } = 2;
    public int ProgressDecimals { get; set; } = 2;
    public int BpmDecimals { get; set; } = 2;
    public int TimingDecimals { get; set; } = 2;
    public float TimingScaleMilliseconds { get; set; } = 150f;
}

internal sealed class KeyViewerPreferences
{
    public int VisualStyleVersion { get; set; }
    public int GridLayoutVersion { get; set; }
    public int HandKeyCount { get; set; } = 16;
    public int FootKeyCount { get; set; } = 4;
    public float Scale { get; set; } = 1f;
    public float VerticalOffset { get; set; } = 200f;
    public float HandOffsetX { get; set; }
    public float HandOffsetY { get; set; }
    public float FootOffsetX { get; set; }
    public float FootOffsetY { get; set; }
    public float KeySize { get; set; } = 50f;
    public bool ShowRain { get; set; } = true;
    public bool ShowGhostRain { get; set; }
    public float RainSpeed { get; set; } = 100f;
    public float RainHeight { get; set; } = 200f;
    public bool AutoSetupKeyLimit { get; set; }
    public bool ShowTotalKps { get; set; } = true;
    public int TotalCount { get; set; }
    public int[] KeyCounts { get; set; } = new int[36];
    [JsonProperty("HandBindings", NullValueHandling = NullValueHandling.Ignore)]
    public JToken? HandBindings { get; set; }
    [JsonProperty("FootBindings", NullValueHandling = NullValueHandling.Ignore)]
    public JToken? FootBindings { get; set; }
    [JsonProperty("GhostBindings", NullValueHandling = NullValueHandling.Ignore)]
    public JToken? GhostBindings { get; set; }
    public Dictionary<int, KeyBindingData[]>? HandBindingSets { get; set; }
    public Dictionary<int, KeyBindingData[]>? FootBindingSets { get; set; }
    public Dictionary<int, KeyBindingData[]>? GhostHandBindingSets { get; set; }
    public Dictionary<int, KeyBindingData[]>? GhostFootBindingSets { get; set; }
    public Dictionary<int, KeyViewerSlotLayout[]> HandSlotLayouts { get; set; } = new Dictionary<int, KeyViewerSlotLayout[]>();
    public Dictionary<int, KeyViewerSlotLayout[]> FootSlotLayouts { get; set; } = new Dictionary<int, KeyViewerSlotLayout[]>();
    public string BackgroundColor { get; set; } = "#0E0E11B8";
    public string PressedBackgroundColor { get; set; } = "#FFFFFFE0";
    public string OutlineColor { get; set; } = "#FFFFFF24";
    public string PressedOutlineColor { get; set; } = "#FFFFFF24";
    public string TextColor { get; set; } = "#EDEEF2C7";
    public string PressedTextColor { get; set; } = "#141418E6";
    public string RainColor { get; set; } = "#8320DBCC";
    public string GhostRainColor { get; set; } = "#FFFFFFFF";
}

internal sealed class KeyViewerSlotLayout
{
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float BorderWidth { get; set; } = 1f;
    public float FontSize { get; set; } = 18f;
    public string? BackgroundColor { get; set; }
    public string? PressedBackgroundColor { get; set; }
    public string? OutlineColor { get; set; }
    public string? PressedOutlineColor { get; set; }
    public string? TextColor { get; set; }
    public string? PressedTextColor { get; set; }
    public bool? NoteEffectEnabled { get; set; }
    public bool? CounterVisible { get; set; }
}

internal sealed class KeyBindingData
{
    public int KeyCode { get; set; }
    public string? Label { get; set; }

    internal static KeyBindingData[] CreateDefaultHandBindings()
    {
        var keyCodes = new[]
        {
            UnityEngine.KeyCode.Tab, UnityEngine.KeyCode.Alpha1, UnityEngine.KeyCode.Alpha2,
            UnityEngine.KeyCode.E, UnityEngine.KeyCode.P, UnityEngine.KeyCode.Equals,
            UnityEngine.KeyCode.Backspace, UnityEngine.KeyCode.Backslash, UnityEngine.KeyCode.Space,
            UnityEngine.KeyCode.C, UnityEngine.KeyCode.Comma, UnityEngine.KeyCode.Period,
            UnityEngine.KeyCode.CapsLock, UnityEngine.KeyCode.LeftShift, UnityEngine.KeyCode.Return,
            UnityEngine.KeyCode.H, UnityEngine.KeyCode.CapsLock, UnityEngine.KeyCode.D,
            UnityEngine.KeyCode.RightShift, UnityEngine.KeyCode.Semicolon,
        };

        var result = new KeyBindingData[keyCodes.Length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new KeyBindingData { KeyCode = (int)keyCodes[i] };
        }

        return result;
    }

    internal static Dictionary<int, KeyBindingData[]> CreateDefaultHandBindingSets()
    {
        return new Dictionary<int, KeyBindingData[]>
        {
            { 10, Convert(new[] { UnityEngine.KeyCode.Tab, UnityEngine.KeyCode.Alpha1, UnityEngine.KeyCode.Alpha2, UnityEngine.KeyCode.E, UnityEngine.KeyCode.P, UnityEngine.KeyCode.Equals, UnityEngine.KeyCode.Backspace, UnityEngine.KeyCode.Backslash, UnityEngine.KeyCode.Space, UnityEngine.KeyCode.Comma }) },
            { 12, Convert(new[] { UnityEngine.KeyCode.Tab, UnityEngine.KeyCode.Alpha1, UnityEngine.KeyCode.Alpha2, UnityEngine.KeyCode.E, UnityEngine.KeyCode.P, UnityEngine.KeyCode.Equals, UnityEngine.KeyCode.Backspace, UnityEngine.KeyCode.Backslash, UnityEngine.KeyCode.Space, UnityEngine.KeyCode.C, UnityEngine.KeyCode.Comma, UnityEngine.KeyCode.Period }) },
            { 16, Convert(new[] { UnityEngine.KeyCode.Tab, UnityEngine.KeyCode.Alpha1, UnityEngine.KeyCode.Alpha2, UnityEngine.KeyCode.E, UnityEngine.KeyCode.P, UnityEngine.KeyCode.Equals, UnityEngine.KeyCode.Backspace, UnityEngine.KeyCode.Backslash, UnityEngine.KeyCode.Space, UnityEngine.KeyCode.C, UnityEngine.KeyCode.Comma, UnityEngine.KeyCode.Period, UnityEngine.KeyCode.CapsLock, UnityEngine.KeyCode.LeftShift, UnityEngine.KeyCode.Return, UnityEngine.KeyCode.H }) },
            { 20, Convert(new[] { UnityEngine.KeyCode.Tab, UnityEngine.KeyCode.Alpha1, UnityEngine.KeyCode.Alpha2, UnityEngine.KeyCode.E, UnityEngine.KeyCode.P, UnityEngine.KeyCode.Equals, UnityEngine.KeyCode.Backspace, UnityEngine.KeyCode.Backslash, UnityEngine.KeyCode.Space, UnityEngine.KeyCode.C, UnityEngine.KeyCode.Comma, UnityEngine.KeyCode.Period, UnityEngine.KeyCode.CapsLock, UnityEngine.KeyCode.LeftShift, UnityEngine.KeyCode.Return, UnityEngine.KeyCode.H, UnityEngine.KeyCode.CapsLock, UnityEngine.KeyCode.D, UnityEngine.KeyCode.RightShift, UnityEngine.KeyCode.Semicolon }) },
        };
    }

    internal static KeyBindingData[] CreateDefaultFootBindings()
    {
        var keyCodes = new[]
        {
            UnityEngine.KeyCode.F8, UnityEngine.KeyCode.F4, UnityEngine.KeyCode.F7, UnityEngine.KeyCode.F3,
            UnityEngine.KeyCode.F6, UnityEngine.KeyCode.F2, UnityEngine.KeyCode.F5, UnityEngine.KeyCode.F1,
            UnityEngine.KeyCode.Alpha0, UnityEngine.KeyCode.Alpha6, UnityEngine.KeyCode.Alpha9,
            UnityEngine.KeyCode.Alpha5, UnityEngine.KeyCode.Alpha8, UnityEngine.KeyCode.Alpha4,
            UnityEngine.KeyCode.Alpha7, UnityEngine.KeyCode.Alpha3,
        };

        var result = new KeyBindingData[keyCodes.Length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new KeyBindingData { KeyCode = (int)keyCodes[i] };
        }

        return result;
    }

    internal static Dictionary<int, KeyBindingData[]> CreateDefaultFootBindingSets()
    {
        return new Dictionary<int, KeyBindingData[]>
        {
            { 2, Convert(new[] { UnityEngine.KeyCode.F8, UnityEngine.KeyCode.F3 }) },
            { 4, Convert(new[] { UnityEngine.KeyCode.F8, UnityEngine.KeyCode.F3, UnityEngine.KeyCode.F7, UnityEngine.KeyCode.F2 }) },
            { 6, Convert(new[] { UnityEngine.KeyCode.F8, UnityEngine.KeyCode.F3, UnityEngine.KeyCode.F7, UnityEngine.KeyCode.F2, UnityEngine.KeyCode.F6, UnityEngine.KeyCode.F1 }) },
            { 8, Convert(new[] { UnityEngine.KeyCode.F8, UnityEngine.KeyCode.F4, UnityEngine.KeyCode.F7, UnityEngine.KeyCode.F3, UnityEngine.KeyCode.F6, UnityEngine.KeyCode.F2, UnityEngine.KeyCode.F5, UnityEngine.KeyCode.F1 }) },
            { 16, Convert(new[] { UnityEngine.KeyCode.F8, UnityEngine.KeyCode.F4, UnityEngine.KeyCode.F7, UnityEngine.KeyCode.F3, UnityEngine.KeyCode.F6, UnityEngine.KeyCode.F2, UnityEngine.KeyCode.F5, UnityEngine.KeyCode.F1, UnityEngine.KeyCode.Alpha0, UnityEngine.KeyCode.Alpha6, UnityEngine.KeyCode.Alpha9, UnityEngine.KeyCode.Alpha5, UnityEngine.KeyCode.Alpha8, UnityEngine.KeyCode.Alpha4, UnityEngine.KeyCode.Alpha7, UnityEngine.KeyCode.Alpha3 }) },
        };
    }

    internal static KeyBindingData[] CreateDefaultGhostBindings()
    {
        var hand = CreateDefaultHandBindings();
        var foot = CreateDefaultFootBindings();
        var result = new KeyBindingData[hand.Length + foot.Length];
        for (var i = 0; i < hand.Length; i++)
        {
            result[i] = new KeyBindingData { KeyCode = hand[i].KeyCode };
        }

        for (var i = 0; i < foot.Length; i++)
        {
            result[i + hand.Length] = new KeyBindingData { KeyCode = foot[i].KeyCode };
        }

        return result;
    }

    internal static Dictionary<int, KeyBindingData[]> CreateDefaultGhostHandBindingSets()
    {
        return CloneSets(CreateDefaultHandBindingSets());
    }

    internal static Dictionary<int, KeyBindingData[]> CreateDefaultGhostFootBindingSets()
    {
        return CloneSets(CreateDefaultFootBindingSets());
    }

    private static Dictionary<int, KeyBindingData[]> CloneSets(Dictionary<int, KeyBindingData[]> source)
    {
        var result = new Dictionary<int, KeyBindingData[]>();
        foreach (var pair in source)
        {
            var clone = new KeyBindingData[pair.Value.Length];
            for (var i = 0; i < clone.Length; i++)
            {
                clone[i] = new KeyBindingData { KeyCode = pair.Value[i].KeyCode };
            }

            result[pair.Key] = clone;
        }

        return result;
    }

    private static KeyBindingData[] Convert(UnityEngine.KeyCode[] keyCodes)
    {
        var result = new KeyBindingData[keyCodes.Length];
        for (var i = 0; i < keyCodes.Length; i++) result[i] = new KeyBindingData { KeyCode = (int)keyCodes[i] };
        return result;
    }
}

internal sealed class AppearancePreferences
{
    public bool ChangePlanetColor { get; set; }
    public bool ChangeTileColor { get; set; }
    public bool ChangeAutoIcon { get; set; }
    public bool ChangeLogoText { get; set; }
    public string PlanetColor { get; set; } = "#CFB4F7FF";
    public string TileColor { get; set; } = "#F2DEFFFF";
    public string LogoColor { get; set; } = "#9076A1FF";
    public string LogoTitle { get; set; } = "Argon";
    public string AutoIconResourcePath { get; set; } = string.Empty;
}

internal sealed class LevelPlayRecord
{
    public int Attempts { get; set; }
    public float BestProgress { get; set; }
    public long LastPlayedUtcTicks { get; set; }
}

internal sealed class HudLayoutData
{
    public string Id { get; set; } = "default";
    public string Name { get; set; } = "기본";
    public bool BuiltInDefaultsAdded { get; set; }
    public List<HudElementLayoutData> Elements { get; set; } = new List<HudElementLayoutData>();

    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtensionData { get; set; }
}

internal sealed class HudElementLayoutData
{
    public string InstanceId { get; set; } = string.Empty;
    public string ElementId { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Anchor { get; set; } = "TopLeft";
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 200f;
    public float Height { get; set; } = 40f;
    public int Order { get; set; }
    public float Scale { get; set; } = 1f;
    public float Opacity { get; set; } = 1f;
    public JObject Configuration { get; set; } = new JObject();

    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtensionData { get; set; }
}
