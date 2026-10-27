using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace JustDesktop;

// picking which resources the list at the top left shows. The eye on its card puts a checkbox on every line and
// hides the unchecked ones when clicked again; the list button opens a window with everything that can be counted,
// with a search, to pick ones you don't have yet. Kept in the mod settings, so every colony uses the same picks
public static class Resources
{
    public const float Header = 24f;

    private static Settings S => JustDesktopMod.Settings;
    public static bool On => Loading.Ready;
    public static bool Editing;

    private static HashSet<string> hidden;

    private static HashSet<string> Hidden => hidden ??= new HashSet<string>(S.hiddenResources);

    private static Texture2D _eye;
    private static Texture2D Eye => _eye ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Hud/Eye");
    private static Texture2D _list;
    private static Texture2D ListIcon => _list ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Hud/List");

    public static string Key(ThingDef def) => "thing:" + def.defName;
    public static string Key(ThingCategoryDef def) => "cat:" + def.defName;

    public static bool IsHidden(string key) => Hidden.Contains(key);

    // hidden only when not picking
    public static bool Skip(string key) => On && !Editing && Hidden.Contains(key);

    public static void Set(string key, bool show)
    {
        if (show ? Hidden.Remove(key) : Hidden.Add(key))
        {
            S.hiddenResources = Hidden.ToList();
            LoadedModManager.GetMod<JustDesktopMod>().WriteSettings();
        }
    }

    public static void ShowAll()
    {
        Hidden.Clear();
        S.hiddenResources = new List<string>();
        LoadedModManager.GetMod<JustDesktopMod>().WriteSettings();
    }

    // the two buttons at the top of the card
    public static void DrawHeader(Rect card)
    {
        Rect eye = new Rect(card.x + 6f, card.y + 4f, 18f, 18f);
        Rect list = new Rect(eye.xMax + 6f, eye.y, 18f, 18f);
        if (Button(eye, Eye, Editing))
        {
            Editing = !Editing;
            (Editing ? SoundDefOf.Tick_High : SoundDefOf.Tick_Low).PlayOneShotOnCamera();
        }
        TooltipHandler.TipRegion(eye, Editing ? "Done: hide the unchecked ones" : "Pick which resources show here");
        bool open = Find.WindowStack.IsOpen<Dialog_Resources>();
        if (Button(list, ListIcon, open))
        {
            if (open)
            {
                Find.WindowStack.TryRemove(typeof(Dialog_Resources));
            }
            else
            {
                Find.WindowStack.Add(new Dialog_Resources());
            }
        }
        TooltipHandler.TipRegion(list, "All resources, to pick ones you don't have yet");
        if (Editing)
        {
            GUI.color = Style.Accent;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(list.xMax + 6f, card.y + 2f, card.xMax - list.xMax - 8f, 22f), "Picking");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }
    }

    private static bool Button(Rect r, Texture2D tex, bool on)
    {
        bool over = Mouse.IsOver(r.ExpandedBy(2f));
        GUI.color = on ? Style.Accent : over ? Color.white : Style.Dim;
        GUI.DrawTexture(r, tex);
        GUI.color = Color.white;
        return Widgets.ButtonInvisible(r.ExpandedBy(2f));
    }

    // a checkbox at the right of a line while picking
    public static void Check(Rect row, string key)
    {
        if (!On || !Editing)
        {
            return;
        }
        float size = 16f;
        Rect box = new Rect(row.xMax - size - 2f, row.center.y - size / 2f, size, size);
        bool show = !IsHidden(key);
        ModernWidgets.CheckBox(box, show, solid: true);
        if (Widgets.ButtonInvisible(box.ExpandedBy(3f)))
        {
            Set(key, !show);
            (show ? SoundDefOf.Checkbox_TurnedOff : SoundDefOf.Checkbox_TurnedOn).PlayOneShotOnCamera();
        }
    }
}

// the plain list, the game's loop with the hidden ones left out and the checkboxes while picking
[HarmonyPatch(typeof(ResourceReadout), "DoReadoutSimple")]
public static class Resources_Simple
{
    private static readonly System.Action<ResourceReadout, Rect, ThingDef> drawResource = AccessTools.MethodDelegate<System.Action<ResourceReadout, Rect, ThingDef>>(AccessTools.Method(typeof(ResourceReadout), nameof(ResourceReadout.DrawResourceSimple)));

