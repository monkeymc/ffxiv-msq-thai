using System;
using System.Text.RegularExpressions;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FfxivMsqThai.Services;

public sealed class TalkHook : IDisposable
{
    private const string AddonName = "Talk";

    private static readonly string[] CutsceneAddons = {
        "TalkSubtitle",
        "CutSceneSubtitle",
        "CutsceneDialogue"
    };

    public string ActiveAddonName { get; private set; } = "Talk";

    // Strip lines that are purely dashes/hyphens (AI "no-translation" markers)
    private static readonly Regex DashOnlyLine =
        new(@"^[\-–—\s]+$", RegexOptions.Compiled);

    // Remove inline sequences of 3+ dashes (layout artefacts from game payloads)
    private static readonly Regex InlineDashRun =
        new(@"[\-–—]{3,}", RegexOptions.Compiled);

    // Strip raw SeString control-code bytes that survive the string decoder
    private static readonly Regex SeControl =
        new(@"[\x02][\s\S]{1,4}[\x03]|[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]",
            RegexOptions.Compiled);

    private readonly IAddonLifecycle _addonLifecycle;
    private readonly DialogueDictionary _dictionary;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly IPluginLog _log = Plugin.Log;

    public string[] CurrentTokens { get; private set; } = Array.Empty<string>();

    public TalkHook(
        IAddonLifecycle addonLifecycle,
        DialogueDictionary dictionary,
        IClientState clientState,
        IObjectTable objectTable)
    {
        _addonLifecycle = addonLifecycle;
        _dictionary     = dictionary;
        _clientState    = clientState;
        _objectTable    = objectTable;

       _addonLifecycle.RegisterListener(AddonEvent.PreRefresh, AddonName, OnPreRefresh);
        _addonLifecycle.RegisterListener(AddonEvent.PreHide,    AddonName, OnHide);
        _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonName, OnHide);

