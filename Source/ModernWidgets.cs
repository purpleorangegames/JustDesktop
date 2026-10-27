using System.Linq;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace JustDesktop;

// flat, rounded buttons, checkboxes, radio buttons, sections and tabs, only while a framed tab window
// draws its contents; everything else in the game keeps its look
[StaticConstructorOnStartup]
public static class ModernWidgets
{
    // set by WindowFrame.Content for the window being drawn, cleared when the window is done
    public static bool Active;

    public static bool Enabled => WindowFrame.Modern && JustDesktopMod.Settings.modernWidgets;

    private static Texture2D _fill;

    public static Texture2D Fill => _fill ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/RoundSmall");
    private static Texture2D _line;
    public static Texture2D Line => _line ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/RoundSmallLine");
    private static Texture2D _circleLine;
    private static Texture2D CircleLine => _circleLine ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/CircleLine");
    private static Texture2D _check;
    private static Texture2D Check => _check ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Check");
    // checkboxes are always square, so these are drawn whole instead of in nine pieces
    private static Texture2D _box;
    private static Texture2D Box => _box ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Box");
    private static Texture2D _boxOutline;
    private static Texture2D BoxOutline => _boxOutline ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/BoxLine");
    private static readonly Color ButtonFill = new(1f, 1f, 1f, 0.08f);
    private static readonly Color ButtonHover = new(1f, 1f, 1f, 0.15f);
    private static readonly Color Outline = new(1f, 1f, 1f, 0.1f);
    private static readonly Color SectionFill = new(0f, 0f, 0f, 0.2f);
    private static readonly Color BoxLine = new(1f, 1f, 1f, 0.38f);

    // keeps the alpha of the color the game asked for (it dims disabled things that way)
    private static void Draw(Rect rect, Texture2D atlas, Color color)
    {
        Color old = GUI.color;
        GUI.color = new Color(color.r, color.g, color.b, color.a * old.a);
        Widgets.DrawAtlas(rect, atlas);
        GUI.color = old;
    }

    private static void DrawTexture(Rect rect, Texture2D tex, Color color)
    {
        Color old = GUI.color;
        GUI.color = new Color(color.r, color.g, color.b, color.a * old.a);
        GUI.DrawTexture(rect, tex);
        GUI.color = old;
    }

    // sliders: a thin rounded rail filled in the accent up to a round handle
    private static Rect rail;

    public static void SliderRail(Rect rect, Texture2D atlas)
    {
        if (!Active)
        {
            Widgets.DrawAtlas(rect, atlas);
            return;
        }
        rail = new Rect(rect.x, Mathf.Round(rect.center.y - 2f), rect.width, 4f);
        Draw(rail, Fill, new Color(1f, 1f, 1f, 0.12f));
    }