    public static bool Prefix(ResourceReadout __instance, Rect rect, float outRectHeight, Vector2 ___scrollPosition, ref float ___lastDrawnHeight)
    {
        if (!Resources.On)
        {
            return true;
        }
        Widgets.BeginGroup(rect);
        Text.Anchor = TextAnchor.MiddleLeft;
        float y = 0f;
        foreach (KeyValuePair<ThingDef, int> pair in Find.CurrentMap.resourceCounter.AllCountedAmounts)
        {
            if ((pair.Value > 0 || pair.Key.resourceReadoutAlwaysShow) && !Resources.Skip(Resources.Key(pair.Key)))
            {
                Rect row = new Rect(0f, y, 999f, 24f);
                if (row.yMax >= ___scrollPosition.y && row.y <= ___scrollPosition.y + outRectHeight)
                {
                    drawResource(__instance, row, pair.Key);
                    Resources.Check(new Rect(0f, y, rect.width, 24f), Resources.Key(pair.Key));
                }
                y += 24f;
            }
        }
        Text.Anchor = TextAnchor.UpperLeft;
        ___lastDrawnHeight = y;
        Widgets.EndGroup();
        return false;
    }
}

// the list by categories: a hidden category or item isn't drawn, while picking each line gets its checkbox
[HarmonyPatch(typeof(Listing_ResourceReadout), nameof(Listing_ResourceReadout.DoCategory))]
public static class Resources_Category
{
    private static readonly AccessTools.FieldRef<Listing, float> curY = AccessTools.FieldRefAccess<Listing, float>("curY");

    public static bool Prefix(Listing_ResourceReadout __instance, TreeNode_ThingCategory node, out float __state)
    {
        __state = curY(__instance);
        return !Resources.Skip(Resources.Key(node.catDef));
    }

    public static void Postfix(Listing_ResourceReadout __instance, TreeNode_ThingCategory node, float __state)
    {
        if (curY(__instance) > __state)
        {
            Resources.Check(new Rect(0f, __state, __instance.ColumnWidth, __instance.lineHeight), Resources.Key(node.catDef));
        }
    }
}

[HarmonyPatch(typeof(Listing_ResourceReadout), "DoThingDef")]
public static class Resources_Thing
{
    private static readonly AccessTools.FieldRef<Listing, float> curY = AccessTools.FieldRefAccess<Listing, float>("curY");

    public static bool Prefix(Listing_ResourceReadout __instance, ThingDef thingDef, out float __state)
    {
        __state = curY(__instance);
        return !Resources.Skip(Resources.Key(thingDef));
    }

    public static void Postfix(Listing_ResourceReadout __instance, ThingDef thingDef, float __state)
    {
        if (curY(__instance) > __state)
        {
            Resources.Check(new Rect(0f, __state, __instance.ColumnWidth, __instance.lineHeight), Resources.Key(thingDef));
        }
    }
}

// everything the resource list can count, with a search: categories first, then the items, each with how many you have
public class Dialog_Resources : Window
{
    private string search = "";
    private Vector2 scroll;
    private bool onlyHave;
    private readonly List<ThingCategoryDef> categories;
    private readonly List<ThingDef> things;

    public override Vector2 InitialSize => new(480f, Mathf.Min(720f, UI.screenHeight - 120f));

    public Dialog_Resources()
    {
        closeOnClickedOutside = false;
        absorbInputAroundWindow = false;
        preventCameraMotion = false;
        draggable = true;
        var roots = DefDatabase<ThingCategoryDef>.AllDefsListForReading.Where(c => c.resourceReadoutRoot).ToList();
        categories = new List<ThingCategoryDef>();
        foreach (ThingCategoryDef root in roots)
        {
            Collect(root, categories);
        }
        things = Find.CurrentMap?.resourceCounter.AllCountedAmounts.Keys.OrderBy(d => d.label).ToList() ?? new List<ThingDef>();
    }

    private static void Collect(ThingCategoryDef cat, List<ThingCategoryDef> into)
    {
        if (into.Contains(cat))
        {
            return;
        }
        into.Add(cat);
        foreach (ThingCategoryDef child in cat.childCategories)
        {
            if (!child.resourceReadoutRoot)
            {
                Collect(child, into);
            }
        }
    }

    private bool Matches(string label) => search.NullOrEmpty() || label.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0;

