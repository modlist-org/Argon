using System;
using System.Collections.Generic;
using System.Reflection;
using Argon.Storage;
using Argon.Compat;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Argon.Appearance;

/// <summary>Appearance changes are isolated from the HUD registry and preserve source values for restoration.</summary>
internal sealed class AppearanceCustomizer : IDisposable
{
    // Per-instance id: a deferred Dispose of an old instance must not unpatch a newer one.
    private static int _instanceCounter;
    private readonly string _harmonyId = "argon.appearance." + (++_instanceCounter);
    private static readonly FieldInfo? DebugTextField = typeof(scrShowIfDebug).GetField("txt", BindingFlags.Instance | BindingFlags.NonPublic);
    private static AppearanceCustomizer? _instance;
    private readonly ArgonStore _store;
    private readonly Harmony _harmony;
    private readonly Dictionary<int, PlanetSnapshot> _planets = new Dictionary<int, PlanetSnapshot>();
    private readonly Dictionary<int, Color> _tiles = new Dictionary<int, Color>();
    private readonly Dictionary<int, string> _uiText = new Dictionary<int, string>();
    private readonly Dictionary<int, Color> _uiTextColors = new Dictionary<int, Color>();
    private readonly Dictionary<int, Sprite?> _autoSprites = new Dictionary<int, Sprite?>();
    private float _lastRefreshAt;
    private string? _lastPlanetOverride;
    private string? _lastTileOverride;
    private bool _disposed;
    private bool _restoring;
    private bool _applyingTile;

    internal AppearanceCustomizer(ArgonStore store)
    {
        _store = store;
        _instance = this;
        _harmony = new Harmony(_harmonyId);
        PatchPrefix(typeof(PlanetRenderer), "SetRainbow", nameof(AllowRainbow), typeof(bool));
        PatchPrefix(typeof(PlanetRenderer), "SetPlanetColor", nameof(OverridePlanetColor), typeof(Color));
        PatchPrefix(typeof(PlanetRenderer), "SetCoreColor", nameof(OverridePlanetColor), typeof(Color));
        PatchPrefix(typeof(PlanetRenderer), "SetTailColor", nameof(OverridePlanetColor), typeof(Color));
        PatchPrefix(typeof(PlanetRenderer), "SetRingColor", nameof(OverridePlanetColor), typeof(Color));
        PatchPrefix(typeof(PlanetRenderer), "SetFaceColor", nameof(OverridePlanetColor), typeof(Color));
        Patch(typeof(PlanetRenderer), "SetColor", nameof(OnPlanetChanged), typeof(PlanetColor), typeof(bool));
        Patch(typeof(PlanetRenderer), "LoadPlanetColor", nameof(OnPlanetChanged), typeof(bool));
        Patch(typeof(scrFloor), "SetTileColor", nameof(OnTileChanged), typeof(Color));
        Patch(typeof(scrFloor), "ColorFloor", nameof(OnTileChanged));
        PatchPrefix(typeof(scrLogoText), "UpdateColors", nameof(OverrideLogoColors));
        PatchPrefix(typeof(scrLogoText), "LateUpdate", nameof(OverrideLogoColors));
        PatchPrefix(typeof(scrShowIfDebug), "Update", nameof(HideDebugText));
        ApplyCurrent();
    }

    internal static void Apply(AppearancePreferences _)
    {
        _instance?.ApplyCurrent();
    }

    internal static void RestoreOriginals()
    {
        var instance = _instance;
        if (instance == null)
        {
            return;
        }

        var appearance = instance._store.Document.Preferences.Appearance;
        appearance.ChangePlanetColor = false;
        appearance.ChangeTileColor = false;
        appearance.ChangeAutoIcon = false;
        appearance.ChangeLogoText = false;
        instance.ApplyCurrent();
        instance._store.Save();
    }

