using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;

namespace FfxivMsqThai.Services;

public class DialogueDictionary
{
    private readonly Dictionary<string, string> _lookup = new(StringComparer.Ordinal);
    private readonly HashSet<string> _knownQuestSlugs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string?> _fuzzyCache = new(StringComparer.Ordinal);

    public int Count => _lookup.Count;

    public bool HasQuest(string questSlug) => _knownQuestSlugs.Contains(questSlug);

    public DialogueDictionary(string contentRoot, IPluginLog log)
    {
        var enRoot = Path.Combine(contentRoot, "content_community", "en");
        var thRoot = Path.Combine(contentRoot, "content_community", "th");

        if (!Directory.Exists(enRoot))
        {
            log.Warning($"[ffxiv-msq-thai] content_community/en/ not found at: {enRoot}");
            return;
        }

        foreach (var enFile in Directory.EnumerateFiles(enRoot, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                var rel    = Path.GetRelativePath(enRoot, enFile);
                var thFile = Path.Combine(thRoot, rel);
                LoadQuest(enFile, thFile);
            }
            catch (Exception ex)
            {
                log.Warning($"[ffxiv-msq-thai] Skip {Path.GetFileName(enFile)}: {ex.Message}");
            }
        }

        log.Information($"[ffxiv-msq-thai] Dictionary ready — {_lookup.Count} entries.");
    }

    // ── Exact lookup ──────────────────────────────────────────────────────

    public bool TryGetThai(string lookupKey, out string textTh)
        => _lookup.TryGetValue(lookupKey, out textTh!);

    // ── Fuzzy fallback (Levenshtein >= 95 % similarity) ───────────────────

    public bool TryGetThaiFuzzy(string lookupKey, out string textTh, double threshold = 0.95)
    {
        textTh = string.Empty;
        if (lookupKey.Length < 15) return false; // too short for safe fuzzy matching

        string? bestKey = null;
        var bestSim    = 0.0;

        foreach (var key in _lookup.Keys)
        {
            var maxLen = Math.Max(key.Length, lookupKey.Length);
            // Cap maximum allowed edits to 10 characters to maintain interactive performance
            // on the main UI thread during dialogue refreshes.
            var maxDist = Math.Min(10, (int)(maxLen * (1.0 - threshold)));

            if (Math.Abs(key.Length - lookupKey.Length) > maxDist) continue;

            var dist = LevenshteinDistance(key, lookupKey, maxDist);
            if (dist > maxDist) continue;

            var sim  = 1.0 - (double)dist / maxLen;
            if (sim > bestSim) { bestSim = sim; bestKey = key; }
        }

        if (bestKey != null && bestSim >= threshold)
        {
            textTh = _lookup[bestKey];
            return true;
        }
        return false;
    }

    public bool TryGetCachedFuzzyMatch(string lookupKey, out string? cachedThai)
        => _fuzzyCache.TryGetValue(lookupKey, out cachedThai);

    public record FuzzyMatchResult(string ThaiText);

    public FuzzyMatchResult? FindFuzzyMatch(string lookupKey, double threshold = 0.95)
    {
        if (_fuzzyCache.TryGetValue(lookupKey, out var cachedThai))
        {
            return cachedThai != null ? new FuzzyMatchResult(cachedThai) : null;
        }

        if (TryGetThaiFuzzy(lookupKey, out var textTh, threshold))
        {
            _fuzzyCache[lookupKey] = textTh;
            return new FuzzyMatchResult(textTh);
        }

        _fuzzyCache[lookupKey] = null;
        return null;
    }

    private static int LevenshteinDistance(string a, string b, int maxDist)
    {
        if (a == b) return 0;
        var m = a.Length;
        var n = b.Length;

        if (Math.Abs(m - n) > maxDist) return maxDist + 1;

        var row = new int[n + 1];
        for (var j = 0; j <= n; j++) row[j] = j;

        for (var i = 1; i <= m; i++)
        {
            var prev = i;
            var minInRow = i;

            for (var j = 1; j <= n; j++)
            {
                var curr = a[i - 1] == b[j - 1]
                    ? row[j - 1]
                    : 1 + Math.Min(row[j - 1], Math.Min(row[j], prev));
                row[j - 1] = prev;
                prev = curr;
                if (curr < minInRow) minInRow = curr;
            }
            row[n] = prev;

            if (minInRow > maxDist) return maxDist + 1;
        }
        return row[n];
    }

    // ── Key normalizers ───────────────────────────────────────────────────

    private static readonly Regex MultiSpace = new Regex(@" {2,}", RegexOptions.Compiled);

    /// <summary>
    /// Strips punctuation and casing, keeping only lowercase letters and digits.
    /// Used as the actual dictionary key so spacing, quote, and punctuation
    /// variances between JSON metadata and game memory never cause a miss.
    /// </summary>
    public static string ToPureAlphanumericKey(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>
    /// Human-readable normalizer for the English fallback display token.
    /// NOT used as a lookup key.
    /// </summary>
    public static string NormalizeEnglishKey(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var s = input.Trim()
                     .Replace("‘", "'").Replace("’", "'")
                     .Replace("“", "\"").Replace("”", "\"")
                     .Replace("\r", "")
                     .Replace("\n", " ");
        return MultiSpace.Replace(s, " ");
    }

    // ── Load ──────────────────────────────────────────────────────────────

    private void LoadQuest(string enFile, string thFile)
    {
        var en = Read<QuestFile>(enFile);
        if (en?.Dialogues == null) return;

        var th       = File.Exists(thFile) ? Read<QuestFile>(thFile) : null;
        var questSlug = ToPureAlphanumericKey(Path.GetFileNameWithoutExtension(enFile));
        _knownQuestSlugs.Add(questSlug);

        for (var i = 0; i < en.Dialogues.Count; i++)
        {
            var textEn = en.Dialogues[i].Text;
            if (string.IsNullOrWhiteSpace(textEn)) continue;

            var key = ToPureAlphanumericKey(textEn);
            if (key.Length == 0) continue;

            if (th?.Dialogues != null && i < th.Dialogues.Count)
            {
                var textTh = th.Dialogues[i].Text;
                if (!string.IsNullOrWhiteSpace(textTh))
                {
                    // Tier 1: quest-scoped compound key — highest priority
                    _lookup[$"{questSlug}_{key}"] = textTh;

                    // Tier 2: global key — first-write-wins so a later quest's
                    // translation never silently overwrites an earlier one
                    _lookup.TryAdd(key, textTh);
                }
            }
        }
    }

    private static T? Read<T>(string path)
    {
        using var s = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(s, JsonOpts);
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private record QuestFile(
        [property: JsonPropertyName("dialogues")] List<Dialogue>? Dialogues);

    private record Dialogue(
        [property: JsonPropertyName("text")] string? Text);
}
