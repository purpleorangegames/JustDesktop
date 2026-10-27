using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace JustDesktop;

// the main menu in the modern look: the buttons and the links each on a rounded card, the buttons flat. The
// options window gets the rounded background and our buttons, checkboxes and sliders through WindowFrame, and its
// categories are drawn like the pages of our own options
public static class MainMenu
{
    private static bool On => Current.ProgramState == ProgramState.Entry && ModernWidgets.Enabled;

    // in place of OptionListingUtility.DrawOptionListing: a card behind the list first
    private static readonly Rect[] cards = new Rect[2];

    public static float Listing(Rect rect, List<ListableOption> options)
    {
        if (ModernWidgets.Active)
        {
            float height = 0f;
            Text.Font = GameFont.Small;
            foreach (ListableOption o in options)
            {
                float w = o is ListableOption_WebLink ? rect.width - 27f : rect.width;
                height += Mathf.Max(o.minHeight, Text.CalcHeight(o.label, w)) + 7f;
            }
            // drawn next frame before the group starts, which would cut it at its edges
            cards[rect.x > 0f ? 1 : 0] = new Rect(rect.x, rect.y, rect.width, height - 7f);
        }
        return OptionListingUtility.DrawOptionListing(rect, options);
    }

    // the game's dark gradient behind the links, not needed with the card
    public static void WebBackground(Rect rect, Texture2D atlas)
    {
        if (!ModernWidgets.Active)
        {
            Widgets.DrawAtlas(rect, atlas);
        }
    }

    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.DoMainMenuControls))]
    public static class Controls
    {
        public static void Prefix(Rect rect, out bool __state)
        {
            __state = ModernWidgets.Active;
            if (On)
            {
                ModernWidgets.Active = true;
                foreach (Rect card in cards)
                {
                    if (card.height > 0f)
                    {
                        Pane.CardBack(new Rect(rect.x + card.x, rect.y + card.y, card.width, card.height).ExpandedBy(12f));
                    }
                }
            }
        }

        public static void Postfix(bool __state)
        {
            ModernWidgets.Active = __state;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo listing = AccessTools.Method(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing));
            MethodInfo atlas = AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawAtlas), new[] { typeof(Rect), typeof(Texture2D) });
            instructions = Gizmos.Swap(instructions, listing, AccessTools.Method(typeof(MainMenu), nameof(Listing)));
            return Gizmos.Swap(instructions, atlas, AccessTools.Method(typeof(MainMenu), nameof(WebBackground)));
        }
    }

    // tooltips on a rounded card in the window color
    [HarmonyPatch(typeof(ActiveTip), "DrawInner")]
    public static class Tooltip
    {
        public static bool Prefix(Rect bgRect, string label)
        {
            if (!Loading.Ready || !ModernWidgets.Enabled)
            {
                return true;
            }
            Color back = JustDesktopMod.Settings.windowColor;
            back.a = 0.97f;
            GUI.color = back;
            Widgets.DrawAtlas(bgRect, ModernWidgets.Fill);
            GUI.color = new Color(1f, 1f, 1f, 0.14f);
            Widgets.DrawAtlas(bgRect, ModernWidgets.Line);
            GUI.color = Style.Text;
            Text.Font = GameFont.Small;
            Widgets.Label(bgRect.ContractedBy(4f), label);
            GUI.color = Color.white;
            return false;
        }
    }

    // float menus: the options on the window color, the one under the mouse in the accent, no frame around each
    public static void OptionBack(Rect rect, Texture tex)
    {
        if (!Loading.Ready || !ModernWidgets.Enabled || tex != BaseContent.WhiteTex)
        {
            GUI.DrawTexture(rect, tex);
            return;
        }
        Color asked = GUI.color;
        bool disabled = Mathf.Abs(asked.r - FloatMenuOption.ColorBGDisabled.r * asked.a) < 0.02f && Mathf.Abs(asked.b - FloatMenuOption.ColorBGDisabled.b * asked.a) < 0.02f;
        bool hover = Mathf.Abs(asked.b - FloatMenuOption.ColorBGActiveMouseover.b * asked.a) < 0.02f && Mathf.Abs(asked.g - FloatMenuOption.ColorBGActiveMouseover.g * asked.a) < 0.02f;
        Color back = JustDesktopMod.Settings.windowColor;
        back.a = asked.a;
        GUI.color = back;
        GUI.DrawTexture(rect, BaseContent.WhiteTex);
        if (hover)
        {
            GUI.color = Style.Faded(0.35f * asked.a);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
        }
        else if (disabled)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.3f * asked.a);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
        }
        GUI.color = asked;
    }

    public static void OptionFrame(Rect rect, Texture2D atlas)
    {
        if (!Loading.Ready || !ModernWidgets.Enabled)
        {
            Widgets.DrawAtlas(rect, atlas);
            return;
        }
        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.05f * old.a);
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), BaseContent.WhiteTex);
        GUI.color = old;
    }

    [HarmonyPatch(typeof(FloatMenuOption), nameof(FloatMenuOption.DoGUI))]
    public static class FloatOption
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo atlas = AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawAtlas), new[] { typeof(Rect), typeof(Texture2D) });
            instructions = Gizmos.Swap(instructions, Gizmos.DrawTexture, AccessTools.Method(typeof(MainMenu), nameof(OptionBack)));
            return Gizmos.Swap(instructions, atlas, AccessTools.Method(typeof(MainMenu), nameof(OptionFrame)));
        }
    }

    // the options' categories: a soft pill, the open one in the accent with a bar at its left
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.DrawOptionBackground))]
    public static class OptionBackground
    {
        public static bool Prefix(Rect rect, bool selected)
        {
            if (!ModernWidgets.Active)
            {
                return true;
            }
            bool over = Mouse.IsOver(rect);
            GUI.color = selected ? Style.Faded(0.3f) : over ? new Color(1f, 1f, 1f, 0.1f) : new Color(1f, 1f, 1f, 0.04f);
            Widgets.DrawAtlas(rect, ModernWidgets.Fill);
            if (selected)
            {
                GUI.color = Style.Accent;
                GUI.DrawTexture(new Rect(rect.x + 3f, rect.y + 8f, 3f, rect.height - 16f), BaseContent.WhiteTex);
            }
            GUI.color = Color.white;
            return false;
        }
    }
}
