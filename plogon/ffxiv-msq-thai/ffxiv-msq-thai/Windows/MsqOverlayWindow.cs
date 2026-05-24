using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FfxivMsqThai.Services;

namespace FfxivMsqThai.Windows;

public sealed class MsqOverlayWindow : Window, IDisposable
{
    private const uint NameNodeId = 2;
    private const uint TextNodeId = 3;

    // Parchment-cream palette matching FFXIV's native dialogue tone
    private static readonly Vector4 BgColor     = new(0.90f, 0.85f, 0.73f, 0.95f);
    private static readonly Vector4 TextColor   = new(0.10f, 0.07f, 0.03f, 1.00f);
    private static readonly Vector4 BorderColor = new(0.60f, 0.50f, 0.35f, 0.80f);

    private const float PadH        = 15f;
    private const float PadV        = 10f;
    private const float WrapSafeMargin = 20f; // buffer against Thai glyph right-edge clipping
    private const float CutsceneOverlayWidth = 1000f; // wider for cutscene subtitles

    private readonly TalkHook _talkHook;
    private readonly IGameGui _gameGui;
    private readonly PluginConfig _config;
    private readonly IFontHandle _thaiFont;

    private float _targetX, _targetY, _targetW;
    private bool _isFallbackLayout;

    public MsqOverlayWindow(
        TalkHook talkHook,
        IGameGui gameGui,
        IDalamudPluginInterface pi,
        PluginConfig config)
        : base("##msq-th-overlay",
            ImGuiWindowFlags.NoTitleBar      |
            ImGuiWindowFlags.NoResize        |
            ImGuiWindowFlags.NoMove          |
            ImGuiWindowFlags.NoScrollbar     |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoCollapse      |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav           |
            ImGuiWindowFlags.NoInputs        |
            ImGuiWindowFlags.AlwaysAutoResize)
    {
        _talkHook = talkHook;
        _gameGui  = gameGui;
        _config   = config;

        var fontPath = System.IO.Path.Combine(
            pi.AssemblyLocation.Directory!.FullName,
            "THSarabunNew-Bold.ttf");

        // Three-pass font build (GPOS compensation — ImGui has no shaping engine):
        //
        //   Pass 1 — full font at natural baseline.
        //   Pass 2 — upper-vowel layer (ิ ี ึ ื ็ ํ) shifted DOWN +4 px so they
        //            sit closer to the consonant top, freeing vertical headroom
        //            between the vowel and tone-mark layers.
        //   Pass 3 — tone-mark layer (่ ้ ๊ ๋ ์) shifted UP -6 px so they float
        //            cleanly above the vowels in stacked syllables like ทิ้ง, พื้น.
        //
        //   Net visible gap vowel→tone mark: 10 px (4 down + 6 up).
        //   Clip-rect expansion in Draw() prevents the -6 px shift from being
        //   cut off by the window content boundary on the first text line.
        _thaiFont = pi.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
            e.OnPreBuild(tk =>
            {
                // Pass 1: full font at natural baseline
                var baseFont = tk.AddFontFromFile(fontPath,
                    new SafeFontConfig { SizePx = _config.FontSize });

                // Pass 2: upper-vowel layer — shift down to open stack headroom
                //   ิ ี ึ ื  U+0E34–0E37  sara i – sara uee
                //   ็        U+0E47        mai taikhu
                //   ํ        U+0E4D        nikhahit
                tk.AddFontFromFile(fontPath, new SafeFontConfig
                {
                    SizePx      = _config.FontSize,
                    MergeFont   = baseFont,
                    GlyphOffset = new Vector2(0f, 4f),
                    GlyphRanges = new ushort[]
                    {
                        0x0E34, 0x0E37,
                        0x0E47, 0x0E47,
                        0x0E4D, 0x0E4D,
                        0,
                    },
                });

                // Pass 3: tone-mark layer — shift up to float above vowels
                //   ่ ้ ๊ ๋  U+0E48–0E4B  mai ek – mai chattawa
                //   ์        U+0E4C        thanthakat
                tk.AddFontFromFile(fontPath, new SafeFontConfig
                {
                    SizePx      = _config.FontSize,
                    MergeFont   = baseFont,
                    GlyphOffset = new Vector2(0f, -6f),
                    GlyphRanges = new ushort[]
                    {
                        0x0E48, 0x0E4C,
                        0,
                    },
                });

                tk.Font = baseFont;
            }));

