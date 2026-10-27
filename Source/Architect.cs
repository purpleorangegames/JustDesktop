using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace JustDesktop;

// Architect in our style, the game's layout kept: the window on our rounded card, the categories as rounded buttons
// with a line icon each, the info box above the buttons on a card too. Clicks, search and sounds are the game's
[StaticConstructorOnStartup]
public static class Architect
{
    public static bool On => JustDesktopMod.Settings.modernArchitect;

    private static readonly Action<MainTabWindow_Architect, ArchitectCategoryTab> clickedCategory = AccessTools.MethodDelegate<Action<MainTabWindow_Architect, ArchitectCategoryTab>>(AccessTools.Method(typeof(MainTabWindow_Architect), "ClickedCategory"));
    private static Texture2D _generic;
    private static Texture2D Generic => _generic ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Architect/Generic");
    private static readonly Dictionary<DesignationCategoryDef, Texture2D> icons = new();

    // our icon for the game's categories, a generic one for those from mods
    public static Texture2D IconFor(DesignationCategoryDef def)
    {
        if (!icons.TryGetValue(def, out Texture2D tex))
        {
            string name = def == ArchitectLists.Favorites ? "Favorites" : def == ArchitectLists.Recent ? "Recent" : def.defName;
            tex = ContentFinder<Texture2D>.Get("UI/JustDesktop/Architect/" + name, false) ?? Generic;
            icons[def] = tex;
        }
        return tex;
    }

    public static void CategoryButton(MainTabWindow_Architect window, ArchitectCategoryTab panel, float butWidth, float curXInd, float curYInd, ArchitectCategoryTab openTab, bool enabled)
    {
        Rect rect = new Rect(curXInd * butWidth, curYInd * 32f, butWidth, 32f).ContractedBy(2f);
        bool sides = ArchitectSides.On;
        bool open = sides ? panel == ArchitectSides.Left || panel == ArchitectSides.Right || openTab == panel : openTab == panel;
        bool over = Mouse.IsOver(rect);
        MouseoverSounds.DoRegion(rect, SoundDefOf.Mouseover_Category);
        Color tint = sides && panel == ArchitectSides.Right ? ArchitectSides.RightColor : Style.Accent;
        GUI.color = open ? ArchitectSides.Faded(tint, 0.3f) : over ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.04f);
        Widgets.DrawAtlas(rect, ModernWidgets.Fill);
        // dim when nothing in it matches the search, fainter when there's nothing to build in it
        Color text = !enabled ? Style.Disabled : !panel.AnySearchMatches ? Style.Dim : open || over ? Color.white : Style.Text;
        float icon = 18f;
        GUI.color = text;
        GUI.DrawTexture(new Rect(rect.x + 8f, Mathf.Round(rect.center.y - icon / 2f), icon, icon), IconFor(panel.def));
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.MiddleLeft;
        Text.WordWrap = false;
        Rect label = new Rect(rect.x + 8f + icon + 7f, rect.y, rect.width - icon - 19f, rect.height);
        Widgets.Label(label, panel.def.LabelCap.Resolve().Truncate(label.width));
        Text.WordWrap = true;
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        // with the sides layout, a mark on the side it's open on
        if (sides && panel == ArchitectSides.Left)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 6f, 3f, rect.height - 12f), ArchitectSides.LeftColor);
        }
        if (sides && panel == ArchitectSides.Right)
        {
            Widgets.DrawBoxSolid(new Rect(rect.xMax - 3f, rect.y + 6f, 3f, rect.height - 12f), ArchitectSides.RightColor);
        }
        if (Widgets.ButtonInvisible(rect, false))
        {
            if (sides)
            {
                ArchitectSides.Clicked(window, panel, Event.current.button == 1);
            }
            else
            {
                clickedCategory(window, panel);
            }
        }
        if (window.selectedDesPanel != panel)
        {
            UIHighlighter.HighlightOpportunity(rect, panel.def.cachedHighlightClosedTag);
        }
    }

    // the game's info box (name, cost and readout, description) on our card, only while there's something to show;
    // no shadow, the card doesn't need one and an empty box left only the shadow
    public static void InfoBox(Rect infoRect, Designator designator)
    {
        if (designator == null)
        {
            return;
        }
        Find.WindowStack.ImmediateWindow(32520, infoRect, WindowLayer.GameUI, () =>
        {
            Pane.CardBack(infoRect.AtZero());
            Rect rect = infoRect.AtZero().ContractedBy(10f);
            Widgets.BeginGroup(rect);
            Rect title = new Rect(0f, 0f, rect.width - designator.PanelReadoutTitleExtraRightMargin, 999f);
            Text.Font = GameFont.Small;
            Widgets.Label(title, designator.LabelCap);
            float y = Mathf.Max(24f, Text.CalcHeight(designator.LabelCap, title.width));
            designator.DrawPanelReadout(ref y, rect.width);
            Rect descRect = new Rect(0f, y, rect.width, rect.height - y);
            string desc = designator.Desc;
            GenText.SetTextSizeToFit(desc, descRect);
            GUI.color = Style.Text;
            Widgets.Label(descRect, desc.TruncateHeight(descRect.width, descRect.height));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Widgets.EndGroup();
        }, false, false, 0f);
    }
}

