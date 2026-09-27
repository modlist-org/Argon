using System;
using System.Reflection;
using Argon.Compat;
using Argon.Storage;
using HarmonyLib;
using UnityEngine;

namespace Argon.Integration;

/// <summary>Small Harmony boundary for judgement and timing events; all HUD reads remain in GameStateSource.</summary>
internal sealed class GameEventBridge : IDisposable
{
    // Each bridge gets its own Harmony id so a late Dispose of a previous instance
    // (deferred Object.Destroy during a same-frame off/on toggle) cannot unpatch the new one.
    private static int _instanceCounter;
    private readonly string _harmonyId;
    private readonly Harmony _harmony;
    private readonly HudDisplayPreferences _preferences;
    private static HudDisplayPreferences? _activePreferences;
    private static float _timingTotal;
    private static int _timingSamples;
    private static double _pendingTimingMs;
    private static bool _pendingTimingValid;

    internal static string LastJudgement { get; private set; } = string.Empty;
    internal static float LastTimingMilliseconds { get; private set; }
    internal static float AverageTimingMilliseconds => _timingSamples == 0 ? 0f : _timingTotal / _timingSamples;
    internal static int Combo { get; private set; }
    internal static int PurePerfectCombo { get; private set; }
    internal static int ComboTier { get; private set; }

    internal GameEventBridge(HudDisplayPreferences preferences)
    {
        _preferences = preferences;
        _activePreferences = preferences;
        _harmonyId = "argon.game-events." + (++_instanceCounter);
        _harmony = new Harmony(_harmonyId);
        // Targets resolve by name on scrMarginTracker (current) or scrMistakesManager (legacy).
        Patch(GameApi.AddHitTarget, "AddHit", nameof(OnHit), prefix: false);
        // Failed floors bypass AddHit (AddHits(FailedFloor, n)); they still break the combo.
        Patch(GameApi.AddHitsTarget, "AddHits", nameof(OnHits), prefix: false);
        Patch(GameApi.RevertTarget, "RevertToLastCheckpoint", nameof(OnRevert), prefix: false);
        Patch(GameApi.SwitchChosenTarget, "SwitchChosen", nameof(OnSwitchChosen), prefix: true);
        Patch(GameApi.PlayTarget, "Play", nameof(OnRunBoundary), prefix: false);
        Patch(GameApi.StartLoadingSceneTarget, "StartLoadingScene", nameof(OnRunBoundary), prefix: false);
    }

    public void Dispose()
    {
        _harmony.UnpatchAll(_harmonyId);
        if (ReferenceEquals(_activePreferences, _preferences))
        {
            _activePreferences = null;
            ResetSession();
        }
    }

    internal static void ResetSession()
    {
        LastJudgement = string.Empty;
        LastTimingMilliseconds = 0f;
        _timingTotal = 0f;
        _timingSamples = 0;
        _pendingTimingValid = false;
        Combo = 0;
        PurePerfectCombo = 0;
        ComboTier = 0;
    }

    private void Patch(MethodBase? original, string originalName, string patchName, bool prefix)
    {
        try
        {
            var patch = typeof(GameEventBridge).GetMethod(patchName, BindingFlags.NonPublic | BindingFlags.Static);
            if (original == null || patch == null)
            {
                Debug.LogWarning($"[Argon] Optional game event patch '{originalName}' is unavailable on this game version.");
                return;
            }

            var method = new HarmonyMethod(patch);
            if (prefix) _harmony.Patch(original, prefix: method);
            else _harmony.Patch(original, postfix: method);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Argon] Could not patch '{originalName}': {exception.Message}");
        }
    }

    private static bool IsPlayerOne(object tracker)
    {
        // Unknown ownership: accept rather than drop every hit.
        var playerOne = GameApi.Tracker(scrController.instance);
        return playerOne == null || ReferenceEquals(playerOne, tracker);
    }

    private static void OnRunBoundary() => ResetSession();

    private static void OnRevert(object __instance)
    {
        if (!IsPlayerOne(__instance)) return;
        Combo = 0;
        PurePerfectCombo = 0;
        ComboTier = 0;
        _pendingTimingValid = false;
    }

    private static void OnHits(object __instance, HitMargin __0, int __1)
    {
        if (__1 > 0) OnHit(__instance, __0);
    }

    // Same formula the game uses for its own ms readout, captured before SwitchChosen advances the planet.
    private static void OnSwitchChosen(scrPlanet __instance)
    {
        _pendingTimingValid = false;
        try
        {
            var controller = scrController.instance;
            if (controller == null || !GameApi.IsGameworld(controller) || RDC.auto) return;
            var system = GameApi.Planetary(__instance);
            var conductor = scrConductor.instance;
            if (conductor == null || conductor.song == null) return;
            var rate = Math.PI * conductor.bpm * GameApi.SystemSpeed(system, controller) * conductor.song.pitch;
            if (Math.Abs(rate) < 1e-9) return;
            _pendingTimingMs = (__instance.cachedAngle - __instance.targetExitAngle) * (GameApi.IsClockwise(system) ? 1 : -1) * 60000.0 / rate;
            _pendingTimingValid = !double.IsNaN(_pendingTimingMs) && !double.IsInfinity(_pendingTimingMs);
        }
        catch
        {
            // Optional readout; judgements still flow through AddHit.
        }
    }

    private static void OnHit(object __instance, HitMargin __0)
    {
        if (!IsPlayerOne(__instance)) return;
        var margin = __0;
        var kind = HitKinds.Of(margin);
        LastJudgement = margin.ToString();
        ConsumeTiming(kind);

        var preferences = _activePreferences;
        switch (kind)
        {
            case HitKind.Perfect when HitKinds.IsPurePerfect(margin):
                Combo++;
                PurePerfectCombo++;
                return;
            case HitKind.Perfect when preferences != null && preferences.ComboMinimumTier >= 1:
                Combo++;
                PurePerfectCombo = 0;
                ComboTier = Math.Max(ComboTier, 1);
                return;
            case HitKind.EarlyPerfect:
            case HitKind.LatePerfect:
                if (preferences != null && preferences.ComboMinimumTier >= 2)
                {
                    Combo++;
                    PurePerfectCombo = 0;
                    ComboTier = 2;
                    return;
                }

                break;
            case HitKind.Auto:
                if (preferences != null && preferences.CountAutoInCombo) Combo++;
                // Auto/midspin never break a combo.
                return;
        }

        Combo = 0;
        PurePerfectCombo = 0;
        ComboTier = 0;
    }

    private static void ConsumeTiming(HitKind kind)
    {
        if (!_pendingTimingValid) return;
        _pendingTimingValid = false;
        switch (kind)
        {
            case HitKind.EarlyPerfect:
            case HitKind.Perfect:
            case HitKind.LatePerfect:
            case HitKind.VeryEarly:
            case HitKind.VeryLate:
                break;
            default:
                return; // misses, overloads, multipresses and auto have no meaningful offset
        }

        var milliseconds = (float)_pendingTimingMs;
        LastTimingMilliseconds = milliseconds;
        _timingTotal += milliseconds;
        _timingSamples++;
    }
}