    internal void Tick()
    {
        if (_disposed || Time.unscaledTime - _lastRefreshAt < 0.5f)
        {
            return;
        }

        _lastRefreshAt = Time.unscaledTime;
        ApplyCurrent();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _restoring = true;
        RestoreCapturedValues();
        _restoring = false;
        _harmony.UnpatchAll(_harmonyId);
        if (ReferenceEquals(_instance, this)) _instance = null;
    }

    private void ApplyCurrent()
    {
        if (_disposed || _restoring)
        {
            return;
        }

        var appearance = _store.Document.Preferences.Appearance;
        if (appearance.ChangePlanetColor)
        {
            var color = ParseColor(appearance.PlanetColor, new Color(0.8125f, 0.707f, 0.969f, 1f));
            var colorKey = ColorUtility.ToHtmlStringRGBA(color);
            var force = colorKey != _lastPlanetOverride;
            _lastPlanetOverride = colorKey;
            foreach (var renderer in Resources.FindObjectsOfTypeAll<PlanetRenderer>()) ApplyPlanet(renderer, color, force);
        }
        else
        {
            RestorePlanets();
            if (_lastPlanetOverride != null && scrLogoText.instance != null)
            {
                try { scrLogoText.instance.UpdateColors(); }
                catch (Exception exception) { Debug.LogWarning($"[Argon] Could not restore logo colors: {exception.Message}"); }
            }
            _lastPlanetOverride = null;
        }

        if (appearance.ChangeTileColor)
        {
            var color = ParseColor(appearance.TileColor, new Color(0.949f, 0.871f, 1f, 1f));
            var colorKey = ColorUtility.ToHtmlStringRGBA(color);
            var force = colorKey != _lastTileOverride;
            _lastTileOverride = colorKey;
            var levelMaker = ADOBase.lm;
            if (levelMaker != null && levelMaker.listFloors != null)
            {
                foreach (var floor in levelMaker.listFloors)
                {
                    if (IsBeatTile(floor)) ApplyTile(floor, color, null, force);
                }
            }
        }
        else
        {
            RestoreTiles();
            _lastTileOverride = null;
        }

        ApplyTitle(appearance);
        ApplyAutoSprite(appearance);
    }

    private static void OnPlanetChanged(object __instance)
    {
        var instance = _instance;
        if (instance == null || instance._restoring || !instance._store.Document.Preferences.Appearance.ChangePlanetColor)
        {
            return;
        }

        instance.ApplyPlanet(__instance as PlanetRenderer, ParseColor(instance._store.Document.Preferences.Appearance.PlanetColor, Color.white), true);
    }

    private static bool OverrideLogoColors(object __instance)
    {
        if (!ShouldOverridePlanetColor()) return true;
        if (__instance is scrLogoText logo)
        {
            var color = ParseColor(_instance!._store.Document.Preferences.Appearance.PlanetColor, Color.white);
            logo.ColorLogo(color, true);
            logo.ColorLogo(color, false);
        }

        return false;
    }

    private static bool AllowRainbow(bool enabled)
    {
        return !enabled || !ShouldOverridePlanetColor();
    }

    private static void OverridePlanetColor(object __instance, ref Color color)
    {
        var instance = _instance;
        if (instance != null && !instance._restoring && instance._store.Document.Preferences.Appearance.ChangePlanetColor &&
            !GameApi.CoopMode && __instance is PlanetRenderer renderer && IsActiveSceneObject(renderer.gameObject) &&
            renderer.GetComponentInParent<scrPlanet>() != null)
        {
            color = ParseColor(instance._store.Document.Preferences.Appearance.PlanetColor, Color.white);
        }
    }

    private static bool ShouldOverridePlanetColor()
    {
        var instance = _instance;
        return instance != null && !instance._restoring &&
               instance._store.Document.Preferences.Appearance.ChangePlanetColor && !GameApi.CoopMode;
    }

    private static void OnTileChanged(object __instance, object[] __args)
    {
        var instance = _instance;
        if (instance == null || instance._restoring || instance._applyingTile || !instance._store.Document.Preferences.Appearance.ChangeTileColor)
        {
            return;
        }

        var original = __args != null && __args.Length > 0 && __args[0] is Color color ? color : (Color?)null;
        instance.ApplyTile(__instance as scrFloor,
            ParseColor(instance._store.Document.Preferences.Appearance.TileColor, Color.white), original, true);
    }