[HarmonyPatch(typeof(MainTabWindow_Architect), "DoCategoryButton")]
public static class Architect_CategoryButton
{
    public static bool Prefix(MainTabWindow_Architect __instance, ArchitectCategoryTab panel, float butWidth, float curXInd, float curYInd, ArchitectCategoryTab openTab, bool enabled)
    {
        if (!Architect.On)
        {
            return true;
        }
        Architect.CategoryButton(__instance, panel, butWidth, curXInd, curYInd, openTab, enabled);
        return false;
    }
}

// our rounded card behind the categories; the game's square background goes off in Window_WindowOnGUI
[HarmonyPatch(typeof(MainTabWindow_Architect), nameof(MainTabWindow_Architect.DoWindowContents))]
public static class Architect_Background
{
    public static void Prefix(Rect inRect)
    {
        if (Architect.On)
        {
            Pane.CardBack(inRect);
        }
    }
}

[HarmonyPatch(typeof(ArchitectCategoryTab), "DoInfoBox")]
public static class Architect_InfoBox
{
    public static bool Prefix(Rect infoRect, Designator designator)
    {
        if (!Architect.On)
        {
            return true;
        }
        Architect.InfoBox(infoRect, designator);
        return false;
    }
}

// Favorites and Recent as two more categories at the top of the Architect: right click anything there to add it to
// or remove it from the favorites; what was last picked to build or designate goes to the front of Recent. Both are
// real categories, so the game's button row, search and info box work with them; kept in the mod's settings
public static class ArchitectLists
{
    private const int MaxRecent = 10;

    public static readonly DesignationCategoryDef Favorites = new() { defName = "JustDesktop_Favorites", label = "favorites", order = 10000, cachedHighlightClosedTag = "" };
    public static readonly DesignationCategoryDef Recent = new() { defName = "JustDesktop_Recent", label = "recent", order = 9999, cachedHighlightClosedTag = "" };

    private static List<string> FavoriteKeys => JustDesktopMod.Settings.architectFavorites;
    private static List<string> RecentKeys => JustDesktopMod.Settings.architectRecent;

    private static readonly AccessTools.FieldRef<Designator_Dropdown, Designator> activeDesignator = AccessTools.FieldRefAccess<Designator_Dropdown, Designator>("activeDesignator");

    public static bool Ours(DesignationCategoryDef def) => def == Favorites || def == Recent;