    public static void SliderHandle(Rect rect, Texture tex)
    {
        if (!Active)
        {
            GUI.DrawTexture(rect, tex);
            return;
        }
        float x = rect.center.x;
        if (x - rail.x >= 3f)
        {
            Draw(new Rect(rail.x, rail.y, x - rail.x, rail.height), Fill, Style.Faded(0.8f));
        }
        Rect knob = new Rect(x - 7f, rect.center.y - 7f, 14f, 14f);
        DrawTexture(knob, Style.Circle, Mouse.IsOver(knob.ExpandedBy(4f)) ? Color.white : new Color(0.9f, 0.92f, 0.95f, 1f));
        DrawTexture(knob.ContractedBy(4f), Style.Circle, Style.Accent);
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.HorizontalSlider), new[] { typeof(Rect), typeof(float), typeof(float), typeof(float), typeof(bool), typeof(string), typeof(string), typeof(string), typeof(float) })]
    public static class Slider
    {
        public static System.Collections.Generic.IEnumerable<CodeInstruction> Transpiler(System.Collections.Generic.IEnumerable<CodeInstruction> instructions)
        {
            instructions = Gizmos.Swap(instructions, AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawAtlas), new[] { typeof(Rect), typeof(Texture2D) }), AccessTools.Method(typeof(ModernWidgets), nameof(SliderRail)));
            return Gizmos.Swap(instructions, Gizmos.DrawTexture, AccessTools.Method(typeof(ModernWidgets), nameof(SliderHandle)));
        }
    }

    // the small X at a dialog's corner: our close glyph, red under the mouse
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.CloseButtonFor))]
    public static class CloseX
    {
        public static bool Prefix(Rect rectToClose, ref bool __result)
        {
            if (!Active)
            {
                return true;
            }
            Rect r = new Rect(rectToClose.xMax - 30f, rectToClose.y + 6f, 24f, 24f);
            bool over = Mouse.IsOver(r);
            if (over)
            {
                DrawTexture(r, Style.Circle, Style.CloseHover);
            }
            DrawTexture(r.ContractedBy(6f), Style.Close, over ? Color.white : Style.Dim);
            __result = Widgets.ButtonInvisible(r);
            return false;
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.DrawButtonGraphic))]
    public static class Button
    {
        public static bool Prefix(Rect rect)
        {
            if (!Active)
            {
                return true;
            }
            bool over = Mouse.IsOver(rect);
            Draw(rect, Fill, over && Input.GetMouseButton(0) ? Style.Faded(0.35f) : over ? ButtonHover : ButtonFill);
            // a button the game tints (a destructive Confirm in red) keeps a bit of that color
            Color tint = GUI.color;
            if (Mathf.Abs(tint.r - tint.g) > 0.15f || Mathf.Abs(tint.g - tint.b) > 0.15f)
            {
                Draw(rect, Fill, new Color(tint.r, tint.g, tint.b, over ? 0.45f : 0.3f));
            }
            Draw(rect, Line, over ? Style.Faded(0.5f) : Outline);
            return false;
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.DrawMenuSection))]
    public static class Section
    {
        public static bool Prefix(Rect rect)
        {
            if (!Active)
            {
                return true;
            }
            Draw(rect, Fill, SectionFill);
            Draw(rect, Line, Outline);
            return false;
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.CheckboxDraw))]
    public static class Checkbox
    {
        // only the game's own checkbox pictures, ones with their own textures stay
        public static bool Prefix(float x, float y, bool active, bool disabled, float size, Texture2D texChecked, Texture2D texUnchecked)
        {
            if (!Active || texChecked != null || texUnchecked != null)
            {
                return true;
            }
            // whole pixels, so the box and the check inside line up
            float inset = Mathf.Round(size * 0.14f);
            Rect box = new Rect(Mathf.Round(x) + inset, Mathf.Round(y) + inset, Mathf.Round(size) - inset * 2f, Mathf.Round(size) - inset * 2f);
            Color old = GUI.color;
            if (disabled)
            {
                GUI.color = new Color(old.r, old.g, old.b, old.a * 0.45f);
            }
            CheckBox(box, active);
            GUI.color = old;
            return false;
        }
    }

    // the box itself, also on the gizmo toggles; over the map (solid) an empty one gets a dark fill so it shows
    public static void CheckBox(Rect box, bool on, bool solid = false)
    {
        if (on)
        {
            DrawTexture(box, Box, Style.Accent);
            DrawTexture(box.ContractedBy(Mathf.Round(box.width * 0.1f)), Check, Color.white);
        }
        else
        {
            DrawTexture(box, Box, solid ? new Color(0.05f, 0.06f, 0.075f, 0.85f) : new Color(1f, 1f, 1f, 0.05f));
            DrawTexture(box, BoxOutline, BoxLine);
        }
    }

    // range sliders (hit points, quality, ages...): the rail and the picked span like our slider, round knobs at both ends
    [HarmonyPatch]
    public static class Ranges
    {
        public static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Widgets), nameof(Widgets.FloatRange));
            yield return AccessTools.Method(typeof(Widgets), nameof(Widgets.IntRange));
            yield return AccessTools.Method(typeof(Widgets), nameof(Widgets.QualityRange));
        }

        public static System.Collections.Generic.IEnumerable<CodeInstruction> Transpiler(System.Collections.Generic.IEnumerable<CodeInstruction> instructions)
        {
            return Gizmos.Swap(instructions, Gizmos.DrawTexture, AccessTools.Method(typeof(ModernWidgets), nameof(RangePart)));
        }
    }

    public static void RangePart(Rect rect, Texture tex)
    {
        if (!Active)
        {
            GUI.DrawTexture(rect, tex);
            return;
        }
        Color old = GUI.color;
        GUI.color = Color.white;
        if (tex == BaseContent.WhiteTex)
        {
            // the whole rail is 2 high, the picked span 4
            Rect r = new Rect(rect.x, Mathf.Round(rect.center.y - 2f), rect.width, 4f);
            Draw(r, Fill, rect.height < 3f ? new Color(1f, 1f, 1f, 0.12f) : Style.Faded(0.8f));
        }
        else
        {
            // the left knob ends at its value, the right one is drawn backwards from its value
            float x = rect.width < 0f ? rect.x + rect.width : rect.xMax;
            Rect knob = new Rect(Mathf.Round(x - 7f), Mathf.Round(rect.center.y - 7f), 14f, 14f);
            DrawTexture(knob, Style.Circle, Mouse.IsOver(knob.ExpandedBy(4f)) ? Color.white : new Color(0.9f, 0.92f, 0.95f, 1f));
            DrawTexture(knob.ContractedBy(4f), Style.Circle, Style.Accent);
        }
        GUI.color = old;
    }

    // the three-state boxes of the storage and bill filters: on and off like our checkboxes, partly as a dash on a softer accent
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.CheckboxMulti))]
    public static class CheckboxMulti
    {
        public static System.Collections.Generic.IEnumerable<CodeInstruction> Transpiler(System.Collections.Generic.IEnumerable<CodeInstruction> instructions)
        {
            var from = AccessTools.Method(typeof(Widgets), nameof(Widgets.ButtonImageDraggable), new[] { typeof(Rect), typeof(Texture2D) });
            return Gizmos.Swap(instructions, from, AccessTools.Method(typeof(ModernWidgets), nameof(MultiBox)));
        }
    }

    public static Widgets.DraggableResult MultiBox(Rect rect, Texture2D tex)
    {
        if (!Active)
        {
            return Widgets.ButtonImageDraggable(rect, tex);
        }
        float inset = Mathf.Round(rect.width * 0.14f);
        Rect box = new Rect(Mathf.Round(rect.x) + inset, Mathf.Round(rect.y) + inset, Mathf.Round(rect.width) - inset * 2f, Mathf.Round(rect.height) - inset * 2f);
        if (tex == Widgets.CheckboxOnTex || tex == Widgets.CheckboxOffTex)
        {
            CheckBox(box, tex == Widgets.CheckboxOnTex);
        }
        else
        {
            DrawTexture(box, Box, Style.Faded(0.45f));
            float h = Mathf.Max(2f, Mathf.Round(box.height * 0.16f));
            Draw(new Rect(box.x + box.width * 0.25f, Mathf.Round(box.center.y - h / 2f), box.width * 0.5f, h), Fill, Color.white);
        }
        if (Mouse.IsOver(rect))
        {
            DrawTexture(box, Box, new Color(1f, 1f, 1f, 0.12f));
        }
        return Widgets.ButtonInvisibleDraggable(rect);
    }

    [HarmonyPatch(typeof(Widgets), "RadioButtonDraw")]
    public static class Radio
    {
        public static bool Prefix(float x, float y, bool chosen, bool disabled)
        {
            if (!Active)
            {
                return true;
            }
            Rect ring = new Rect(x, y, 24f, 24f).ContractedBy(3f);
            Color dim = disabled ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
            Color old = GUI.color;
            GUI.color = dim;
            if (chosen)
            {
                DrawTexture(ring, CircleLine, Style.Accent);
                DrawTexture(ring.ContractedBy(4.5f), Style.Circle, Style.Accent);
            }
            else
            {
                DrawTexture(ring, CircleLine, BoxLine);
            }
            GUI.color = old;
            return false;
        }
    }

    // the work tab's boxes: a rounded square that gets lighter the better the skill, the game's warnings and passion flames on it
    [HarmonyPatch(typeof(RimWorld.WidgetsWork), "DrawWorkBoxBackground")]
    public static class WorkBox
    {
        private static readonly Color Awful = new(0.11f, 0.12f, 0.13f), Bad = new(0.16f, 0.17f, 0.19f), Mid = new(0.24f, 0.25f, 0.28f), Excellent = new(0.34f, 0.35f, 0.39f);

        public static bool Prefix(Rect rect, Pawn p, WorkTypeDef workDef)
        {
            if (!Active)
            {
                return true;
            }
            float skill = p.skills.AverageOfRelevantSkillsFor(workDef);
            Color fill = skill < 4f ? Color.Lerp(Awful, Bad, skill / 4f) : skill <= 14f ? Color.Lerp(Bad, Mid, (skill - 4f) / 10f) : Color.Lerp(Mid, Excellent, (skill - 14f) / 6f);
            Color tint = GUI.color;
            Rect box = new Rect(Mathf.Round(rect.x) + 1f, Mathf.Round(rect.y) + 1f, Mathf.Round(rect.width) - 2f, Mathf.Round(rect.height) - 2f);
            GUI.color = new Color(fill.r * tint.r, fill.g * tint.g, fill.b * tint.b, 1f);
            GUI.DrawTexture(box, Box);
            GUI.color = new Color(1f, 1f, 1f, 0.07f);
            GUI.DrawTexture(box, BoxOutline);
            if (p.Ideo != null && p.Ideo.IsWorkTypeConsideredDangerous(workDef))
            {
                GUI.color = Color.white;
                GUI.DrawTexture(rect, RimWorld.WidgetsWork.WorkBoxOverlay_PreceptWarning);
            }
            if (workDef.relevantSkills.Any() && skill <= 2f && p.workSettings.WorkIsActive(workDef))
            {
                GUI.color = Color.white;
                GUI.DrawTexture(rect.ContractedBy(-2f), RimWorld.WidgetsWork.WorkBoxOverlay_Warning);
            }
            RimWorld.Passion passion = p.skills.MaxPassionOfRelevantSkillsFor(workDef);
            if (passion > RimWorld.Passion.None)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                Rect flame = new Rect(rect.center.x, rect.center.y, rect.width / 2f, rect.height / 2f);
                GUI.DrawTexture(flame, ContentFinder<Texture2D>.Get(passion == RimWorld.Passion.Major ? "UI/Icons/PassionMajorGray" : "UI/Icons/PassionMinorGray"));
            }
            GUI.color = Color.white;
            return false;
        }
    }

    // the check mark when manual priorities are off
    public static Texture2D WorkCheck(Texture2D vanilla) => Active ? Check : vanilla;

    [HarmonyPatch(typeof(RimWorld.WidgetsWork), nameof(RimWorld.WidgetsWork.DrawWorkBoxFor))]
    public static class WorkBoxCheck
    {
        public static System.Collections.Generic.IEnumerable<CodeInstruction> Transpiler(System.Collections.Generic.IEnumerable<CodeInstruction> instructions)
        {
            var field = AccessTools.Field(typeof(RimWorld.WidgetsWork), nameof(RimWorld.WidgetsWork.WorkBoxCheckTex));
            var swap = AccessTools.Method(typeof(ModernWidgets), nameof(WorkCheck));
            foreach (CodeInstruction ins in instructions)
            {
                yield return ins;
                if (ins.LoadsField(field))
                {
                    yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call, swap);
                }
            }
        }
    }

    [HarmonyPatch(typeof(TabRecord), nameof(TabRecord.Draw))]
    public static class Tab
    {
        public static bool Prefix(TabRecord __instance, Rect rect)
        {
            if (!Active)
            {
                return true;
            }
            bool selected = __instance.Selected;
            bool over = Mouse.IsOver(rect);
            Rect back = new Rect(rect.x + 2f, rect.y + 3f, rect.width - 4f, rect.height - 3f);
            Draw(back, Fill, selected ? new Color(1f, 1f, 1f, 0.12f) : over ? ButtonFill : new Color(1f, 1f, 1f, 0.04f));
            if (selected)
            {
                Widgets.DrawBoxSolid(new Rect(back.x + 6f, back.yMax - 2f, back.width - 12f, 2f), Style.Accent);
            }
            if (!__instance.TutorTag.NullOrEmpty())
            {
                UIHighlighter.HighlightOpportunity(rect, __instance.TutorTag);
            }
            GUI.color = __instance.labelColor ?? (selected || over ? Color.white : Style.Text);
            // a label too long for its tab goes small, and is cut short if even that doesn't fit
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.WordWrap = false;
            float room = rect.width - 10f;
            string label = __instance.label;
            if (Text.CalcSize(label).x > room)
            {
                Text.Font = GameFont.Tiny;
                label = label.Truncate(room);
            }
            Widgets.Label(rect, label);
            Text.WordWrap = true;
            Text.Font = font;
            Text.Anchor = anchor;
            GUI.color = Color.white;
            return false;
        }
    }
}