        foreach (var cutsceneAddon in CutsceneAddons)
        {
            _addonLifecycle.RegisterListener(AddonEvent.PreSetup,   cutsceneAddon, OnPreRefresh);
            _addonLifecycle.RegisterListener(AddonEvent.PreRefresh, cutsceneAddon, OnPreRefresh);
            _addonLifecycle.RegisterListener(AddonEvent.PreHide,    cutsceneAddon, OnHide);
            _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, cutsceneAddon, OnHide);
        }
    }

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PreRefresh, AddonName, OnPreRefresh);
        _addonLifecycle.UnregisterListener(AddonEvent.PreHide,    AddonName, OnHide);
        _addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, AddonName, OnHide);

        foreach (var cutsceneAddon in CutsceneAddons)
        {
            _addonLifecycle.UnregisterListener(AddonEvent.PreSetup,   cutsceneAddon, OnPreRefresh);
            _addonLifecycle.UnregisterListener(AddonEvent.PreRefresh, cutsceneAddon, OnPreRefresh);
            _addonLifecycle.UnregisterListener(AddonEvent.PreHide,    cutsceneAddon, OnHide);
            _addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, cutsceneAddon, OnHide);
        }
    }

    private unsafe void OnPreRefresh(AddonEvent type, AddonArgs args)
    {
        ActiveAddonName = args.AddonName;

        string textEn = string.Empty;

        if (args.AddonName == AddonName)
        {
            // Talk addon: text in AtkValue[0]
            if (args is not AddonRefreshArgs refreshArgs) return;
            var atkValues = (AtkValue*)refreshArgs.AtkValues;
            if (atkValues == null || refreshArgs.AtkValueCount < 1) return;

            var textPtr = (nint)atkValues[0].String.Value;
            if (textPtr == 0) { CurrentTokens = Array.Empty<string>(); return; }

            textEn = MemoryHelper.ReadSeStringAsString(out _, textPtr);
        }
        else if (args.AddonName == "TalkSubtitle" || args.AddonName == "CutSceneSubtitle" || args.AddonName == "CutsceneDialogue")
        {
            // Cutscene subtitle addons: try AtkValue first, fallback to text node
            AtkValue* atkValues = null;
            if (args is AddonSetupArgs setupArgs)
            {
                atkValues = (AtkValue*)setupArgs.AtkValues;
            }
            else if (args is AddonRefreshArgs refreshArgs2)
            {
                atkValues = (AtkValue*)refreshArgs2.AtkValues;
            }

            if (atkValues != null && atkValues[0].Type == FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.String && atkValues[0].String.Value != null)
            {
                textEn = MemoryHelper.ReadSeStringAsString(out _, (nint)atkValues[0].String.Value);
            }
            else
            {
                var addon = (AtkUnitBase*)args.Addon.Address;
                textEn = GetTextFromSubtitleAddon(addon);
            }
        }
        else
        {
            return;
        }
       if (string.IsNullOrWhiteSpace(textEn)) { CurrentTokens = Array.Empty<string>(); return; }

        _log.Information($"[MSQ-Thai-UI] TEXT: '{textEn}' (addon={args.AddonName})");

        // Normalize and strip the player's character name so our generic JSON
        // placeholder ("Forename Surname" / "Forename") aligns with whatever the player chose.
        var displayEn  = DialogueDictionary.NormalizeEnglishKey(textEn);
        var fullName   = _objectTable.LocalPlayer?.Name.ToString() ?? string.Empty;
        var firstName  = !string.IsNullOrEmpty(fullName) ? fullName.Split(' ')[0] : string.Empty;

        if (!string.IsNullOrEmpty(fullName) && displayEn.Contains(fullName))
        {
            displayEn = displayEn.Replace(fullName, "Forename Surname");
        }
        else if (!string.IsNullOrEmpty(firstName) && displayEn.Contains(firstName))
        {
            displayEn = displayEn.Replace(firstName, "Forename");
        }

        if (displayEn.Length == 0)
        {
            CurrentTokens = Array.Empty<string>();
            return;
        }

       var lookupKey = DialogueDictionary.ToPureAlphanumericKey(displayEn);

        _log.Information($"[MSQ-Thai-UI] LOOKUP: key='{lookupKey}' (normalized='{displayEn}')");

        string? rawThai = null;

        // Step A: Strict Exact Probing (100%)
        if (_dictionary.TryGetThai(lookupKey, out var exactTh))
        {
            rawThai = exactTh;
            _log.Information($"[MSQ-Thai-UI] MATCH: exact '{lookupKey}' -> '{rawThai}'");
        }
      // Step B: Fuzzy Probing (75%) — try regardless of player name
        else
        {
            var fuzzyResult = _dictionary.FindFuzzyMatch(lookupKey, threshold: 0.75f);
            if (fuzzyResult != null)
            {
                rawThai = fuzzyResult.ThaiText;
                _log.Information($"[MSQ-Thai-UI] MATCH: fuzzy '{lookupKey}' -> '{rawThai}'");
            }
            else
            {
                _log.Debug($"[MSQ-Thai-UI] NO MATCH: '{lookupKey}' — no match above 0.75");
            }
        }

        // Step C: Absolute Miss Gate (The Suppress Rule)
        if (rawThai == null)
        {
            CurrentTokens = Array.Empty<string>();
            return;
        }

        var clean = SanitizeThai(rawThai);
        if (string.IsNullOrWhiteSpace(clean))
        {
            CurrentTokens = Array.Empty<string>();
            return;
        }

        CurrentTokens = ThaiWordSegmenter.Segment(clean);
    }

    private void OnHide(AddonEvent type, AddonArgs args)
        => CurrentTokens = Array.Empty<string>();

    /// <summary>
    /// Strips game artefacts and AI "no-translation" markers from Thai output.
    /// </summary>
    internal static string SanitizeThai(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        if (DashOnlyLine.IsMatch(text)) return string.Empty;

        text = SeControl.Replace(text, string.Empty);
        text = InlineDashRun.Replace(text, string.Empty);
        return text.Trim();
    }

    private unsafe string GetTextFromSubtitleAddon(AtkUnitBase* addon)
    {
        if (addon == null) return string.Empty;
        var textNode = FindTextNode(addon->RootNode);
        if (textNode == null) return string.Empty;

        var strPtr = ((AtkTextNode*)textNode)->NodeText.StringPtr.Value;
        if (strPtr == null) return string.Empty;

        return MemoryHelper.ReadSeStringAsString(out _, (nint)strPtr);
    }

    private unsafe AtkResNode* FindTextNode(AtkResNode* node)
    {
        if (node == null) return null;
        if ((int)node->Type == 3) return node;

        var child = FindTextNode(node->ChildNode);
        if (child != null) return child;

        return FindTextNode(node->NextSiblingNode);
    }
}
