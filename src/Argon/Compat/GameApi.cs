using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Argon.Compat;

/// <summary>
/// Every game member whose name, owner or existence varies between ADOFAI releases is read here.
/// Current builds keep hits on per-player <c>scrMarginTracker</c>s (reached via <c>scrPlayer</c>);
/// legacy (pre-3.0) builds keep them on <c>scrMistakesManager</c> and the planets on the controller.
/// Structure follows Quartz (GPL-3.0) Compat/Game/GameApi.cs.
/// </summary>
internal static class GameApi
{
    internal static readonly Type? MarginTrackerType = Refl.Type("scrMarginTracker");
    internal static readonly Type? PlayerType = Refl.Type("scrPlayer");
    internal static readonly Type TrackerType = MarginTrackerType ?? typeof(scrMistakesManager);
    internal static bool IsLegacy => MarginTrackerType == null;

    private static readonly Refl.Member ControllerPlayerOne = new Refl.Member(typeof(scrController), "playerOne");
    private static readonly Refl.Member PlayerMarginTracker = new Refl.Member(PlayerType, "marginTracker");
    private static readonly Refl.Member PlayerPlanetary = new Refl.Member(PlayerType, "planetarySystem");
    private static readonly Refl.Reader<scrController, PlanetarySystem> ControllerPlanetary =
        new Refl.Reader<scrController, PlanetarySystem>("planetarySystem");
    private static readonly Refl.Reader<scrController, scrMistakesManager> ControllerMistakes =
        new Refl.Reader<scrController, scrMistakesManager>("mistakesManager");
    private static readonly Refl.Reader<scrController, int> ControllerSeq = new Refl.Reader<scrController, int>("currentSeqID");
    private static readonly Refl.Reader<scrController, scrFloor> ControllerCurrFloor = new Refl.Reader<scrController, scrFloor>("currFloor", "currentFloor");
    private static readonly Refl.Reader<scrController, scrFloor> ControllerFirstFloor = new Refl.Reader<scrController, scrFloor>("firstFloor");
    private static readonly Refl.Reader<scrController, float> ControllerPercent = new Refl.Reader<scrController, float>("percentComplete");
    private static readonly Refl.Reader<scrController, string> ControllerLevelName = new Refl.Reader<scrController, string>("levelName");
    private static readonly Refl.Reader<scrController, bool> ControllerFromCheckpoint = new Refl.Reader<scrController, bool>("startedFromCheckpoint");
    private static readonly Refl.Reader<scrController, bool> ControllerGameworld = new Refl.Reader<scrController, bool>("gameworld");
    private static readonly Refl.Reader<scrController, double> ControllerSpeed = new Refl.Reader<scrController, double>("speed");
    private static readonly Refl.Reader<scrController, bool> ControllerIsCw = new Refl.Reader<scrController, bool>("isCW");
    private static readonly Refl.Member ControllerState = new Refl.Member(typeof(scrController), "state", "currentState");
    private static readonly Refl.Member CurrentWorldString = new Refl.Member(typeof(scrController), "currentWorldString");
    private static readonly Refl.Member CheckpointsUsedMember = new Refl.Member(typeof(scrController), "checkpointsUsed");
    private static readonly Refl.Member CoopModeMember = new Refl.Member(typeof(scrController), "coopMode");
    private static readonly Refl.Member CheckpointNum = new Refl.Member(typeof(GCS), "checkpointNum");
    private static readonly Refl.Member PlayerHitFloorsMember = new Refl.Member(typeof(scrLevelMaker), "PlayerHitFloors");

    private static readonly Refl.Reader<PlanetarySystem, double> PlanetarySpeed = new Refl.Reader<PlanetarySystem, double>("speed");
    private static readonly Refl.Reader<PlanetarySystem, bool> PlanetaryIsCw = new Refl.Reader<PlanetarySystem, bool>("isCW");
    private static readonly Refl.Reader<PlanetarySystem, scrPlanet> PlanetaryChosen = new Refl.Reader<PlanetarySystem, scrPlanet>("chosenPlanet");
    private static readonly Refl.Member PlanetPlanetary = new Refl.Member(typeof(scrPlanet), "planetarySystem");

