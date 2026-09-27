using System;
using System.Reflection;
using Argon.Storage;
using HarmonyLib;
using UnityEngine;

namespace Argon.Integration;

/// <summary>Small Harmony boundary for judgement and timing events; all HUD reads remain in GameStateSource.</summary>
internal sealed class GameEventBridge : IDisposable
{
    private const string HarmonyId = "argon.game-events";
    private readonly Harmony _harmony;
    private readonly HudDisplayPreferences _preferences;
    private static float _timingTotal;
    private static int _timingSamples;

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
        _harmony = new Harmony(HarmonyId);
        Patch(typeof(scrMarginTracker), "AddHit", nameof(OnHit), typeof(HitMargin));
        Patch(typeof(scrMistakesManager), "AddHit", nameof(OnHit), typeof(HitMargin));
        Patch(typeof(scrMisc), "GetHitMarginInSec", nameof(OnTimingSeconds));
        Patch(typeof(scrMisc), "GetHitMarginInDeg", nameof(OnTimingDegrees));
    }

    public void Dispose()
    {
        _harmony.UnpatchAll(HarmonyId);
        if (ReferenceEquals(_activePreferences, _preferences))
        {
            _activePreferences = null;
        }
        LastJudgement = string.Empty;
        LastTimingMilliseconds = 0f;
        _timingTotal = 0f;
        _timingSamples = 0;
        Combo = 0;
        PurePerfectCombo = 0;
        ComboTier = 0;
    }

    internal static void ResetSession()
    {
        LastJudgement = string.Empty;
        LastTimingMilliseconds = 0f;
        _timingTotal = 0f;
        _timingSamples = 0;
        Combo = 0;
        PurePerfectCombo = 0;
        ComboTier = 0;
    }

    private void Patch(Type type, string originalName, string postfixName, params Type[] argumentTypes)
    {
        try
        {
            var original = type.GetMethod(
                originalName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static,
                null,
                argumentTypes,
                null);
            var postfix = typeof(GameEventBridge).GetMethod(postfixName, BindingFlags.NonPublic | BindingFlags.Static);
            if (original == null || postfix == null)
            {
                Debug.LogWarning($"[Argon] Optional game event patch '{type.Name}.{originalName}' is unavailable.");
                return;
            }

            _harmony.Patch(original, postfix: new HarmonyMethod(postfix));
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Argon] Could not patch '{type.Name}.{originalName}': {exception.Message}");
        }
    }

    private static void OnHit(object[] __args)
    {
        if (__args == null || __args.Length == 0 || __args[0] == null)
        {
            return;
        }

        var judgement = __args[0].ToString() ?? string.Empty;
        LastJudgement = judgement;
        var isPurePerfect = judgement == "XPerfect" || judgement == "PurePerfect";
        var isSignedPerfect = judgement == "PerfectMinus" || judgement == "PerfectPlus";
        var isNearPerfect = judgement == "EarlyPerfect" || judgement == "LatePerfect";
        var isAuto = judgement == "Auto";
        var isNeutral = judgement == "Midspin";

        if (isPurePerfect)
        {
            Combo++;
            PurePerfectCombo++;
            return;
        }

        if (isSignedPerfect && _activePreferences != null && _activePreferences.ComboMinimumTier >= 1)
        {
            Combo++;
            PurePerfectCombo = 0;
            ComboTier = Math.Max(ComboTier, 1);
            return;
        }

        if (isNearPerfect && _activePreferences != null && _activePreferences.ComboMinimumTier >= 2)
        {
            Combo++;
            PurePerfectCombo = 0;
            ComboTier = 2;
            return;
        }

        if (isAuto && _activePreferences != null && _activePreferences.CountAutoInCombo)
        {
            Combo++;
            return;
        }

        if (!isAuto && !isNeutral)
        {
            Combo = 0;
            PurePerfectCombo = 0;
            ComboTier = 0;
        }
    }

    private static HudDisplayPreferences? _activePreferences;

    private static void OnTimingSeconds(object[] __args)
    {
        if (__args == null || __args.Length < 2 || __args[1] == null)
        {
            return;
        }

        RecordTiming(Convert.ToSingle(__args[1]) * 1000f);
    }

    private static void OnTimingDegrees(object[] __args)
    {
        if (__args == null || __args.Length < 7)
        {
            return;
        }

        try
        {
            var hitAngle = Convert.ToSingle(__args[1]);
            var referenceAngle = Convert.ToSingle(__args[2]);
            var clockwise = Convert.ToBoolean(__args[3]);
            var bpm = Math.Max(0.001f, Convert.ToSingle(__args[4]));
            var pitch = Math.Max(0.001f, Convert.ToSingle(__args[5]));
            var signedDegrees = (hitAngle - referenceAngle) * (clockwise ? 1f : -1f) * 57.29578f;
            RecordTiming(signedDegrees / 180f / bpm / pitch * 60000f);
        }
        catch
        {
            // A future ADOFAI signature may change argument types; the judgement patch still works.
        }
    }

    private static void RecordTiming(float milliseconds)
    {
        if (float.IsNaN(milliseconds) || float.IsInfinity(milliseconds))
        {
            return;
        }

        LastTimingMilliseconds = milliseconds;
        _timingTotal += milliseconds;
        _timingSamples++;
    }
}
