# 🌀 Desktop Idle RPG

A minimalist **idle roguelite** that runs quietly in a small always-on-top window.  
You watch a lone adventurer — represented by a small circle — wander through **procedurally generated mazes**, battling monsters, collecting loot, and diving ever deeper until death claims them.  
When they fall, you tweak their class, gear, or seed, and send them back into the depths to see how far they make it next time.

---

## 🎯 Project Scope

**Goal:**  
Create a lightweight, visually clean **desktop app** (≈300×420 px window) that simulates endless dungeon runs in real time — requiring no player input beyond setup and occasional configuration.

**Core Loop**
1. Generate a new maze floor procedurally.
2. The hero (a simple circle icon) navigates corridors automatically.
3. Encounters trigger quick auto-combat.
4. Loot and XP increase stats and gear quality.
5. When HP hits 0, display the run summary and restart.

**Target Platforms**
- Windows (primary)
- macOS / Linux (secondary via Avalonia cross-build)

**Tech Stack**
- **Language:** C# (.NET 9)
- **UI Framework:** Avalonia UI with Skia canvas
- **Architecture:**  
  - `SimCore` (headless simulation)  
  - `UIFront` (render + control)  
  - Shared `Data` layer (JSON-driven tables)

---

## ⚙️ Parameters

| Category | Detail |
|-----------|---------|
| **Window Size** | 300 × 420 px, resizable |
| **Always-On-Top** | Toggleable (`Topmost = true`) |
| **Tick Rate** | 10–60 ticks / sec (auto-scaled when unfocused) |
| **Run Seed** | 32-bit integer, deterministic PRNG (PCG/Xoshiro) |
| **Maze Size** | 41 × 31 cells per floor |
| **Classes** | Wanderer • Warrior • Elementalist • Rogue |
| **Progression** | Level → Stats → Loot → Floor → Death → Restart |
| **Persistence** | JSON log of previous runs + seeds |
| **Art Style** | Flat 2D, single-color line art + simple animations |
| **Audio** | Optional tick / chime sounds (muted by default) |

---

## 🧩 Core Systems

- **Maze Generator** — randomized DFS (odd-dimension grid)
- **Agent Pathing** — BFS/A* toward unexplored cells
- **Auto-Combat** — turn-based resolution per tick
- **Loot Table** — weighted affix rolls via JSON
- **Leveling** — HP/ATK scaling, perk unlocks
- **Floor Transition** — triggers after stair encounter
- **Run Summary** — floors cleared, kills, loot score
- **Replay** — re-seed deterministic replay mode

---

## 🚧 Feature Roadmap

| Milestone | Features |
|------------|-----------|
| **v0.1 – Core Sim** | Maze gen, movement, basic combat, restart |
| **v0.2 – UI MVP** | Topmost window, circle render, HUD bars |
| **v0.3 – Meta** | Class & gear selection, run summaries |
| **v0.4 – Visual Polish** | Simple particles, color cues, smooth motion |
| **v0.5 – Replay / Analytics** | Save seeds, batch simulations, graphs |

---

## 🎮 Visual Concept

**Minimalist Top-Down View**

The game window displays a simple, centered viewport of the maze with the hero at its center:

- **Hero:** A solid circle (8–12px diameter) that remains centered on screen
- **Maze:** Thin lines forming corridors and walls in a grid pattern
  - Walls: 1–2px thick borders
  - Explored areas: Light gray lines
  - Unexplored: Darker or hidden
- **Enemies:** Small geometric shapes (triangles, squares) in contrasting colors
- **Items/Loot:** Tiny diamonds or stars that pulse subtly
- **Stairs:** Concentric circles or a spiral glyph

**Camera:** The viewport pans smoothly to keep the hero centered as they move through the maze, revealing new sections as they explore.

**HUD Overlay:**
- Top: HP/XP bars (thin horizontal lines)
- Bottom: Floor number, current stats (ATK/DEF)
- Side panel: Class icon, current gear symbols

**Color Palette:**
- Background: Dark gray (#1a1a1a)
- Walls: Light gray (#808080)
- Hero: Bright accent color (blue/gold)
- Enemies: Red/orange
- Loot: Yellow/white glow

**Animation:**
- Smooth lerp movement (no instant teleports)
- Subtle fade-in for discovered areas
- Quick flash on hit/damage
- Particles on level-up or rare drops (optional)

Everything is rendered with simple geometry—no sprites, just clean vector shapes that scale perfectly and keep the app lightweight.

---

## 🧠 Design Philosophy

**"Idle doesn't mean lifeless."**

- **Observation-based enjoyment:** Satisfying progress even while working
- **Determinism:** Same seed = same outcome — perfect for theorycrafting
- **Expandable:** Sim engine allows quick additions (gear, classes, affixes) without touching UI
- **Small footprint:** < 50 MB memory, < 5 MB disk
- **Visual clarity:** Clean geometric shapes and smooth motion over clutter


---

## 📁 Folder Structure

```
/TheMazeRPG
│
├── /Core/                  # Game logic (headless)
│   ├── /Models/            # Hero, Maze, Enemy, Item
│   ├── /Systems/           # MazeGenerator, CombatSystem, LootSystem
│   └── /Services/          # GameState, RngService, TickManager
│
├── /ViewModels/            # Avalonia MVVM ViewModels
│   └── MainViewModel.cs
│
├── /Views/                 # Avalonia UI Views
│   └── MainWindow.axaml
│
├── /UI/
│   └── /Rendering/         # MazeRenderer, custom drawing logic
│
├── /Data/
│   ├── /Classes/           # wanderer.json, warrior.json, etc.
│   ├── /Loot/              # affixes.json, items.json
│   └── /Config/            # settings.json
│
├── /Assets/                # Icons, fonts, sounds
│
├── App.axaml               # Application entry
├── Program.cs              # Main entry point
└── TheMazeRPG.csproj       # Single project file
```