    public override void DoWindowContents(Rect inRect)
    {
        Map map = Find.CurrentMap;
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 30f, 32f), "Resources");
        Text.Font = GameFont.Small;
        Rect close = new Rect(inRect.xMax - 22f, inRect.y + 4f, 20f, 20f);
        bool over = Mouse.IsOver(close.ExpandedBy(4f));
        if (over)
        {
            GUI.color = Style.CloseHover;
            GUI.DrawTexture(close.ExpandedBy(4f), Style.Circle);
        }
        GUI.color = over ? Color.white : Style.Dim;
        GUI.DrawTexture(close.ContractedBy(3f), Style.Close);
        GUI.color = Color.white;
        if (Widgets.ButtonInvisible(close.ExpandedBy(4f)))
        {
            Close();
        }
        float y = inRect.y + 38f;
        Rect searchRect = new Rect(inRect.x, y, inRect.width, 28f);
        GUI.DrawTexture(new Rect(searchRect.x, searchRect.y + 5f, 18f, 18f), TexButton.Search);
        search = Widgets.TextField(new Rect(searchRect.x + 24f, searchRect.y, searchRect.width - 24f, 28f), search);
        y += 34f;
        Widgets.CheckboxLabeled(new Rect(inRect.x, y, 260f, 24f), "Only what you have", ref onlyHave);
        if (Widgets.ButtonText(new Rect(inRect.xMax - 110f, y - 2f, 110f, 28f), "Show all"))
        {
            Resources.ShowAll();
        }
        y += 32f;

        Rect outRect = new Rect(inRect.x, y, inRect.width, inRect.yMax - y);
        float rowH = 28f;
        int rows;
        var shownCats = categories.Where(c => Matches(c.label) && (!onlyHave || map == null || map.resourceCounter.GetCountIn(c) > 0)).ToList();
        var shownThings = things.Where(t => Matches(t.label) && (!onlyHave || map == null || map.resourceCounter.GetCount(t) > 0)).ToList();
        rows = shownCats.Count + shownThings.Count + 2;
        Rect view = new Rect(0f, 0f, outRect.width - 16f, rows * rowH);
        Widgets.BeginScrollView(outRect, ref scroll, view);
        float ry = 0f;
        Heading(ref ry, view.width, "Categories");
        foreach (ThingCategoryDef c in shownCats)
        {
            Row(new Rect(0f, ry, view.width, rowH), c.icon, c.LabelCap, map?.resourceCounter.GetCountIn(c) ?? 0, Resources.Key(c), c.description);
            ry += rowH;
        }
        Heading(ref ry, view.width, "Items");
        foreach (ThingDef t in shownThings)
        {
            Row(new Rect(0f, ry, view.width, rowH), null, t.LabelCap, map?.resourceCounter.GetCount(t) ?? 0, Resources.Key(t), t.description, t);
            ry += rowH;
        }
        Widgets.EndScrollView();
    }

    private static void Heading(ref float y, float width, string label)
    {
        GUI.color = Style.Dim;
        Text.Anchor = TextAnchor.LowerLeft;
        Widgets.Label(new Rect(0f, y, width, 26f), label);
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        y += 28f;
    }

    private static void Row(Rect r, Texture2D icon, string label, int count, string key, string tip, ThingDef thing = null)
    {
        if (Mouse.IsOver(r))
        {
            Widgets.DrawHighlight(r);
            if (!tip.NullOrEmpty())
            {
                TooltipHandler.TipRegion(r, label + ": " + tip.CapitalizeFirst());
            }
        }
        Rect iconRect = new Rect(r.x + 4f, r.y + 2f, 24f, 24f);
        if (thing != null)
        {
            Widgets.ThingIcon(iconRect, thing);
        }
        else if (icon != null)
        {
            GUI.DrawTexture(iconRect, icon);
        }
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(new Rect(iconRect.xMax + 8f, r.y, r.width - 140f, r.height), label);
        GUI.color = count > 0 ? Style.Text : Style.Dim;
        Text.Anchor = TextAnchor.MiddleRight;
        Widgets.Label(new Rect(r.xMax - 120f, r.y, 70f, r.height), count.ToStringCached());
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
        bool show = !Resources.IsHidden(key);
        bool was = show;
        Widgets.Checkbox(new Vector2(r.xMax - 30f, r.y + 2f), ref show, 24f);
        if (show != was)
        {
            Resources.Set(key, show);
        }
    }
}
