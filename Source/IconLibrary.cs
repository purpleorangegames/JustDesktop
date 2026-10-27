using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace JustDesktop;

// every icon that can go on a button: our line icons (white, tinted when drawn), the colored dock icons,
// the game's own tab icons, and pictures the player puts in the custom icons folder
public static class IconLibrary
{
    public sealed class Icon
    {
        public string Key;
        public string Label;
        public Texture2D Texture;
        public bool Line;
    }

    private static readonly string[] lineNames = { "Work", "Schedule", "Assign", "Animals", "Mechs", "Wildlife", "Research", "Quests", "World", "History", "Factions", "Ideos", "Menu", "Generic" };
    private static readonly string[] dockNames = { "Architect", "Work", "Schedule", "Assign", "Animals", "Mechs", "Wildlife", "Research", "Quests", "World", "History", "Factions", "Ideos", "Menu" };

    private static List<Icon> all;
    private static readonly Dictionary<string, Icon> byKey = new();

    // the sets that can be put on every button at once: label, key prefix
    public static readonly (string label, string prefix)[] Sets = { ("Line icons", "line"), ("Colorful", "dock"), ("Colorful 2", "dock2"), ("Soft", "dock3") };

    // the key of a set's icon for a button, null when the set has none for it
    public static string SetIcon(string prefix, MainButtonDef def)
    {
        string key = prefix + ":" + (prefix == "line" && def == MainButtonDefOf.Architect ? "Start" : def.defName);
        return Get(key) != null ? key : null;
    }

    public static string CustomFolder => Path.Combine(GenFilePaths.SaveDataFolderPath, "JustDesktop", "Icons");

    public static List<Icon> All
    {
        get
        {
            if (all == null)
            {
                Load();
            }
            return all;
        }
    }

    public static Icon Get(string key)
    {
        if (all == null)
        {
            Load();
        }
        return key != null && byKey.TryGetValue(key, out Icon icon) ? icon : null;
    }

    public static void Reload()
    {
        if (all != null)
        {
            foreach (Icon icon in all.Where(i => i.Key.StartsWith("custom:")))
            {
                UnityEngine.Object.Destroy(icon.Texture);
            }
        }
        all = null;
        byKey.Clear();
        Style.ClearCache();
    }

    private static void Load()
    {
        all = new List<Icon>();
        Add("line:Start", "Start", Style.Start, true);
        foreach (string name in lineNames)
        {
            Add("line:" + name, name, ContentFinder<Texture2D>.Get("UI/JustDesktop/Tabs/" + name, false), true);
        }
        foreach (string name in dockNames)
        {
            Add("dock:" + name, name, ContentFinder<Texture2D>.Get("UI/JustDesktop/Dock/" + name, false), false);
        }
        foreach (string name in dockNames)
        {
            Add("dock2:" + name, name, ContentFinder<Texture2D>.Get("UI/JustDesktop/Dock2/" + name, false), false);
        }
        foreach (string name in dockNames)
        {
            Add("dock3:" + name, name, ContentFinder<Texture2D>.Get("UI/JustDesktop/Dock3/" + name, false), false);
        }
        foreach (MainButtonDef def in DefDatabase<MainButtonDef>.AllDefs)
        {
            if (def.Icon != null)
            {
                Add("game:" + def.defName, def.LabelCap, def.Icon, false);
            }
        }
        try
        {
            Directory.CreateDirectory(CustomFolder);
            foreach (string file in Directory.EnumerateFiles(CustomFolder).Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f))
            {
                Texture2D tex = LoadPicture(file);
                if (tex != null)
                {
                    Add("custom:" + Path.GetFileName(file), Path.GetFileNameWithoutExtension(file), tex, false);
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning("[JustDesktop] could not read the custom icons folder: " + e.Message);
        }
    }

    private static Texture2D LoadPicture(string file)
    {
        try
        {
            Texture2D tex = Texture2D.LoadFromFile(file);
            if (tex == null)
            {
                tex = new Texture2D(2, 2);
                tex.LoadImage(File.ReadAllBytes(file));
            }
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }
        catch (Exception e)
        {
            Log.Warning("[JustDesktop] could not load " + file + ": " + e.Message);
            return null;
        }
    }

    private static void Add(string key, string label, Texture2D tex, bool line)
    {
        if (tex == null || byKey.ContainsKey(key))
        {
            return;
        }
        var icon = new Icon { Key = key, Label = label, Texture = tex, Line = line };
        all.Add(icon);
        byKey[key] = icon;
    }
}

// picks the icon of each button for one style, from everything in the library
public class Dialog_Icons : Window
{
    private readonly BarStyle style;
    private MainButtonDef selected;
    private Vector2 listScroll, iconScroll;

    public override Vector2 InitialSize => new(860f, 680f);

    public Dialog_Icons(BarStyle style)
    {
        this.style = style;
        doCloseButton = true;
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = false;
        selected = MainButtonDefOf.Architect;
    }

    private Dictionary<string, string> Choices => style == BarStyle.Dock ? JustDesktopMod.Settings.dockIcons : JustDesktopMod.Settings.taskbarIcons;

    private static IEnumerable<MainButtonDef> Buttons => DefDatabase<MainButtonDef>.AllDefs.Where(d => d != MainButtonDefOf.Inspect).OrderBy(d => d == MainButtonDefOf.Architect ? -1 : d == MainButtonDefOf.Menu ? 1 : 0).ThenBy(Taskbar.GroupOf).ThenBy(d => d.order);

