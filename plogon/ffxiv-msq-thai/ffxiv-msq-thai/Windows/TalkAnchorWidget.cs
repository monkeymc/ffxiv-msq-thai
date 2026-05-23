using System;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FfxivMsqThai.Windows;

public sealed class TalkAnchorWidget : Window, IDisposable
{
    // ── Button geometry ───────────────────────────────────────────────────────
    private const float BtnW    = 44f;
    private const float BtnH    = 26f;
    private const float MarginH =  8f;
    private const float MarginV =  5f;

    // ── Palette ───────────────────────────────────────────────────────────────
    private static readonly Vector4 BtnBg        = new(0.18f, 0.14f, 0.08f, 0.92f);
    private static readonly Vector4 BtnBgHover   = new(0.32f, 0.25f, 0.10f, 0.98f);
    private static readonly Vector4 BtnBgActive  = new(0.38f, 0.30f, 0.12f, 1.00f);
    private static readonly Vector4 BtnBorder    = new(0.72f, 0.58f, 0.28f, 0.90f);
    private static readonly Vector4 BtnBorderHot = new(1.00f, 0.86f, 0.32f, 1.00f);
    private static readonly Vector4 TextEnabled  = new(0.95f, 0.80f, 0.35f, 1.00f);
    private static readonly Vector4 TextDisab    = new(0.60f, 0.55f, 0.50f, 1.00f);

    private static readonly Vector4 PopupBg      = new(0.11f, 0.10f, 0.08f, 0.97f);
    private static readonly Vector4 PopupBorder  = new(0.55f, 0.47f, 0.30f, 0.85f);
    private static readonly Vector4 HeaderColor  = new(0.78f, 0.68f, 0.44f, 1.00f);

    private static readonly Vector4 RadioDot     = new(0.85f, 0.70f, 0.28f, 1.00f);
    private static readonly Vector4 RadioDotHov  = new(0.95f, 0.82f, 0.38f, 1.00f);
    private static readonly Vector4 RadioFrmHov  = new(0.72f, 0.60f, 0.32f, 1.00f);

    private static readonly Vector4 FntBg        = new(0.20f, 0.17f, 0.10f, 0.85f);
    private static readonly Vector4 FntHover     = new(0.72f, 0.58f, 0.28f, 0.25f);
    private static readonly Vector4 FntActive    = new(0.72f, 0.58f, 0.28f, 0.45f);
    private static readonly Vector4 FntBorder    = new(0.55f, 0.47f, 0.30f, 0.70f);
    private static readonly Vector4 FntText      = new(0.90f, 0.80f, 0.55f, 1.00f);

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly PluginConfig _config;
    private readonly IDalamudPluginInterface _pi;
    private readonly IGameGui _gameGui;

    // Fixed 16 px Thai font — menu labels only, independent of overlay font size
    private readonly IFontHandle _menuFont;

    public TalkAnchorWidget(
        PluginConfig config,
        IDalamudPluginInterface pi,
        IGameGui gameGui)
        : base("##msq-th-anchor",
            ImGuiWindowFlags.NoTitleBar        |
            ImGuiWindowFlags.NoResize          |
            ImGuiWindowFlags.NoMove            |
            ImGuiWindowFlags.NoScrollbar       |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoCollapse        |
            ImGuiWindowFlags.NoSavedSettings   |
            ImGuiWindowFlags.NoFocusOnAppearing|
            ImGuiWindowFlags.NoNav             |
            ImGuiWindowFlags.NoBackground      |
            ImGuiWindowFlags.AlwaysAutoResize)
    {
        _config  = config;
        _pi      = pi;
        _gameGui = gameGui;

        var fontPath = Path.Combine(
            pi.AssemblyLocation.Directory!.FullName,
            "THSarabunNew-Bold.ttf");

        _menuFont = pi.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
            e.OnPreBuild(tk => tk.AddFontFromFile(fontPath,
                new SafeFontConfig { SizePx = 26f })));