    // what to remember a designator by: the thing it builds, or what kind of order it is
    public static string Key(Designator d)
    {
        if (d is Designator_Dropdown dropdown)
        {
            d = activeDesignator(dropdown) ?? dropdown.Elements.FirstOrDefault();
        }
        return d switch
        {
            null => null,
            Designator_Build build when build.PlacingDef != null => "build:" + build.PlacingDef.defName,
            _ => "type:" + d.GetType().FullName
        };
    }

    // the game's designators by key, from every category and inside their dropdowns
    private static readonly Dictionary<string, Designator> byKey = new();

    private static void Index()
    {
        byKey.Clear();
        foreach (DesignationCategoryDef cat in DefDatabase<DesignationCategoryDef>.AllDefs)
        {
            foreach (Designator d in cat.AllResolvedDesignators)
            {
                Add(d);
                if (d is Designator_Dropdown dropdown)
                {
                    foreach (Designator element in dropdown.Elements)
                    {
                        Add(element);
                    }
                }
            }
        }

        void Add(Designator d)
        {
            if (d is not Designator_Dropdown && Key(d) is { } key && !byKey.ContainsKey(key))
            {
                byKey[key] = d;
            }
        }
    }

    // fills the two categories from the saved keys
    public static void Refresh()
    {
        if (byKey.Count == 0)
        {
            Index();
        }
        Fill(Favorites, FavoriteKeys);
        Fill(Recent, RecentKeys);

        static void Fill(DesignationCategoryDef cat, List<string> keys)
        {
            cat.AllResolvedDesignators.Clear();
            foreach (string key in keys)
            {
                if (byKey.TryGetValue(key, out Designator d))
                {
                    cat.AllResolvedDesignators.Add(d);
                }
            }
        }
    }

    // Recent changed since the settings were last written
    public static bool Dirty;

    public static bool IsFavorite(Designator d) => Key(d) is { } key && FavoriteKeys.Contains(key);

    public static void ToggleFavorite(Designator d)
    {
        if (Key(d) is not { } key)
        {
            return;
        }
        if (!FavoriteKeys.Remove(key))
        {
            FavoriteKeys.Add(key);
        }
        Refresh();
        JustDesktopMod.Settings.Write();
    }

    public static void Picked(Designator d)
    {
        if (Key(d) is not { } key)
        {
            return;
        }
        RecentKeys.Remove(key);
        RecentKeys.Insert(0, key);
        Dirty = true;
        if (RecentKeys.Count > MaxRecent)
        {
            RecentKeys.RemoveRange(MaxRecent, RecentKeys.Count - MaxRecent);
        }
        Refresh();
    }

    // the right click option, once, while the Architect is open
    public static IEnumerable<FloatMenuOption> WithFavorite(IEnumerable<FloatMenuOption> options, Designator d)
    {
        string add = "Add to favorites", remove = "Remove from favorites";
        bool had = false;
        foreach (FloatMenuOption option in options)
        {
            had |= option.Label == add || option.Label == remove;
            yield return option;
        }
        if (!had && Architect.On && MainButtonDefOf.Architect.TabWindow.IsOpen && Key(d) != null)
        {
            yield return new FloatMenuOption(IsFavorite(d) ? remove : add, () => ToggleFavorite(d));
        }
    }
}

// the two categories go first in the list
[HarmonyPatch(typeof(MainTabWindow_Architect), "CacheDesPanels")]
public static class ArchitectLists_Panels
{
    private static readonly AccessTools.FieldRef<MainTabWindow_Architect, List<ArchitectCategoryTab>> panels = AccessTools.FieldRefAccess<MainTabWindow_Architect, List<ArchitectCategoryTab>>("desPanelsCached");
    private static readonly AccessTools.FieldRef<MainTabWindow_Architect, QuickSearchWidget> search = AccessTools.FieldRefAccess<MainTabWindow_Architect, QuickSearchWidget>("quickSearchWidget");