// the flag goes off when a window finishes drawing, so nothing outside the framed windows is restyled
[HarmonyPatch(typeof(Window), "InnerWindowOnGUI")]
public static class Window_InnerWindowOnGUI_End
{
    public static void Postfix()
    {
        ModernWidgets.Active = false;
    }
}

// highlights and boxes in the modern windows: rounded, the selected one in the accent
[HarmonyPatch]
public static class ModernHighlights
{
    private static readonly Color Light = new(1f, 1f, 1f, 0.05f);
    private static readonly Color Normal = new(1f, 1f, 1f, 0.1f);

    private static void Fill(Rect rect, Color color)
    {
        Color old = GUI.color;
        GUI.color = new Color(color.r, color.g, color.b, color.a * old.a);
        Widgets.DrawAtlas(rect, ModernWidgets.Fill);
        GUI.color = old;
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.DrawHighlight), new[] { typeof(Rect) })]
    [HarmonyPrefix]
    public static bool Highlight(Rect rect)
    {
        if (!ModernWidgets.Active)
        {
            return true;
        }
        Fill(rect, Normal);
        return false;
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.DrawHighlight), new[] { typeof(Rect), typeof(float) })]
    [HarmonyPrefix]
    public static bool HighlightOpacity(Rect rect, float opacity)
    {
        if (!ModernWidgets.Active)
        {
            return true;
        }
        Fill(rect, new Color(1f, 1f, 1f, 0.1f * opacity));
        return false;
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.DrawLightHighlight))]
    [HarmonyPrefix]
    public static bool LightHighlight(Rect rect)
    {
        if (!ModernWidgets.Active)
        {
            return true;
        }
        Fill(rect, Light);
        return false;
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.DrawHighlightSelected))]
    [HarmonyPrefix]
    public static bool Selected(Rect rect)
    {
        if (!ModernWidgets.Active)
        {
            return true;
        }
        Fill(rect, Style.Faded(0.25f));
        Color old = GUI.color;
        GUI.color = Style.Faded(0.7f * old.a);
        Widgets.DrawAtlas(rect, ModernWidgets.Line);
        GUI.color = old;
        return false;
    }

    // a box's border as a rounded line in the color asked for
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.DrawBox))]
    [HarmonyPrefix]
    public static bool Box(Rect rect, int thickness, Texture2D lineTexture)
    {
        if (!ModernWidgets.Active || lineTexture != null)
        {
            return true;
        }
        Widgets.DrawAtlas(rect, ModernWidgets.Line);
        if (thickness > 1)
        {
            Widgets.DrawAtlas(rect.ContractedBy(1f), ModernWidgets.Line);
        }
        return false;
    }
}
