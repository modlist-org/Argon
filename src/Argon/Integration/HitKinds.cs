using System;
using System.Collections.Generic;
using UnityEngine;

namespace Argon.Integration;

/// <summary>Stable judgement classes, independent of the game's HitMargin numbering.</summary>
internal enum HitKind
{
    TooEarly,
    VeryEarly,
    EarlyPerfect,
    Perfect,
    LatePerfect,
    VeryLate,
    TooLate,
    Multipress,
    FailMiss,
    FailOverload,
    Auto,
    OverPress,
    FailedFloor,
    Unknown,
}

/// <summary>
/// Maps HitMargin values by enum <em>name</em> at runtime, so reordered or added game judgements
/// never shift array indices. Adapted from Quartz (GPL-3.0) Compat/Game/HitKinds.cs.
/// </summary>
internal static class HitKinds
{
    private static readonly HitKind[] ByGame;
    private static readonly bool[] PurePerfect;
    private static readonly List<int>[] GameIndices;

    static HitKinds()
    {
        var kinds = (int)HitKind.Unknown + 1;
        GameIndices = new List<int>[kinds];
        for (var k = 0; k < kinds; k++) GameIndices[k] = new List<int>();

        var byGame = new List<HitKind>();
        var pure = new List<bool>();
        try
        {
            foreach (var raw in Enum.GetValues(typeof(HitMargin)))
            {
                var value = Convert.ToInt32(raw);
                if (value < 0) continue;
                var name = Enum.GetName(typeof(HitMargin), raw) ?? string.Empty;
                var kind = Classify(name);
                while (byGame.Count <= value)
                {
                    byGame.Add(HitKind.Unknown);
                    pure.Add(false);
                }

                byGame[value] = kind;
                pure[value] = name == "XPerfect" || name == "Perfect";
                GameIndices[(int)kind].Add(value);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Argon] Could not map the game's HitMargin values: {exception.Message}");
        }

        ByGame = byGame.ToArray();
        PurePerfect = pure.ToArray();
    }

    internal static HitKind Classify(string name)
    {
        switch (name)
        {
            case "Perfect":
            case "PerfectMinus":
            case "XPerfect":
            case "PerfectPlus":
                return HitKind.Perfect;
            case "Midspin":
                return HitKind.Auto;
            default:
                return Enum.TryParse(name, out HitKind kind) ? kind : HitKind.Unknown;
        }
    }

    internal static HitKind Of(HitMargin margin)
    {
        var i = (int)margin;
        return i >= 0 && i < ByGame.Length ? ByGame[i] : HitKind.Unknown;
    }

    /// <summary>True for the centre (pure / X) perfect only, not its signed ±perfect neighbours.</summary>
    internal static bool IsPurePerfect(HitMargin margin)
    {
        var i = (int)margin;
        return i >= 0 && i < PurePerfect.Length && PurePerfect[i];
    }

    /// <summary>Sums a game-indexed count array (e.g. hitMarginsCount) for one kind.</summary>
    internal static int Count(int[]? gameCounts, HitKind kind)
    {
        if (gameCounts == null) return 0;
        var sum = 0;
        foreach (var i in GameIndices[(int)kind])
        {
            if (i < gameCounts.Length) sum += gameCounts[i];
        }

        return sum;
    }
}
