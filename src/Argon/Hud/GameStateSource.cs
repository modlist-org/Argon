using System;
using System.Collections.Generic;
using System.Globalization;
using Argon.Integration;
using Argon.Storage;
using UnityEngine;

namespace Argon.Hud;

internal struct GameSnapshot
{
    internal bool InGame;
    internal bool IsAuto;
    internal bool IsDead;
    internal string LevelName;
    internal string LevelAuthor;
    internal string State;
    internal string Judgement;
    internal int Sequence;
    internal int TotalTiles;
    internal int CheckpointsUsed;
    internal int CheckpointCount;
    internal int Attempts;
    internal int Deaths;
    internal int StartSequence;
    internal int Combo;
    internal int PurePerfectCombo;
    internal int ComboTier;
    internal int XScore;
    internal int MaxXScore;
    internal int PotentialXScore;
    internal float Progress;
    internal float Accuracy;
    internal float AbsoluteAccuracy;
    internal float PotentialAccuracy;
    internal float XAccuracy;
    internal float PotentialXAccuracy;
    internal float Bpm;
    internal float CurrentBpm;
    internal float Kps;
    internal int PseudoBeatCount;
    internal bool IsPseudoBpm;
    internal float TimingMilliseconds;
    internal float AverageTimingMilliseconds;
    internal float TimingScale;
    internal float MusicSeconds;
    internal float MusicDuration;
    internal float MapSeconds;
    internal float MapDuration;
    internal float PlanetAngle;
    internal float BestProgress;
    internal bool HasMusic;
    internal bool HasJudgement;
}

/// <summary>Collects game-version-dependent reads in one place and keeps HUD elements game-agnostic.</summary>
internal static class GameStateSource
{
    private static GameSnapshot _current;
    private static bool _sessionActive;
    private static string _sessionKey = string.Empty;
    private static int _pseudoFloor = -1;
    private static IList<scrFloor>? _checkpointFloorSource;
    private static string _checkpointLevelKey = string.Empty;
    private static readonly List<int> CheckpointSequences = new List<int>();
    private static float _lastReadWarningAt = -10f;

    internal static GameSnapshot Current => _current;
    internal static HudDisplayPreferences Preferences { get; private set; } = new HudDisplayPreferences();

    internal static bool TryGetProgress(out float progress)
    {
        progress = _current.Progress;
        return _current.InGame;
    }

    internal static bool TryGetBpm(out BpmSnapshot snapshot)
    {
        snapshot = new BpmSnapshot(_current.Bpm, _current.CurrentBpm, _current.Kps);
        return _current.InGame;
    }

    internal static bool TryGetAccuracy(out AccuracySnapshot snapshot)
    {
        snapshot = new AccuracySnapshot(
            _current.Accuracy,
            _current.XAccuracy,
            _current.PotentialAccuracy,
            _current.PotentialXAccuracy,
            _current.XScore,
            _current.MaxXScore,
            _current.PotentialXScore);
        return _current.InGame;
    }