    private static readonly Refl.Reader<scrMistakesManager, float> PercentAccReader = new Refl.Reader<scrMistakesManager, float>("percentAcc");
    private static readonly Refl.Reader<scrMistakesManager, float> PercentXAccReader = new Refl.Reader<scrMistakesManager, float>("percentXAcc");
    private static readonly Refl.Member TrackerCounts = new Refl.Member(TrackerType, "hitMarginsCount");
    private static readonly Refl.Member TrackerMargins = new Refl.Member(TrackerType, "hitMargins");
    private static readonly Refl.Member TrackerXScore = new Refl.Member(TrackerType, "xScore");
    private static readonly Refl.Member TrackerMaxXScore = new Refl.Member(TrackerType, "maxXScore");
    private static readonly Refl.Member TrackerPlayerHits = new Refl.Member(TrackerType, "playerHitMarginCount");
    private static readonly Refl.Member TrackerMaxXAcc = new Refl.Member(TrackerType, "maxPossibleXAcc");
    private static readonly Refl.Member ManagerMaxXAcc = new Refl.Member(typeof(scrMistakesManager), "maxPossibleXAcc");
    private static readonly MethodInfo? TrackerGetDeaths = Refl.Method(TrackerType, "GetDeaths", 0);

    private static readonly Refl.Member LevelDataHash = new Refl.Member(typeof(ADOFAI.LevelData), "Hash");
    private static readonly Dictionary<int, string> StateNames = new Dictionary<int, string>();

    // ---- Harmony targets ----
    internal static MethodBase? AddHitTarget => Refl.Method(TrackerType, "AddHit", 1);
    internal static MethodBase? AddHitsTarget => Refl.Method(TrackerType, "AddHits", 2);
    internal static MethodBase? RevertTarget => Refl.Method(TrackerType, "RevertToLastCheckpoint", 0);
    internal static MethodBase? SwitchChosenTarget => Refl.Method(typeof(scrPlanet), "SwitchChosen");
    internal static MethodBase? PlayTarget => Refl.Method(typeof(scnGame), "Play");
    internal static MethodBase? StartLoadingSceneTarget => Refl.Method(typeof(scrController), "StartLoadingScene");

    // ---- controller ----
    internal static object? PlayerOne(scrController? controller) =>
        controller == null ? null : ControllerPlayerOne.Get(controller);

    /// <summary>Player one's hit tracker: scrMarginTracker on current builds, scrMistakesManager on legacy.</summary>
    internal static object? Tracker(scrController? controller)
    {
        if (controller == null) return null;
        if (IsLegacy) return MistakesManager(controller);
        var player = PlayerOne(controller);
        return player == null ? null : PlayerMarginTracker.Get(player);
    }

    internal static scrMistakesManager? MistakesManager(scrController? controller) =>
        ControllerMistakes.Get(controller, null!);

    internal static PlanetarySystem? Planetary(scrController? controller)
    {
        if (controller == null) return null;
        var player = PlayerOne(controller);
        if (player != null && PlayerPlanetary.Get(player) is PlanetarySystem system) return system;
        return ControllerPlanetary.Get(controller, null!);
    }

    internal static PlanetarySystem? Planetary(scrPlanet? planet)
    {
        if (planet == null) return null;
        return PlanetPlanetary.Get(planet) as PlanetarySystem ?? Planetary(scrController.instance);
    }

    internal static double PlanetSpeed(scrController? controller)
    {
        var system = Planetary(controller);
        if (system != null && PlanetarySpeed.Exists) return PlanetarySpeed.Get(system, 1d);
        return ControllerSpeed.Get(controller, 1d);
    }

    internal static double SystemSpeed(PlanetarySystem? system, scrController? controller) =>
        system != null && PlanetarySpeed.Exists ? PlanetarySpeed.Get(system, 1d) : ControllerSpeed.Get(controller, 1d);

