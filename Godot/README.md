# Godot Migration

This directory is the parallel Godot 4.7.1 .NET frontend for The Maze RPG. The existing Avalonia
application remains runnable while behavior is moved incrementally.

## Open the project

1. Start the Godot 4.7.1 .NET editor.
2. Choose **Import** in the Project Manager.
3. Select `C:\TheMazeRPG\Godot\project.godot`.
4. Open the project, allow the initial C# build to finish, then press **F6** or **F5**.

The project now starts at a Godot title screen with character creation and save selection. The
dungeon draws semantic placeholder terrain rather than the unverified atlas mappings. Movement is
real-time through WASD/arrow actions, Space triggers the existing dash, and the primary mouse
button fires the selected attack toward the cursor. Number keys or the mouse wheel select attacks.

Dungeon interface controls:

- `I` opens the Character modal on its Inventory tab.
- `Tab` opens the same Character modal on its Stats tab.
- `E` interacts with a nearby chest. Locked chests support trap inspection, lockpicking, and matching keys.
- Right-click a corpse or perceived trap for its interaction menu.
- Hovering an available chest, perceived trap, or corpse gives it a pulsing cyan outline.
- `Escape` opens the pause menu or closes the active overlay.

Weapons and armor use humanoid equipment slots; one- and two-handed weapons obey hand occupancy,
while learned class actions remain separate from physical gear. The Codex is available from both
the title and dungeon HUD. Inventory provides portable synthesis
for combinations allowed by `CombineLocation.Anywhere`; forge- and shrine-gated pairs remain
unavailable until their locations are ported. Godot display, camera, screen-shake, V-sync, and
master-volume settings are stored in `user://client-settings.json`.

Regular dungeon floors use persistent line-of-sight exploration. Currently visible terrain is
fully lit, previously seen terrain remains dimmed, and unexplored space stays concealed. Enemies,
projectiles, features, decorations, and tactical markers follow the same visibility rules; mouse
movement and attack commands cannot route through undiscovered cells or target unseen actors.

Press `=` to enter or leave tactical turn mode. In tactical mode, WASD/arrows spend the displayed
agility-based movement allowance, the primary mouse button spends the primary action, `Q` uses the
first available health potion as a bonus action, Space spends the primary action on a short dash,
and Enter ends the player turn. The highlighted cells show the currently reachable movement area.
After player effects resolve, each visible or pursuing enemy takes one agility-ordered attack,
cardinal step, or hold action before control returns to the player. The HUD identifies the acting
enemy and tracks progress through the enemy phase. During the player phase, the enemy-order band
and matching numbered map markers preview attacks, advances, retreats, and holds; movement updates
the plan immediately.

Hover a reachable empty tile to preview its shortest path and movement cost, then left-click to
move there. Hovering a living enemy, an unreachable tile, or any tile while holding Shift switches
to the selected attack preview. Its overlay shows authored range, line-of-sight validity, impact
cell, and affected cells for area attacks. Left-click commits only a `READY` target, so blocked,
out-of-range, and unaffordable attacks preserve the action. WASD and arrow-key movement remain
available.

The town (overworld) loop is playable: leaving the dungeon via a safe-room shrine lands in the
town, which renders with its own palette and points of interest (dungeon entrance, mine, smithy,
market stall, street lamps). `E` opens each structure's menu — mining, crafting recipes, selling,
and starting a fresh dive. The HUD mode line shows the world clock in town, and night falls for
real: a day/night `CanvasModulate` with warm flickering lamp/forge light pools, the hero's own
light sized by racial darkvision or a carried torch, and the Night Sight skill thinning the dark
globally. Overworld-resume saves load directly into the town.

## Architecture

- `../Core/TheMazeRPG.Core.csproj` is shared game simulation and content logic.
- `Scripts/GameHost.cs` owns navigation, the fixed-rate simulation bridge, input, and camera.
- `Scripts/GameUi.cs` owns Godot menus, dynamic lists, the HUD, and dungeon overlays.
- `Scripts/DungeonView.cs` is the temporary semantic renderer used while TileSets are visually verified.
- `Scenes/Game.tscn` contains the shared world and responsive interface shell.

The Godot host reads data from the repository-level `Data` directory during migration and writes
saves under Godot's per-user `user://` directory. Assets will move behind verified TileSet resources;
the current guessed terrain catalog is intentionally not treated as authoritative.