        IsOpen = true;
    }

    public void Dispose() => _menuFont.Dispose();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override unsafe bool DrawConditions()
    {
        var ptr = _gameGui.GetAddonByName("Talk");
        if (ptr.Address == nint.Zero) return false;
        return ((AtkUnitBase*)ptr.Address)->IsVisible;
    }

    public override unsafe void PreDraw()
    {
        var ptr    = _gameGui.GetAddonByName("Talk");
        var addonX = 0f;
        var addonY = 0f;
        var addonW = BtnW + MarginH * 2f;

        if (ptr.Address != nint.Zero)
        {
            var a  = (AtkUnitBase*)ptr.Address;
            addonX = (float)a->X;
            addonY = (float)a->Y;
            addonW = a->GetScaledWidth(true);
        }

        Position          = new Vector2(addonX + addonW - BtnW - MarginH, addonY + MarginV);
        PositionCondition = ImGuiCond.Always;
        SizeConstraints   = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(BtnW, BtnH),
            MaximumSize = new Vector2(BtnW, BtnH)
        };

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,  new Vector2(6f, 3f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(3);
    }

    public override void Draw()
    {
        DrawAnchorButton();
        DrawMainPopup();
        DrawAdvancedPopup();
    }

    // ── Anchor button ─────────────────────────────────────────────────────────

    private void DrawAnchorButton()
    {
        var label     = _config.Enabled ? "TH" : "EN";
        var textColor = _config.Enabled ? TextEnabled : TextDisab;

        var winPos      = ImGui.GetWindowPos();
        var hovered     = ImGui.IsMouseHoveringRect(winPos, winPos + new Vector2(BtnW, BtnH), false);
        var borderColor = hovered ? BtnBorderHot : BtnBorder;

        ImGui.PushStyleColor(ImGuiCol.Text,          textColor);
        ImGui.PushStyleColor(ImGuiCol.Button,        BtnBg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BtnBgHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive,  BtnBgActive);
        ImGui.PushStyleColor(ImGuiCol.Border,        borderColor);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1.5f);

        if (ImGui.Button(label, new Vector2(BtnW, BtnH)))
            ImGui.OpenPopup("##mainmenu");

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup("##advmenu");

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(5);
    }

    // ── Left-click popup ──────────────────────────────────────────────────────

    private void DrawMainPopup()
    {
        PushPopupStyle();
        if (ImGui.BeginPopup("##mainmenu"))
        {
            // ── Language section — labels in Thai font ────────────────────────
            using (_menuFont.Push())
            {
                SectionHeader("เลือกภาษาเนื้อเรื่อง");

                PushRadioStyle();

                bool clickTh = ImGui.RadioButton("##th", _config.Enabled);
                ImGui.SameLine(0, 8f);
                ImGui.Text("ภาษาไทย");
                if (clickTh && !_config.Enabled)
                {
                    _config.Enabled = true;
                    _config.Save();
                }

                bool clickEn = ImGui.RadioButton("##en", !_config.Enabled);
                ImGui.SameLine(0, 8f);
                ImGui.Text("ภาษาอังกฤษ");
                if (clickEn && _config.Enabled)
                {
                    _config.Enabled = false;
                    _config.Save();
                }

                PopRadioStyle();
            }

            ImGui.Spacing();

            // ── Font size section — header in Thai font, controls in default ──
            using (_menuFont.Push())
                SectionHeader("ขนาดตัวอักษร");

            // - / value / + row — same font + same FramePadding on all three items
            // so AlignTextToFramePadding() calculates the correct vertical offset.
            FontScaleButton(" - ", -2f);
            ImGui.SameLine(0f, 8f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(14f, 6f));
            ImGui.AlignTextToFramePadding();
            ImGui.PopStyleVar();
            using (_menuFont.Push())
            {
                ImGui.PushStyleColor(ImGuiCol.Text, FntText);
                ImGui.Text($"{_config.FontSize:0} px");
                ImGui.PopStyleColor();
            }
            ImGui.SameLine(0f, 8f);
            FontScaleButton(" + ", +2f);

            ImGui.EndPopup();
        }
        PopPopupStyle();
    }

    // ── Right-click advanced popup ────────────────────────────────────────────

    private void DrawAdvancedPopup()
    {
        PushPopupStyle();
        if (ImGui.BeginPopup("##advmenu"))
        {
            using (_menuFont.Push())
            {
                SectionHeader("สถานะการแปล");
                ImGui.Text("  เป็นปัจจุบัน");

#if DEBUG
                ImGui.Spacing();
                SectionHeader("นักพัฒนา");
                ImGui.TextDisabled("Content root:");
                ImGui.TextDisabled(_config.ContentRoot);
#endif
            }

            ImGui.EndPopup();
        }
        PopPopupStyle();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void FontScaleButton(string label, float delta)
    {
        ImGui.PushStyleColor(ImGuiCol.Text,          FntText);
        ImGui.PushStyleColor(ImGuiCol.Button,        FntBg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, FntHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive,  FntActive);
        ImGui.PushStyleColor(ImGuiCol.Border,        FntBorder);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding,   5f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,    new Vector2(14f, 6f));

        bool clicked;
        using (_menuFont.Push())
            clicked = ImGui.Button(label);

        if (clicked)
        {
            _config.FontSize = Math.Clamp(_config.FontSize + delta, 10f, 60f);
            _config.Save();
            _pi.UiBuilder.FontAtlas.BuildFontsAsync();
        }

        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(5);
    }

    private static void PushRadioStyle()
    {
        ImGui.PushStyleColor(ImGuiCol.CheckMark,      RadioDot);
        ImGui.PushStyleColor(ImGuiCol.FrameBg,        new Vector4(0.15f, 0.12f, 0.07f, 0.90f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, RadioFrmHov);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive,  RadioDotHov);
    }

    private static void PopRadioStyle() => ImGui.PopStyleColor(4);

    private static void SectionHeader(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, HeaderColor);
        ImGui.Text(text);
        ImGui.PopStyleColor();
        ImGui.Separator();
    }

    private static void PushPopupStyle()
    {
        ImGui.PushStyleColor(ImGuiCol.PopupBg, PopupBg);
        ImGui.PushStyleColor(ImGuiCol.Border,  PopupBorder);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,   new Vector2(12f, 10f));
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,     new Vector2(8f, 7f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding,   5f);
    }

    private static void PopPopupStyle()
    {
        ImGui.PopStyleVar(4);
        ImGui.PopStyleColor(2);
    }
}