    public static void Postfix(MainTabWindow_Architect __instance)
    {
        if (!Architect.On)
        {
            return;
        }
        panels(__instance).Insert(0, new ArchitectCategoryTab(ArchitectLists.Recent, search(__instance).filter));
        panels(__instance).Insert(0, new ArchitectCategoryTab(ArchitectLists.Favorites, search(__instance).filter));
    }
}

[HarmonyPatch(typeof(MainTabWindow_Architect), nameof(MainTabWindow_Architect.PreOpen))]
public static class ArchitectLists_Open
{
    public static void Postfix()
    {
        if (Architect.On)
        {
            ArchitectLists.Refresh();
        }
    }
}

// whatever is picked to build or designate goes to Recent
[HarmonyPatch(typeof(DesignatorManager), nameof(DesignatorManager.Select))]
public static class ArchitectLists_Picked
{
    public static void Postfix(Designator des)
    {
        if (Architect.On && des != null)
        {
            ArchitectLists.Picked(des);
        }
    }
}

// the favorites option on the designators' right click menus; the ones that make their own menu get it too
[HarmonyPatch]
public static class ArchitectLists_RightClick
{
    public static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        foreach (Type type in GenTypes.AllSubclasses(typeof(Designator)).Prepend(typeof(Designator)))
        {
            if (AccessTools.DeclaredPropertyGetter(type, nameof(Designator.RightClickFloatMenuOptions)) is { } getter && !getter.IsAbstract)
            {
                yield return getter;
            }
        }
    }

    public static void Postfix(Designator __instance, ref IEnumerable<FloatMenuOption> __result)
    {
        __result = ArchitectLists.WithFavorite(__result, __instance);
    }
}

// while searching, the matches are shown in their own categories, not in Favorites or Recent
[HarmonyPatch(typeof(ArchitectCategoryTab), nameof(ArchitectCategoryTab.CacheSearchState))]
public static class ArchitectLists_Search
{
    private static readonly AccessTools.FieldRef<ArchitectCategoryTab, bool> anyMatches = AccessTools.FieldRefAccess<ArchitectCategoryTab, bool>("anySearchMatches");
    private static readonly AccessTools.FieldRef<ArchitectCategoryTab, Designator> uniqueMatch = AccessTools.FieldRefAccess<ArchitectCategoryTab, Designator>("uniqueSearchMatch");
    private static readonly AccessTools.FieldRef<ArchitectCategoryTab, QuickSearchFilter> filter = AccessTools.FieldRefAccess<ArchitectCategoryTab, QuickSearchFilter>("quickSearchFilter");

    public static void Postfix(ArchitectCategoryTab __instance)
    {
        if (ArchitectLists.Ours(__instance.def) && filter(__instance).Active)
        {
            anyMatches(__instance) = false;
            uniqueMatch(__instance) = null;
        }
    }
}

// Recent is saved when the menu closes, not on every pick
[HarmonyPatch(typeof(Window), nameof(Window.PostClose))]
public static class ArchitectLists_Save
{
    public static void Postfix(Window __instance)
    {
        if (__instance is MainTabWindow_Architect && ArchitectLists.Dirty)
        {
            ArchitectLists.Dirty = false;
            JustDesktopMod.Settings.Write();
        }
    }
}

// the other layout: the menu centered above the taskbar; left click a category to open it on the left of the menu,
// right click to open one on the right, click it again to close that side. With one side open its items are shared
// between both sides. Items are the game's buttons in squares of the chosen size, names under them or on hover
public static class ArchitectSides
{
    private static Settings S => JustDesktopMod.Settings;
    public static bool On => Architect.On && S.architectCentered;

    public static ArchitectCategoryTab Left, Right;

