using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Argon.UI;

/// <summary>
/// Adds the game's own CJK-capable TMP fonts as fallbacks to the fonts Argon renders with, so level
/// names, authors and custom key labels in Korean, Japanese or Chinese never show as missing glyphs.
/// Probe-by-glyph approach adapted from Quartz (GPL-3.0) Resource/FontManager.cs.
/// </summary>
internal sealed class FontFallback : IDisposable
{
    // Common characters per script; a font "covers" a script when it has most of them.
    private static readonly int[][] Probes =
    {
        new[] { 0xAC00, 0xD55C, 0xAD6D, 0xC5B4, 0xC124 }, // Hangul: 가 한 국 어 설
        new[] { 0x3042, 0x3044, 0x30A2, 0x30AB, 0x30F3 }, // Kana: あ い ア カ ン
        new[] { 0x8FD9, 0x56FD, 0x8BF4, 0x6F22, 0x5B57 }, // Han: 这 国 说 漢 字
    };

    private const float RetrySeconds = 5f;
    private const int MaxAttempts = 12;

    private readonly TMP_FontAsset[] _targets;
    private readonly TMP_FontAsset?[] _found = new TMP_FontAsset?[Probes.Length];
    private readonly List<(TMP_FontAsset Target, TMP_FontAsset Fallback)> _added = new List<(TMP_FontAsset, TMP_FontAsset)>();
    private float _nextAttemptAt;
    private int _attempts;

    internal FontFallback(params TMP_FontAsset?[] targets)
    {
        var list = new List<TMP_FontAsset>();
        foreach (var target in targets)
        {
            if (target != null && !list.Contains(target)) list.Add(target);
        }

        _targets = list.ToArray();
        Attach();
    }

    /// <summary>Game fonts can load after Argon; keep probing for a minute until every script is covered.</summary>
    internal void Tick()
    {
        if (_attempts >= MaxAttempts || Time.unscaledTime < _nextAttemptAt) return;
        Attach();
    }

    public void Dispose()
    {
        foreach (var (target, fallback) in _added)
        {
            if (target != null && target.fallbackFontAssetTable != null) target.fallbackFontAssetTable.Remove(fallback);
        }

        _added.Clear();
    }

    private void Attach()
    {
        _attempts++;
        _nextAttemptAt = Time.unscaledTime + RetrySeconds;
        TMP_FontAsset[] candidates;
        try
        {
            candidates = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Argon] Font fallback scan failed: {exception.Message}");
            return;
        }

        var complete = true;
        for (var script = 0; script < Probes.Length; script++)
        {
            if (_found[script] == null && TargetsCover(script))
            {
                continue; // Argon's own font already has this script (SUIT covers Hangul).
            }

            _found[script] ??= FindCovering(candidates, Probes[script]);
            if (_found[script] == null)
            {
                complete = false;
                continue;
            }

            foreach (var target in _targets)
            {
                var fallback = _found[script]!;
                if (fallback == target) continue;
                target.fallbackFontAssetTable ??= new List<TMP_FontAsset>();
                if (target.fallbackFontAssetTable.Contains(fallback)) continue;
                target.fallbackFontAssetTable.Add(fallback);
                _added.Add((target, fallback));
                Debug.Log($"[Argon] Font fallback for {target.name}: {fallback.name}");
            }
        }

        if (complete) _attempts = MaxAttempts;
    }

    private bool TargetsCover(int script)
    {
        foreach (var target in _targets)
        {
            if (!Covers(target, Probes[script], tryAdd: true)) return false;
        }

        return _targets.Length > 0;
    }

    private TMP_FontAsset? FindCovering(TMP_FontAsset[] candidates, int[] probe)
    {
        // Static atlases first (cheap, no atlas writes); then let dynamic fonts try to add the glyphs.
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var candidate in candidates)
            {
                if (candidate == null || Array.IndexOf(_targets, candidate) >= 0) continue;
                if (Covers(candidate, probe, tryAdd: pass == 1)) return candidate;
            }
        }

        return null;
    }

    private static bool Covers(TMP_FontAsset font, int[] probe, bool tryAdd)
    {
        try
        {
            var hits = 0;
            foreach (var codePoint in probe)
            {
                if (font.HasCharacter((char)codePoint, false, tryAdd)) hits++;
            }

            return hits >= 3;
        }
        catch
        {
            return false;
        }
    }
}
