using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace Argon.Hud;

internal static class StatusHudElement
{
    internal static HudElementDefinition CreateDefinition()
    {
        return new HudElementDefinition(
            "argon.builtin.status",
            "시간 · 기록 · 체크포인트",
            false,
            HudAnchor.TopLeft,
            new Vector2(420f, -24f),
            new Vector2(460f, 300f),
            HudUpdatePolicy.Interval,
            0.25f,
            parent => new HudTextView(parent, "Hud.Status", TextAlignmentOptions.TopLeft),
            Update);
    }

    private static void Update(HudElementView view, float _)
    {
        var textView = (HudTextView)view;
        var snapshot = GameStateSource.Current;
        if (!snapshot.InGame)
        {
            textView.SetText(string.Empty);
            textView.ApplyStyle(HudStyle.FontSize, HudStyle.TextColor);
            return;
        }

        var preferences = GameStateSource.Preferences;
        var lines = new List<string>(6);
        var reduced = preferences.HideHudDuringAuto && snapshot.IsAuto;
        if (preferences.ShowAuthor && !string.IsNullOrWhiteSpace(snapshot.LevelAuthor))
        {
            lines.Add("Author: " + snapshot.LevelAuthor);
        }

        if (reduced)
        {
            lines.Add("AUTO · Progress: " + GameStateSource.FormatPercent(snapshot.Progress, preferences.ProgressDecimals));
            textView.SetText(string.Join("\n", lines));
            textView.ApplyStyle(HudStyle.FontSize, HudStyle.TextColor);
            return;
        }

        if (preferences.ShowMusicTime)
        {
            lines.Add(snapshot.HasMusic
                ? $"Music: {GameStateSource.FormatTime(snapshot.MusicSeconds)} / {GameStateSource.FormatTime(snapshot.MusicDuration)}"
                : $"Map time: {GameStateSource.FormatTime(snapshot.MapSeconds)} / {GameStateSource.FormatTime(snapshot.MapDuration)}");
        }

        if (preferences.ShowMapTime && (snapshot.HasMusic || !preferences.ShowMusicTime))
        {
            lines.Add($"Map: {GameStateSource.FormatTime(snapshot.MapSeconds)} / {GameStateSource.FormatTime(snapshot.MapDuration)}");
        }

        if (preferences.ShowCheckpoint)
        {
            lines.Add($"Checkpoints: {snapshot.CheckpointsUsed} used · {snapshot.CheckpointCount} reached");
        }

        if (preferences.ShowAttempts)
        {
            lines.Add($"Attempts: {snapshot.Attempts}");
        }

        if (preferences.ShowDeath)
        {
            lines.Add("Deaths: " + snapshot.Deaths);
        }

        if (preferences.ShowBestProgress)
        {
            lines.Add("Best: " + GameStateSource.FormatPercent(snapshot.BestProgress, preferences.ProgressDecimals));
        }

        if (preferences.ShowState)
        {
            lines.Add("State: " + FormatState(snapshot));
        }

        if (preferences.ShowStart && snapshot.StartSequence > 0)
        {
            lines.Add("Start: Checkpoint " + snapshot.StartSequence);
        }
        if (preferences.ShowTileInfo)
        {
            lines.Add($"Angle: {snapshot.PlanetAngle.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}°");
        }

        textView.SetText(string.Join("\n", lines));
        textView.ApplyStyle(HudStyle.FontSize, HudStyle.TextColor);
    }

    private static string FormatState(GameSnapshot snapshot)
    {
        switch (snapshot.State)
        {
            case "Start":
            case "Countdown":
                return "Waiting";
            case "Fail":
            case "Fail2":
                return "Failed";
            case "Won":
                return "Cleared";
            default:
                return snapshot.IsAuto ? "Auto play" : "Playing";
        }
    }
}