        IsOpen = true;
    }

    public void Dispose() => _thaiFont.Dispose();

    public override bool DrawConditions()
        => _config.Enabled && _talkHook.CurrentTokens.Length > 0;

    public override void PreDraw()
    {
        ComputeLayout();

        var addonName = _talkHook.ActiveAddonName;
        var isCutscene = addonName is "TalkSubtitle" or "CutSceneSubtitle" or "CutsceneDialogue";

        if (isCutscene)
        {
            Position = null;
            var io = ImGui.GetIO();
            var screenW = io.DisplaySize.X;
            var screenH = io.DisplaySize.Y;
            var centerX = screenW / 2.0f;
            ImGui.SetNextWindowPos(new Vector2(centerX, screenH - 180.0f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));

            // Allow the box to shrink for short texts, but cap the maximum width at 1100px so it never blows out of the screen
            ImGui.SetNextWindowSizeConstraints(new Vector2(300f, 100f), new Vector2(1100f, 250f));
        }
        else
        {
            Position          = new Vector2(_targetX, _targetY);
            PositionCondition = ImGuiCond.Always;

            ImGui.SetNextWindowSizeConstraints(
                new Vector2(_targetW, 0f),
                new Vector2(_targetW, float.MaxValue));
        }

        ImGui.PushStyleColor(ImGuiCol.WindowBg, BgColor);
        ImGui.PushStyleColor(ImGuiCol.Border,   BorderColor);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1.5f);

        var paddingV = isCutscene ? 14f : PadV;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(PadH, paddingV));
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    // Extra pixels the clip rect extends above the window top so that
    // -6f-shifted tone marks on the first text line are never cut off.
    private const float ClipOvershoot = 10f;

    public override void Draw()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, TextColor);
        // Extra row gap so upward-shifted tone marks don't crowd the line above.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 14f));

        using (_thaiFont.Push())
        {
            // Expand clip rect upward and downward so -6f tone marks and shifted lower characters are not cut.
            var winPos = ImGui.GetWindowPos();
            var winSz  = ImGui.GetWindowSize();
            ImGui.PushClipRect(
                new Vector2(winPos.X, winPos.Y - ClipOvershoot),
                new Vector2(winPos.X + winSz.X, winPos.Y + winSz.Y + ClipOvershoot),
                false);

            var addonName = _talkHook.ActiveAddonName;
            var isCutscene = addonName is "TalkSubtitle" or "CutSceneSubtitle" or "CutsceneDialogue";

            // Token-by-token render loop: each word is a separate ImGui.Text()
            // call so ​ is never needed (avoiding missing-glyph ? artefacts).
            var wrapWidth = isCutscene
                ? (1100f - PadH * 2f - WrapSafeMargin)
                : (_targetW - PadH * 2f - WrapSafeMargin);
            var tokens    = _talkHook.CurrentTokens;

            // Group tokens into lines based on wrap width
            var lines = new List<(List<string> Tokens, float Width)>();
            var currentLine = new List<string>();
            var lineW = 0f;

            foreach (var token in tokens)
            {
                var tokenW = ImGui.CalcTextSize(token).X;
                if (currentLine.Count > 0 && lineW + tokenW <= wrapWidth)
                {
                    currentLine.Add(token);
                    lineW += tokenW;
                }
                else
                {
                    if (currentLine.Count > 0)
                    {
                        lines.Add((currentLine, lineW));
                    }
                    currentLine = new List<string> { token };
                    lineW = tokenW;
                }
            }
            if (currentLine.Count > 0)
            {
                lines.Add((currentLine, lineW));
            }

            if (isCutscene)
            {
                // Vertically center the text block inside the window
                var windowHeight = ImGui.GetWindowSize().Y;
                var linesCount = lines.Count;
                var textHeight = linesCount * _config.FontSize + (linesCount - 1) * 14f;
                var remainingSpace = windowHeight - textHeight;
                if (remainingSpace > 0f)
                {
                    ImGui.SetCursorPosY(remainingSpace * 0.5f);
                }
            }

            var windowWidth = ImGui.GetWindowSize().X;
            var shouldCenter = _isFallbackLayout || isCutscene;

            foreach (var line in lines)
            {
                if (shouldCenter)
                {
                    // Enforce center alignment for fallback and cutscenes
                    var startX = (windowWidth - line.Width) * 0.5f;
                    ImGui.SetCursorPosX(startX);
                }

                for (var j = 0; j < line.Tokens.Count; j++)
                {
                    if (j > 0)
                    {
                        ImGui.SameLine(0, 0);
                    }
                    ImGui.Text(line.Tokens[j]);
                }
            }

            ImGui.PopClipRect();
        }

        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private unsafe void ComputeLayout()
    {
        var addonName = _talkHook.ActiveAddonName;
        var ptr = _gameGui.GetAddonByName(addonName);
        var io = ImGui.GetIO();
        var screenW = io.DisplaySize.X;
        var screenH = io.DisplaySize.Y;

        var isCutscene = addonName is "TalkSubtitle" or "CutSceneSubtitle" or "CutsceneDialogue";

        // Fallback: center on screen if addon not found
        if (ptr.Address == nint.Zero)
        {
            _targetW = Math.Min(900f, screenW * 0.75f);
            _targetX = (screenW - _targetW) / 2f;
            _targetY = screenH * 0.82f;
            _isFallbackLayout = true;
            return;
        }

        var addon  = (AtkUnitBase*)ptr.Address;
        var scale  = addon->Scale;
        var addonX = (float)addon->X;
        var addonY = (float)addon->Y;
        _targetW   = addon->GetScaledWidth(true);

        // Get text node based on addon type
        AtkResNode* textNode = null;
        if (addonName == "Talk")
        {
            textNode = addon->GetNodeById(TextNodeId);
        }
        else if (addonName == "TalkSubtitle" || addonName == "CutSceneSubtitle" || addonName == "CutsceneDialogue")
        {
            textNode = addon->GetNodeById(NameNodeId);
        }
        else
        {
            textNode = FindTextNode(addon->RootNode);
        }

        if (textNode != null)
        {
            _targetX = addonX + PadH;
            _targetY = addonY + textNode->Y * scale;
        }
        else
        {
            _targetX = addonX + PadH;
            _targetY = addonY + 38f * scale;
        }

        _targetW -= PadH * 2f;

        // Safeguard: if addon position is invalid, use screen-centered fallback
        if (_targetW <= 100f || _targetY <= 100f || textNode == null)
        {
            _targetW = Math.Min(900f, screenW * 0.75f);
            _targetX = (screenW - _targetW) / 2f;
            _targetY = screenH * 0.82f;
            _isFallbackLayout = true;
        }
        else
        {
            _isFallbackLayout = false;
            if (_targetW < 400f)
            {
                _targetW = 600f;
                _targetX = addonX + (addon->GetScaledWidth(true) - _targetW) / 2f;
            }
        }
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
