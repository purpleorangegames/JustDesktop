# JustDesktop

Makes the RimWorld UI work more like a desktop. The bottom bar becomes a dock or a taskbar, the tabs open as windows, and most of the screen gets a cleaner, rounded look.

- A modern taskbar or dock for the bottom bar
- Option to turn on several windows open at once, like an OS
- A smaller selection card with bars and skills, for pawns, buildings, items, plants and zones
- Gizmos in a compact grid next to the card
- A simple minimap, only the ground, water, rock and buildings, no pawns or creatures
- Notifications that stay, so you can open letters again after dismissing them
- A calendar of the quadrum, and an option to not lose old events
- Able to select a builder for a plan
- Able to select to get notified when a town restocks its goods
- Select which resources to show at the top left
- Mood, health and status icons on the colonist bar
- A queue for research
- Favorites and recent things in the Architect
- Gender and age icons when forming caravans and trading, and a search in what a trader buys
- Profiles to save your setup and switch between them

Almost everything can be turned off or changed in the mod options. The Steam page has the full list of options and how to use each feature.

Steam version: https://steamcommunity.com/sharedfiles/filedetails/?id=3828423950

## Building

You need the .NET 10 SDK, then inside this folder run:

    dotnet build Source -c Release

The dll goes to 1.7/Assemblies. To test it, copy or link this folder to RimWorld/Mods.

The icons are SVGs in Art, Art/render.sh turns them into the PNGs in Textures.

## License

CC0, you can do whatever you want with it, no need to give credits.

AI was used to help code/fix issues