    private static readonly Func<MainTabWindow_Architect, ArchitectCategoryTab> openTab = AccessTools.MethodDelegate<Func<MainTabWindow_Architect, ArchitectCategoryTab>>(AccessTools.Method(typeof(MainTabWindow_Architect), "OpenTab"));
    private static readonly AccessTools.FieldRef<MainTabWindow_Architect, Designator> forceActivated = AccessTools.FieldRefAccess<MainTabWindow_Architect, Designator>("forceActivatedCommand");
    private static readonly AccessTools.FieldRef<MainTabWindow_Architect, QuickSearchWidget> search = AccessTools.FieldRefAccess<MainTabWindow_Architect, QuickSearchWidget>("quickSearchWidget");
    private static readonly AccessTools.FieldRef<MainTabWindow_Architect, bool> forcedDuringSearch = AccessTools.FieldRefAccess<MainTabWindow_Architect, bool>("userForcedSelectionDuringSearch");
    private static readonly AccessTools.FieldRef<ArchitectCategoryTab, Func<Gizmo, bool>> highlight = AccessTools.FieldRefAccess<ArchitectCategoryTab, Func<Gizmo, bool>>("shouldHighLightGizmoFunc");
    private static readonly AccessTools.FieldRef<ArchitectCategoryTab, Func<Gizmo, bool>> lowlight = AccessTools.FieldRefAccess<ArchitectCategoryTab, Func<Gizmo, bool>>("shouldLowLightGizmoFunc");

    // the left side in the accent, the right in a lighter tone of it
    public static Color LeftColor => Style.Accent;
    public static Color RightColor => Color.Lerp(Style.Accent, Color.white, 0.45f);
    public static Color Faded(Color c, float a) => new(c.r, c.g, c.b, a);

    private const float Gap = 6f;
    private const float Pad = 10f;
    private const float Title = 24f;

    // the window, centered on the taskbar
    public static void Place(Window window)
    {
        Rect r = window.windowRect;
        r.x = Mathf.Round((UI.screenWidth - r.width) / 2f);
        r.y = UI.screenHeight - Taskbar.Reserved - r.height;
        window.windowRect = r;
    }

    // left click for the left side, right click for the right; the same one again closes that side
    public static void Clicked(MainTabWindow_Architect window, ArchitectCategoryTab panel, bool right)
    {
        if (!panel.Visible)
        {
            Messages.Message("NothingAvailableInCategory".Translate() + ": " + panel.def.LabelCap, MessageTypeDefOf.RejectInput, false);
            return;
        }
        // a category is open on one side only: opening it on the other moves it there
        if (right)
        {
            Right = Right == panel ? null : panel;
            if (Left == panel)
            {
                Left = null;
            }
        }
        else
        {
            Left = Left == panel ? null : panel;
            if (Right == panel)
            {
                Right = null;
            }
        }
        if (search(window).filter.Active)
        {
            forcedDuringSearch(window) = true;
        }
        window.selectedDesPanel = Left ?? Right;
        SoundDefOf.ArchitectCategorySelect.PlayOneShotOnCamera();
    }

    // in place of the game's row of buttons along the bottom
    public static void Draw(MainTabWindow_Architect window)
    {
        Rect menu = window.windowRect;
        Designator force = forceActivated(window);
        forceActivated(window) = null;

        // searching shows where the game would (the first category with a match), shared between both sides
        bool searching = search(window).filter.Active && !forcedDuringSearch(window);
        ArchitectCategoryTab left = searching ? openTab(window) : Left, right = searching ? null : Right;
        Gizmo over = null;
        float leftTop = menu.yMax;
        if (left != null && right != null)
        {
            leftTop = Side(menu, true, left, Items(left), force, ref over, LeftColor);
            Side(menu, false, right, Items(right), force, ref over, RightColor);
        }
        else if ((left ?? right) is { } one && !S.architectShare)
        {
            // only its own side
            if (one == right)
            {
                Side(menu, false, one, Items(one), force, ref over, RightColor);
            }
            else
            {
                leftTop = Side(menu, true, one, Items(one), force, ref over, LeftColor);
            }
        }
        else if ((left ?? right) is { } shared)
        {
            List<Designator> items = Items(shared);
            int half = Mathf.CeilToInt(items.Count / 2f);
            Color color = shared == Right ? RightColor : LeftColor;
            leftTop = Side(menu, true, shared, items.Take(half).ToList(), force, ref over, color);
            Side(menu, false, shared, items.Skip(half).ToList(), force, ref over, color);
        }
        // the info box where the game's Architect would be, at the left above the left side, the placing controls over it
        Rect info = new Rect(8f, Mathf.Max(0f, leftTop - 8f - 270f), 240f, 270f);
        if (Find.DesignatorManager.SelectedDesignator is { } selected)
        {
            selected.DoExtraGuiControls(info.x, info.y);
        }
        Architect.InfoBox(info, (over as Designator) ?? Find.DesignatorManager.SelectedDesignator);
    }