    public override void DoWindowContents(Rect inRect)
    {
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 35f), (style == BarStyle.Dock ? "Dock" : "Taskbar") + " icons");
        Text.Font = GameFont.Small;
        Rect top = new Rect(inRect.x, inRect.y + 40f, inRect.width, 28f);
        if (Widgets.ButtonText(new Rect(top.x, top.y, 220f, top.height), "Open the custom icons folder"))
        {
            OpenFolder(IconLibrary.CustomFolder);
        }
        if (Widgets.ButtonText(new Rect(top.x + 230f, top.y, 160f, top.height), "Load new icons"))
        {
            IconLibrary.Reload();
        }
        GUI.color = Style.Dim;
        Widgets.Label(new Rect(top.x + 400f, top.y + 4f, inRect.width - 400f, top.height), "PNG or JPG files, square works best");
        GUI.color = Color.white;

        // a whole set at once; buttons the set has no icon for keep theirs
        Rect sets = new Rect(inRect.x, top.yMax + 8f, inRect.width, 28f);
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(new Rect(sets.x, sets.y, 110f, sets.height), "Use a whole set");
        Text.Anchor = TextAnchor.UpperLeft;
        float x = sets.x + 115f;
        foreach (var (label, prefix) in IconLibrary.Sets)
        {
            if (Widgets.ButtonText(new Rect(x, sets.y, 120f, sets.height), label))
            {
                foreach (MainButtonDef def in Buttons)
                {
                    string key = IconLibrary.SetIcon(prefix, def);
                    if (key != null)
                    {
                        Choices[def.defName] = key;
                    }
                }
                Style.ClearCache();
            }
            x += 126f;
        }
        if (Widgets.ButtonText(new Rect(x, sets.y, 120f, sets.height), "All default"))
        {
            Choices.Clear();
            Style.ClearCache();
        }

        Rect body = new Rect(inRect.x, sets.yMax + 10f, inRect.width, inRect.height - sets.yMax - 10f - CloseButSize.y - 10f);
        Rect left = new Rect(body.x, body.y, 250f, body.height);
        Rect right = new Rect(left.xMax + 12f, body.y, body.width - left.width - 12f, body.height);
        DrawButtons(left);
        DrawLibrary(right);
    }

    private void DrawButtons(Rect rect)
    {
        var buttons = Buttons.ToList();
        Rect view = new Rect(0f, 0f, rect.width - 16f, buttons.Count * 34f);
        Widgets.BeginScrollView(rect, ref listScroll, view);
        float y = 0f;
        foreach (MainButtonDef def in buttons)
        {
            Rect row = new Rect(0f, y, view.width, 32f);
            if (def == selected)
            {
                Widgets.DrawBoxSolid(row, Style.Faded(0.18f));
            }
            else if (Mouse.IsOver(row))
            {
                Widgets.DrawBoxSolid(row, Style.Hover);
            }
            var (tex, line) = style == BarStyle.Dock ? Style.DockIconFor(def) : Style.TaskbarIconFor(def);
            GUI.color = line ? Style.Text : Color.white;
            GUI.DrawTexture(new Rect(row.x + 6f, row.y + 4f, 24f, 24f), tex);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(row.x + 38f, row.y, row.width - 38f, row.height), def == MainButtonDefOf.Architect && style == BarStyle.Taskbar ? "Architect (start button)" : (string)def.LabelCap);
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonInvisible(row))
            {
                selected = def;
            }
            y += 34f;
        }
        Widgets.EndScrollView();
    }

    private void DrawLibrary(Rect rect)
    {
        Widgets.DrawMenuSection(rect);
        rect = rect.ContractedBy(8f);
        Rect header = new Rect(rect.x, rect.y, rect.width, 28f);
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(new Rect(header.x, header.y, header.width - 130f, header.height), "Icon for " + selected.LabelCap);
        Text.Anchor = TextAnchor.UpperLeft;
        if (Widgets.ButtonText(new Rect(header.xMax - 120f, header.y, 120f, header.height), "Default"))
        {
            Choices.Remove(selected.defName);
            Style.ClearCache();
        }
        Rect area = new Rect(rect.x, header.yMax + 8f, rect.width, rect.height - header.height - 8f);
        const float cell = 62f;
        List<IconLibrary.Icon> icons = IconLibrary.All;
        int perRow = Mathf.Max(1, (int)((area.width - 16f) / cell));
        Rect view = new Rect(0f, 0f, area.width - 16f, Mathf.Ceil(icons.Count / (float)perRow) * cell);
        Choices.TryGetValue(selected.defName, out string current);
        Widgets.BeginScrollView(area, ref iconScroll, view);
        for (int i = 0; i < icons.Count; i++)
        {
            IconLibrary.Icon icon = icons[i];
            Rect box = new Rect(i % perRow * cell, i / perRow * cell, cell - 6f, cell - 6f);
            if (icon.Key == current)
            {
                Widgets.DrawBoxSolid(box, Style.Faded(0.3f));
            }
            else if (Mouse.IsOver(box))
            {
                Widgets.DrawBoxSolid(box, Style.Hover);
            }
            GUI.color = icon.Line ? Style.Text : Color.white;
            GUI.DrawTexture(box.ContractedBy(icon.Line ? 13f : 8f), icon.Texture);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(box, icon.Label);
            if (Widgets.ButtonInvisible(box))
            {
                Choices[selected.defName] = icon.Key;
                Style.ClearCache();
            }
        }
        Widgets.EndScrollView();
    }

    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            string opener = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            var info = new System.Diagnostics.ProcessStartInfo(opener) { UseShellExecute = false };
            info.ArgumentList.Add(path);
            System.Diagnostics.Process.Start(info);
        }
        catch (Exception e)
        {
            Messages.Message("Could not open " + path + ": " + e.Message, MessageTypeDefOf.RejectInput, false);
        }
    }

    public override void PostClose()
    {
        base.PostClose();
        JustDesktopMod.Settings.Write();
    }
}