    private void ApplyPlanet(PlanetRenderer? renderer, Color color, bool force = false)
    {
        if (renderer == null || !IsActiveSceneObject(renderer.gameObject) ||
            renderer.GetComponentInParent<scrPlanet>() == null || GameApi.CoopMode)
        {
            return;
        }

        var id = renderer.GetInstanceID();
        var alreadyCaptured = _planets.ContainsKey(id);
        if (!alreadyCaptured)
        {
            var source = renderer.planetColor;
            var planet = renderer.GetComponentInParent<scrPlanet>();
            _planets.Add(id, new PlanetSnapshot(renderer, source, planet != null && planet.isRed));
        }

        if (alreadyCaptured && !force) return;

        try
        {
            renderer.DisableAllSpecialPlanets();
            renderer.EnableCustomColor();
            renderer.SetPlanetColor(color);
            renderer.SetTailColor(color);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Argon] Could not recolor a planet renderer: {exception.Message}");
        }
    }

    private void ApplyTile(scrFloor? floor, Color color, Color? original, bool force = false)
    {
        if (floor == null || floor.tag != "Beat" || floor.floorRenderer == null)
        {
            return;
        }

        var id = floor.GetInstanceID();
        var alreadyCaptured = _tiles.ContainsKey(id);
        if (!alreadyCaptured)
        {
            _tiles.Add(id, original ?? floor.floorRenderer.color);
        }
        else if (original.HasValue)
        {
            _tiles[id] = original.Value;
        }

        if (alreadyCaptured && !force && original == null) return;

        try
        {
            _applyingTile = true;
            floor.SetTileColor(color);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Argon] Could not recolor a tile: {exception.Message}");
        }
        finally
        {
            _applyingTile = false;
        }
    }

    private void ApplyTitle(AppearancePreferences appearance)
    {
        if (!appearance.ChangeLogoText)
        {
            RestoreTexts();
            return;
        }

        var controller = scrController.instance;
        var title = controller != null ? controller.txtLevelName : null;
        if (title == null)
        {
            return;
        }

        var id = title.GetInstanceID();
        if (!_uiText.ContainsKey(id))
        {
            _uiText.Add(id, title.text);
            _uiTextColors.Add(id, title.color);
        }
        title.text = appearance.LogoTitle;
        var color = ParseColor(appearance.LogoColor, Color.white);
        title.color = color;
    }

    private void ApplyAutoSprite(AppearancePreferences appearance)
    {
        if (!appearance.ChangeAutoIcon || string.IsNullOrWhiteSpace(appearance.AutoIconResourcePath))
        {
            RestoreSprites();
            return;
        }

        var sprite = Resources.Load<Sprite>(appearance.AutoIconResourcePath);
        if (sprite == null)
        {
            RestoreSprites();
            return;
        }

        foreach (var renderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
        {
            if (!IsActiveSceneObject(renderer.gameObject) || renderer.name.IndexOf("auto", StringComparison.OrdinalIgnoreCase) < 0) continue;
            var id = renderer.GetInstanceID();
            if (!_autoSprites.ContainsKey(id)) _autoSprites.Add(id, renderer.sprite);
            renderer.sprite = sprite;
        }

        foreach (var image in Resources.FindObjectsOfTypeAll<Image>())
        {
            if (!IsActiveSceneObject(image.gameObject) || image.name.IndexOf("auto", StringComparison.OrdinalIgnoreCase) < 0) continue;
            var id = image.GetInstanceID();
            if (!_autoSprites.ContainsKey(id)) _autoSprites.Add(id, image.sprite);
            image.sprite = sprite;
        }
    }

    private static bool HideDebugText(object __instance)
    {
        var instance = _instance;
        if (instance == null || !instance._store.Document.Preferences.Hud.HideDebugText)
        {
            return true;
        }

        var text = DebugTextField?.GetValue(__instance) as Text;
        if (text != null) text.enabled = false;
        return false;
    }

    private void RestoreCapturedValues()
    {
        RestorePlanets();
        RestoreTiles();
        RestoreTexts();
        RestoreSprites();
    }

    private void RestorePlanets()
    {
        foreach (var pair in _planets)
        {
            var snapshot = pair.Value;
            if (snapshot.Renderer == null) continue;
            try
            {
                snapshot.Renderer.SetColor(snapshot.Color, snapshot.IsRed);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Argon] Could not restore a planet color: {exception.Message}");
            }
        }

        _planets.Clear();
    }

    private void RestoreTiles()
    {
        foreach (var floor in Resources.FindObjectsOfTypeAll<scrFloor>())
        {
            if (floor == null || floor.floorRenderer == null || !_tiles.TryGetValue(floor.GetInstanceID(), out var color)) continue;
            try
            {
                floor.SetTileColor(color);
            }
            catch
            {
                // Tile may be being destroyed as a scene changes.
            }
        }

        _tiles.Clear();
    }

    private void RestoreTexts()
    {
        var controller = scrController.instance;
        var title = controller != null ? controller.txtLevelName : null;
        if (title != null && _uiText.TryGetValue(title.GetInstanceID(), out var text))
        {
            title.text = text;
            if (_uiTextColors.TryGetValue(title.GetInstanceID(), out var color)) title.color = color;
        }
        _uiText.Clear();
        _uiTextColors.Clear();
    }

    private void RestoreSprites()
    {
        foreach (var renderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
        {
            if (renderer != null && _autoSprites.TryGetValue(renderer.GetInstanceID(), out var sprite)) renderer.sprite = sprite;
        }

        foreach (var image in Resources.FindObjectsOfTypeAll<Image>())
        {
            if (image != null && _autoSprites.TryGetValue(image.GetInstanceID(), out var sprite)) image.sprite = sprite;
        }

        _autoSprites.Clear();
    }

    private void Patch(Type type, string name, string callback, params Type[] arguments)
    {
        try
        {
            var original = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, arguments, null);
            var postfix = typeof(AppearanceCustomizer).GetMethod(callback, BindingFlags.Static | BindingFlags.NonPublic);
            if (original == null || postfix == null) return;
            _harmony.Patch(original, postfix: new HarmonyMethod(postfix));
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Argon] Appearance patch '{type.Name}.{name}' is unavailable: {exception.Message}");
        }
    }

    private void PatchPrefix(Type type, string name, string callback, params Type[] arguments)
    {
        try
        {
            var original = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, arguments, null);
            var prefix = typeof(AppearanceCustomizer).GetMethod(callback, BindingFlags.Static | BindingFlags.NonPublic);
            if (original == null || prefix == null) return;
            _harmony.Patch(original, prefix: new HarmonyMethod(prefix));
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Argon] Appearance prefix '{type.Name}.{name}' is unavailable: {exception.Message}");
        }
    }

    private static bool IsActiveSceneObject(GameObject gameObject)
    {
        return gameObject != null && gameObject.scene.IsValid() && gameObject.activeInHierarchy;
    }

    private static bool IsBeatTile(scrFloor? floor)
    {
        return floor != null && floor.tag == "Beat";
    }

    private static Color ParseColor(string text, Color fallback)
    {
        var value = text.StartsWith("#", StringComparison.Ordinal) ? text : "#" + text;
        return ColorUtility.TryParseHtmlString(value, out var color) ? color : fallback;
    }

    private readonly struct PlanetSnapshot
    {
        internal PlanetRenderer Renderer { get; }
        internal PlanetColor Color { get; }
        internal bool IsRed { get; }

        internal PlanetSnapshot(PlanetRenderer renderer, PlanetColor color, bool isRed)
        {
            Renderer = renderer;
            Color = color;
            IsRed = isRed;
        }
    }
}