    internal static void Refresh(ArgonStore store)
    {
        Preferences = store.Document.Preferences.Hud;
        var snapshot = default(GameSnapshot);
        try
        {
            var controller = scrController.instance;
            var conductor = scrConductor.instance;
            var levelMaker = ADOBase.lm;
            if (controller == null || conductor == null || levelMaker == null || !conductor.isGameWorld || levelMaker.listFloors == null)
            {
                if (_sessionActive)
                {
                    _sessionActive = false;
                    _sessionKey = string.Empty;
                }

                _checkpointFloorSource = null;
                _checkpointLevelKey = string.Empty;
                CheckpointSequences.Clear();

                _current = snapshot;
                return;
            }

            var floors = levelMaker.listFloors!;
            var totalTiles = floors.Count;
            var totalScorable = levelMaker.PlayerHitFloors != null ? levelMaker.PlayerHitFloors.Count : Math.Max(0, totalTiles - 1);
            var sequence = Math.Max(0, controller.currentSeqID);
            var currentFloor = controller.currFloor ?? controller.firstFloor;
            var player = controller.playerOne;
            var tracker = player != null ? player.marginTracker : null;
            var manager = controller.mistakesManager;
            var song = conductor.song;
            var songPitch = song != null ? Math.Max(0.01f, song.pitch) : 1f;
            var planetSpeed = player != null && player.planetarySystem != null ? (float)player.planetarySystem.speed : 1f;
            var baseBpm = conductor.bpm * songPitch * planetSpeed;
            var currentBpm = baseBpm;
            if (currentFloor != null && currentFloor.nextfloor != null)
            {
                var beatLength = currentFloor.nextfloor.entryTime - currentFloor.entryTime;
                if (beatLength > 0.0001d)
                {
                    currentBpm = (float)(60d / beatLength * songPitch);
                }
            }
            var pseudoBpm = 0f;
            var pseudoCount = 1;
            var isPseudoBpm = store.Document.Preferences.Hud.UsePseudoBpm &&
                              TryGetPseudoBpm(currentFloor, baseBpm, songPitch, out pseudoBpm, out pseudoCount);
            if (isPseudoBpm)
            {
                currentBpm = pseudoBpm;
            }
            else
            {
                pseudoCount = 1;
            }

            var stateName = controller.state.ToString();
            var levelName = string.IsNullOrWhiteSpace(controller.levelName)
                ? (scrController.currentWorldString ?? "Unknown level")
                : controller.levelName;
            var levelData = scnGame.instance != null ? scnGame.instance.levelData : null;
            var levelAuthor = levelData != null ? levelData.author : string.Empty;
            var recordKey = CreateLevelKey(levelName, totalTiles, levelData);
            EnsureCheckpointCache(floors, recordKey);
            var isActiveState = stateName == "Start" || stateName == "Countdown" || stateName == "PlayerControl";
            if (isActiveState && (!_sessionActive || _sessionKey != recordKey))
            {
                store.RecordAttempt(recordKey);
                _sessionActive = true;
                _sessionKey = recordKey;
                _pseudoFloor = -1;
                GameEventBridge.ResetSession();
            }

            if (stateName == "Fail" || stateName == "Fail2" || stateName == "Won")
            {
                _sessionActive = false;
                _sessionKey = string.Empty;
            }

            var progress = Mathf.Clamp01(controller.percentComplete);
            var record = store.RecordProgress(recordKey, progress);
            var accuracy = manager != null ? manager.percentAcc : 0f;
            var xAccuracy = manager != null ? manager.percentXAcc : 0f;
            var potentialXAccuracy = manager != null ? Mathf.Max(xAccuracy, manager.maxPossibleXAcc) : xAccuracy;
            var hits = tracker != null ? tracker.playerHitMarginCount : sequence;
            var remaining = Math.Max(0, totalScorable - hits);
            var potentialAccuracy = GetPotentialAccuracy(tracker, accuracy, sequence, totalTiles);
            var xScore = tracker != null ? tracker.xScore : 0;
            var maxXScore = tracker != null ? tracker.maxXScore : 0;
            var perPerfectScore = GetPerfectXScore();
            var potentialXScore = Math.Min(maxXScore, xScore + remaining * perPerfectScore);

            var checkpoints = CountReachedCheckpoints(sequence);

            var clip = song != null ? song.clip : null;
            var hasMusic = clip != null && clip.length > 0f;
            var finalFloor = totalTiles > 0 ? floors![totalTiles - 1] : null;
            var mapDuration = finalFloor != null ? (float)Math.Max(0d, finalFloor.entryTime) : 0f;
            var mapSeconds = (float)Math.Max(0d, conductor.addoffset + conductor.songposition_minusi);
            var musicDuration = hasMusic ? clip!.length : 0f;
            var musicSeconds = hasMusic ? Mathf.Clamp((float)conductor.songposition_minusi, 0f, musicDuration) : 0f;

            snapshot.InGame = true;
            snapshot.IsAuto = RDC.auto;
            snapshot.IsDead = stateName == "Fail" || stateName == "Fail2" || (tracker != null && tracker.GetDeaths() > 0);
            snapshot.LevelName = levelName;
            snapshot.LevelAuthor = levelAuthor ?? string.Empty;
            snapshot.State = stateName;
            snapshot.Judgement = GameEventBridge.LastJudgement;
            snapshot.HasJudgement = !string.IsNullOrEmpty(snapshot.Judgement);
            snapshot.Sequence = sequence;
            snapshot.TotalTiles = totalTiles;
            snapshot.CheckpointsUsed = scrController.checkpointsUsed;
            snapshot.CheckpointCount = checkpoints;
            snapshot.Attempts = record.Attempts;
            snapshot.Deaths = tracker != null ? tracker.GetDeaths() : 0;
            snapshot.StartSequence = controller.startedFromCheckpoint ? Math.Max(0, GCS.checkpointNum) : 0;
            snapshot.Combo = GameEventBridge.Combo;
            snapshot.PurePerfectCombo = GameEventBridge.PurePerfectCombo;
            snapshot.ComboTier = GameEventBridge.ComboTier;
            snapshot.XScore = xScore;
            snapshot.MaxXScore = maxXScore;
            snapshot.PotentialXScore = potentialXScore;
            snapshot.Progress = progress;
            snapshot.Accuracy = accuracy;
            snapshot.AbsoluteAccuracy = totalScorable <= 0 ? 0f : accuracy * Mathf.Clamp01((float)hits / totalScorable);
            snapshot.PotentialAccuracy = potentialAccuracy;
            snapshot.XAccuracy = xAccuracy;
            snapshot.PotentialXAccuracy = potentialXAccuracy;
            snapshot.Bpm = baseBpm;
            snapshot.CurrentBpm = currentBpm;
            snapshot.Kps = currentBpm / 60f * pseudoCount;
            snapshot.PseudoBeatCount = pseudoCount;
            snapshot.IsPseudoBpm = isPseudoBpm;
            snapshot.TimingMilliseconds = GameEventBridge.LastTimingMilliseconds;
            snapshot.AverageTimingMilliseconds = GameEventBridge.AverageTimingMilliseconds;
            snapshot.TimingScale = currentFloor != null ? (float)currentFloor.marginScale : 1f;
            snapshot.MusicSeconds = musicSeconds;
            snapshot.MusicDuration = musicDuration;
            snapshot.MapSeconds = Mathf.Clamp(mapSeconds, 0f, Math.Max(0f, mapDuration));
            snapshot.MapDuration = mapDuration;
            snapshot.PlanetAngle = player != null && player.planetarySystem != null && player.planetarySystem.chosenPlanet != null
                ? (float)player.planetarySystem.chosenPlanet.angle
                : 0f;
            snapshot.BestProgress = record.BestProgress;
            snapshot.HasMusic = hasMusic;
        }
        catch (Exception exception)
        {
            if (Time.unscaledTime - _lastReadWarningAt >= 5f)
            {
                _lastReadWarningAt = Time.unscaledTime;
                Debug.LogWarning($"[Argon] ADOFAI state read failed; status HUD is temporarily unavailable: {exception.Message}");
            }
            snapshot = default;
        }

        _current = snapshot;
    }