    internal static bool IsClockwise(PlanetarySystem? system)
    {
        if (system != null && PlanetaryIsCw.Exists) return PlanetaryIsCw.Get(system, true);
        return ControllerIsCw.Get(scrController.instance, true);
    }

    internal static float ChosenPlanetAngle(scrController? controller)
    {
        var planet = PlanetaryChosen.Get(Planetary(controller), null!);
        return planet != null ? (float)planet.angle : 0f;
    }

    internal static int CurrentSeqId(scrController? controller) => ControllerSeq.Get(controller, 0);
    internal static scrFloor? CurrentFloor(scrController? controller) =>
        ControllerCurrFloor.Get(controller, null!) ?? ControllerFirstFloor.Get(controller, null!);
    internal static float PercentComplete(scrController? controller) => ControllerPercent.Get(controller, 0f);
    internal static string LevelName(scrController? controller) => ControllerLevelName.Get(controller, null!) ?? string.Empty;
    internal static bool StartedFromCheckpoint(scrController? controller) => ControllerFromCheckpoint.Get(controller, false);
    internal static bool IsGameworld(scrController? controller) => ControllerGameworld.Get(controller, false);
    internal static string WorldString => CurrentWorldString.Get(null) as string ?? string.Empty;
    internal static int CheckpointsUsed => CheckpointsUsedMember.Get(null, 0);
    internal static bool CoopMode => CoopModeMember.Get(null, false);
    internal static int CheckpointStart => CheckpointNum.Get(null, 0);

    /// <summary>The controller state's enum name ("PlayerControl", "Fail", "Won"…), cached per value.</summary>
    internal static string StateName(scrController? controller)
    {
        if (!(ControllerState.Get(controller) is Enum state)) return string.Empty;
        var value = Convert.ToInt32(state);
        if (!StateNames.TryGetValue(value, out var name))
        {
            name = state.ToString();
            StateNames[value] = name;
        }

        return name;
    }

    internal static int PlayerHitFloorCount(scrLevelMaker? levelMaker, int fallback) =>
        levelMaker != null && PlayerHitFloorsMember.Get(levelMaker) is ICollection floors ? floors.Count : fallback;

    // ---- accuracy / tracker ----
    internal static float PercentAcc(scrMistakesManager? manager) => PercentAccReader.Get(manager, 0f);
    internal static float PercentXAcc(scrMistakesManager? manager) => PercentXAccReader.Get(manager, 0f);

    internal static float MaxPossibleXAcc(object? tracker, scrMistakesManager? manager, float fallback)
    {
        if (tracker != null && TrackerMaxXAcc.Exists) return TrackerMaxXAcc.Get(tracker, fallback);
        return manager != null && ManagerMaxXAcc.Exists ? ManagerMaxXAcc.Get(manager, fallback) : fallback;
    }

    internal static int[]? HitMarginCounts(object? tracker) => tracker == null ? null : TrackerCounts.Get(tracker) as int[];
    internal static int XScore(object? tracker) => tracker == null ? 0 : TrackerXScore.Get(tracker, 0);
    internal static int MaxXScore(object? tracker) => tracker == null ? 0 : TrackerMaxXScore.Get(tracker, 0);
    internal static bool HasXScore => TrackerXScore.Exists;

    internal static int PlayerHitCount(object? tracker, int fallback) =>
        tracker != null && TrackerPlayerHits.Exists ? TrackerPlayerHits.Get(tracker, fallback) : fallback;

    internal static int HitMarginTotal(object? tracker) =>
        tracker != null && TrackerMargins.Get(tracker) is ICollection margins ? margins.Count : -1;

    internal static int GetDeaths(object? tracker) =>
        tracker == null ? 0 : Refl.Invoke(TrackerGetDeaths, tracker) as int? ?? 0;

    // ---- level data ----
    internal static string? LevelHash(ADOFAI.LevelData? levelData) =>
        levelData == null ? null : LevelDataHash.Get(levelData) as string;

    internal static void LogDetectedVersion()
    {
        Debug.Log("[Argon] Game API: " + (IsLegacy ? "legacy (pre-3.0, scrMistakesManager)" : "current (scrMarginTracker)"));
    }
}