    private static List<Designator> Items(ArchitectCategoryTab tab) => tab.def.ResolvedAllowedDesignators.Where(d => d.Visible).ToList();

    // one side: a card next to the menu, the category's name on top in the side's color, the items in rows; returns
    // where its top is
    private static float Side(Rect menu, bool leftSide, ArchitectCategoryTab tab, List<Designator> items, Designator force, ref Gizmo over, Color color)
    {
        if (items.Count == 0)
        {
            return menu.yMax;
        }
        float size = Mathf.Round(S.architectItemSize);
        float cell = size + (S.architectNames ? GizmoGrid.LabelHeight : 0f);
        float room = leftSide ? menu.x - 16f : UI.screenWidth - menu.xMax - 16f;
        int maxCols = Mathf.Clamp(Mathf.FloorToInt((room - Pad * 2f + Gap) / (size + Gap)), 1, 12);
        int cols = Mathf.Min(items.Count, maxCols), rows = Mathf.CeilToInt(items.Count / (float)cols);
        float w = cols * (size + Gap) - Gap + Pad * 2f, h = Title + rows * (cell + Gap) - Gap + Pad * 2f;
        Rect panel = new Rect(leftSide ? menu.x - 8f - w : menu.xMax + 8f, Mathf.Max(0f, menu.yMax - h), w, h);
        Pane.CardBack(panel);
        GUI.color = Faded(color, 0.35f);
        Widgets.DrawAtlas(panel, Style.RoundLine);
        float icon = 16f;
        GUI.color = color;
        GUI.DrawTexture(new Rect(panel.x + Pad, panel.y + Pad + 2f, icon, icon), Architect.IconFor(tab.def));
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(new Rect(panel.x + Pad + icon + 6f, panel.y + Pad - 2f, w - Pad * 2f - icon - 6f, Title), tab.def.LabelCap);
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;

        int i = 0;
        GizmoGrid.Active = true;
        GizmoGrid.SizeOverride = size;
        GizmoGrid.LabelsUnder = S.architectNames;
        GizmoGrid.CellProvider = () =>
        {
            int col = i % cols, row = i / cols;
            i++;
            return new Rect(panel.x + Pad + col * (size + Gap), panel.y + Pad + Title + row * (cell + Gap), size, size);
        };
        try
        {
            GizmoGridDrawer.DrawGizmoGrid(items.Cast<Gizmo>(), 0f, out Gizmo mouseover, force == null ? null : g => g == force, highlight(tab), lowlight(tab));
            over ??= mouseover;
        }
        finally
        {
            GizmoGrid.CellProvider = null;
            GizmoGrid.SizeOverride = null;
            GizmoGrid.LabelsUnder = false;
            GizmoGrid.Active = false;
        }
        GenUI.AbsorbClicksInRect(panel);
        return panel.y;
    }
}

[HarmonyPatch(typeof(MainTabWindow_Architect), nameof(MainTabWindow_Architect.ExtraOnGUI))]
public static class ArchitectSides_Extra
{
    public static bool Prefix(MainTabWindow_Architect __instance)
    {
        if (!ArchitectSides.On)
        {
            return true;
        }
        ArchitectSides.Draw(__instance);
        return false;
    }
}