    internal static string FormatPercent(float value, int decimals)
    {
        return (value * 100f).ToString("F" + Mathf.Clamp(decimals, 0, 4), CultureInfo.InvariantCulture) + "%";
    }

    internal static string FormatTime(float seconds)
    {
        var safeSeconds = Math.Max(0, (int)Math.Floor(float.IsNaN(seconds) || float.IsInfinity(seconds) ? 0f : seconds));
        return safeSeconds >= 3600
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", safeSeconds / 3600, safeSeconds % 3600 / 60, safeSeconds % 60)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", safeSeconds / 60, safeSeconds % 60);
    }

    private static float GetPotentialAccuracy(scrMarginTracker? tracker, float accuracy, int sequence, int tileCount)
    {
        if (tracker == null || tracker.hitMarginsCount == null || tracker.hitMarginsCount.Length <= 14)
        {
            return accuracy;
        }

        var hits = tracker.hitMarginsCount;
        var perfect = hits[3] + hits[4] + hits[5] + hits[12] + hits[14];
        var accurate = perfect + hits[2] + hits[6];
        var remaining = Math.Max(tileCount - 1 - sequence, 0);
        var rate = accuracy - perfect * 0.0001f;
        var count = accurate == 0 || rate <= 0f || float.IsNaN(rate)
            ? sequence
            : Mathf.RoundToInt(accurate / rate);
        count = Math.Max(count, accurate);
        var total = count + remaining;
        return total == 0
            ? 1f
            : Mathf.Clamp01((perfect + remaining) * 0.0001f + (float)(accurate + remaining) / total);
    }

    private static void EnsureCheckpointCache(IList<scrFloor> floors, string levelKey)
    {
        if (ReferenceEquals(_checkpointFloorSource, floors) && _checkpointLevelKey == levelKey) return;

        _checkpointFloorSource = floors;
        _checkpointLevelKey = levelKey;
        CheckpointSequences.Clear();
        foreach (var floor in floors)
        {
            if (floor != null && floor.GetComponent<ffxCheckpoint>() != null)
            {
                CheckpointSequences.Add(floor.seqID);
            }
        }
    }

    private static int CountReachedCheckpoints(int sequence)
    {
        var reached = 0;
        for (var i = 0; i < CheckpointSequences.Count && CheckpointSequences[i] <= sequence; i++)
        {
            reached++;
        }

        return reached;
    }

    private static string CreateLevelKey(string levelName, int tileCount, ADOFAI.LevelData? levelData)
    {
        var identity = levelName.Trim().ToLowerInvariant();
        try
        {
            if (levelData != null && !string.IsNullOrWhiteSpace(levelData.Hash))
            {
                identity = levelData.Hash.Trim().ToLowerInvariant();
            }
        }
        catch
        {
            // Some built-in levels do not expose enough metadata for a content hash.
        }

        return Hash128.Compute(identity + "|" + tileCount).ToString();
    }

    private static bool TryGetPseudoBpm(scrFloor? currentFloor, float baseBpm, float pitch, out float bpm, out int count)
    {
        bpm = 0f;
        count = 1;
        if (currentFloor == null || baseBpm < 200f || scnGame.instance == null || currentFloor.seqID <= _pseudoFloor)
        {
            return false;
        }

        var maximumAngle = baseBpm < 400f ? 0.5236d : 1.0472d;
        var accumulatedAngle = 0d;
        var midSpins = 0;
        var floor = currentFloor;
        while (floor != null && floor.nextfloor != null)
        {
            if (floor.midSpin)
            {
                floor = floor.nextfloor;
                midSpins++;
                continue;
            }

            var angle = floor.angleLength;
            accumulatedAngle += angle;
            if (accumulatedAngle > maximumAngle &&
                (baseBpm < 600f || accumulatedAngle - angle > 1e-14d || !IsNinetyDegrees(angle)))
            {
                break;
            }

            floor = floor.nextfloor;
        }

        if (floor == null)
        {
            count = 1;
            return false;
        }

        if (IsNinetyDegrees(currentFloor.angleLength) && CountNearbyNinetyDegreeFloors(currentFloor) >= 3)
        {
            return false;
        }

        count = floor.seqID - currentFloor.seqID + 1 - midSpins;
        if (count <= 1)
        {
            count = 1;
            return false;
        }

        var interval = floor.nextfloor != null
            ? floor.nextfloor.entryTime - currentFloor.entryTime
            : floor.entryTime - currentFloor.entryTime + 60d / Math.Max(0.001f, baseBpm);
        if (interval <= 0.0001d)
        {
            count = 1;
            return false;
        }

        bpm = (float)(60d / interval * pitch);
        _pseudoFloor = floor.seqID;
        return bpm > 0f && !float.IsNaN(bpm) && !float.IsInfinity(bpm);
    }

    private static int CountNearbyNinetyDegreeFloors(scrFloor currentFloor)
    {
        var count = 0;
        var floor = currentFloor;
        while (count < 3 && floor != null && IsNinetyDegrees(floor.angleLength))
        {
            count++;
            floor = floor.nextfloor;
            if (floor != null && floor.midSpin) floor = floor.nextfloor;
        }

        return count;
    }

    private static bool IsNinetyDegrees(double angle)
    {
        return Math.Abs(angle - 1.5707963267948966d) < 1e-10d;
    }

    private static int GetPerfectXScore()
    {
        try
        {
            return HitMargin.XPerfect.ToXScore();
        }
        catch
        {
            return 2;
        }
    }
}

internal readonly struct BpmSnapshot
{
    internal float TileBpm { get; }
    internal float CurrentBpm { get; }
    internal float Kps { get; }

    internal BpmSnapshot(float tileBpm, float currentBpm, float kps)
    {
        TileBpm = tileBpm;
        CurrentBpm = currentBpm;
        Kps = kps;
    }
}

internal readonly struct AccuracySnapshot
{
    internal float Accuracy { get; }
    internal float XAccuracy { get; }
    internal float PotentialAccuracy { get; }
    internal float PotentialXAccuracy { get; }
    internal int XScore { get; }
    internal int MaxXScore { get; }
    internal int PotentialXScore { get; }

    internal AccuracySnapshot(float accuracy, float xAccuracy, float potentialAccuracy, float potentialXAccuracy, int xScore, int maxXScore, int potentialXScore)
    {
        Accuracy = accuracy;
        XAccuracy = xAccuracy;
        PotentialAccuracy = potentialAccuracy;
        PotentialXAccuracy = potentialXAccuracy;
        XScore = xScore;
        MaxXScore = maxXScore;
        PotentialXScore = potentialXScore;
    }
}
