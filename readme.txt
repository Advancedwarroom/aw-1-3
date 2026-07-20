

Project Overview: AW Fan Remake (Turn-Based Grid Tactics)

Overview

This project is a Godot 4 engine (C# scripts) based fan remake of Advance Wars (AW), a turn-based grid tactics game. It supports both PC and Android platforms, with a fully implemented core tactical loop, including 15+ playable units, 5 fixed weapons, a complete fog-of-war system, AI opponents, a map editor, and a custom configuration system.

---

Tech Stack

Item	Description	
Engine	Godot 4	
Language	C# (47 .cs scripts)	
Platforms	PC (Windows/macOS/Linux) + Android	
Architecture	Manager pattern, separated responsibilities	

---

Core System Modules

1. Game Master (GameManager.cs)
- Turn management (Player1 / Player2 / Player0 three-phase cycle)
- Funds system (cities/bases/airports generate income per turn)
- Victory conditions (annihilation / HQ capture / destroy designated weapon)
- Statistics panel (kills/losses/funds/turn count)
- Unit production (purchase new units from bases/airports)
- Action undo (cancel after moving)
- End Turn button

2. Grid System (GridManager.cs)
- Map grid management (32x32 pixels per cell)
- A pathfinding algorithm
- Movement range preview (purple trail highlight)
- Attack range preview (red highlight)
- Mobile touch trail preview
- Two-finger pinch zoom + drag pan (CameraTouchController.cs)

3. Unit Management (UnitManager.cs)
- Unit spawn / destroy / bind to grid cell
- Team management (Player1 / Player2 / Player0 / Player)
- World coordinate ↔ grid coordinate conversion

4. Unit Base Class (Infantry.cs)
Parent class for all combat units, containing:
- HP system (1–10 bar display, supports overflow HP such as Oozium's 200HP)
- Dual-weapon system (primary + secondary, with full attack/defense matrix)
- Fuel system (air/naval daily consumption, self-destruct when depleted)
- Ammo system (limited ammo + resupply)
- Movement types (infantry/tread/tire/hover/pipe/air/naval/lava/slime, etc.)
- Embark system (APC and other transport units can load/unload infantry)
- Capture system (city/base/airport progress-based capture)
- Self-destruct system (universal module, fixed value / percentage / formula damage modes)
- Counter-attack mechanic (default 50% coefficient, supports ranged counter-attack toggle)
- Damage formula: Base Damage × Attacker HP% × (1 − Terrain Defense) − Dynamic Defense

5. Action Menu (ActionMenu.cs)
Dynamically generates buttons: Move, Attack, Wait, Capture, Self-Destruct, Flare, Load, Unload, Produce, Info

6. AI System (AI_Manager.cs)
- Production decisions (auto-purchase units when funds are sufficient)
- Attack decisions (prioritize high-value targets)
- Capture decisions (move toward nearest city)
- Resupply decisions (return to base when low on ammo/fuel)
- Evasion decisions (avoid enemy attack ranges)
- Self-destruct decisions (choose self-destruct when near death)

7. Fog of War (FogOfWarManager.cs)
- Global vision pool (merged vision from own units + buildings + watchtowers)
- Flare system (Flare unit launches, multi-turn temporary vision)
- Watchtower system (fan/ray vision modes)
- Special vision: laser four-direction ray, Black Cannon triangular cone
- Terrain vision bonus (different units have different vision on different terrain)
- Independent vision mode (weapons do not rely on units for vision)

8. Map Editor (TerrainEditor.cs)
- Real-time spawn units/weapons/facilities
- Property editing (dynamically edit any Export property via reflection)
- Batch terrain placement
- Multi-cell weapon preview placement (2×2 / 3×3)
- Custom movement types / terrain / texture upload
- Save/Load system (ExtraSave.cs, 10 unit slots + 5 map slots)

9. Custom Configuration System (CustomConfigUI.cs + ExtraSave.cs)
- Custom movement types (define new movement types and their terrain costs)
- Custom terrain (defense bonus, color, movement cost)
- Texture upload (32×32 PNG/JPG/GIF, applied to units/weapons/facilities/terrain)
- Favorites system

---

Implemented Units (15 Types)

Unit	Cost	Move	Range	Traits	
Infantry	1000	3	1	Can capture, cheap	
Mech	3000	2	1	Good mountain mobility, anti-armor	
Bike	2500	5	1	High mobility, can capture	
Oozium	20000	1	1	200HP, devour replaces attack, high defense	
Light Tank	7000	6	1	Balanced tank	
Medium Tank	16000	5	1	High attack/defense	
Artillery	6000	5	2–3	Indirect fire, cannot counter-attack	
Rocket	15000	4	3–5	Long-range indirect fire	
APC	5000	6	1	Can transport infantry/mech, resupply	
Anti-Air	8000	6	1	Anti-air specialization	
Recon	4000	8	1	High vision, high mobility	
Anti-Tank	11000	4	1	High damage vs armor	
Flare	5000	5	1	Can launch flares to dispel fog	
Pipe Runner	20000	9	2–5	Pipe-only movement, can attack after moving	
Fly Bomb	25000	8	1	Air unit, resupply at airports	

Unit Traits
- All units support Inspector custom mode (when `useDefaultConfig = false`, fully customize properties)
- Dual-faction distinction (Player1 Red / Player2 Blue)
- Animation state machine (idle / move / attack / breath)
- Wait-state visual dimming + breath animation stop
- Low fuel/ammo flashing warning

---

Implemented Weapons (5 Types)

Weapons are fixed facilities, immobile, operated by AI or player.

Weapon	Cost	Attack Mode	Traits	
Black Cannon	12000	Triangular cone range	Four-direction rotation, fixed damage, configurable cooldown	
Large Cannon	19000	3×3 occupy + triangular range	Multi-cell occupy, only weak point is attackable, still impassable after destruction	
Death Ray	25000	3×3 occupy + 3-cell wide laser	Four-direction fire, 7-turn cooldown, impassable after destruction	
Laser	19000	Any-angle ray	Configurable arbitrary firing angle, penetrates multiple targets, contact area decay	
Crystal	15000	No attack	Heal + ammo/fuel resupply, configurable range	

Weapon Traits
- Multi-cell weapon support (2×2 / 3×3)
- Muzzle and weak point separated design
- Cooldown system (configurable cooldown turns and attacks per cycle)
- Ammo system (optional)
- AI auto-operation (P0 weapons auto-attack)
- Rotation system (four-direction / arbitrary angle)
- Destroyed Broken form (still occupies cells, impassable)

---

Implemented Facilities (3 Types)

Facility	Function	
City	+1000 funds per turn, resupply all ground units	
Base	City functions + can produce all 15 ground unit types	
Airport	City functions + can produce Fly Bomb, resupply air units only	

Facility Traits
- Four-faction support (P-1 light purple / P0 gray-white / P1 red / P2 blue)
- P-1 facilities resupply all factions
- P0 facilities have no bonus, no vision, non-operable
- Capture progress system (can be contested, interrupted)
- Capture/contest/interrupt effects

---

Implemented Effects

Effect	Description	
HitEffect	Hit red circle expansion	
CannonFlash	Black Cannon muzzle flash	
SupplyEffect	Resupply cross glow + floating text	
GridSupplyEffect	Facility resupply hex pulse + energy pillar	
CaptureEffect	Capture progress ring + waving flag	
CaptureContestEffect	Contest lightning clash + shockwave	
CaptureInterruptedEffect	Interrupt shatter ring + debris scatter	

---

Faction System (TeamHelper.cs)

Faction	ID	Traits	
Player (P-1)	"Player"	Both sides can operate, facilities resupply all factions	
Player0 (P0)	"Player0"	Neutral, no reaction, attacks everyone, facilities have no bonus	
Player1 (P1)	"Player1"	Normal faction, red	
Player2 (P2)	"Player2"	Normal faction, blue	

---

Terrain System (Grids.cs)

Supports 30+ terrain types:
- GROUND, ROAD, FOREST, HILL, SEA, RIVER, BEACH, REEF, WHIRLPOOL
- LAVA, LAVASIDE, LAVABRIDGE, LAVAFOG
- PIPE, PIPESEAM, PASSABLEPIPE, BROKENPIPE
- METEORITE, TRACK, BROKENTRACK, STATION, BRIDGE
- OVERPASS, CLIFF, SLOPE, CAVE, HOLE, RUINS
- SEAFOG, LANDFOG, WATERFALL, SHIPGATE

Each terrain has independent: defense bonus, movement cost (by movement type), vision bonus, damage/ammo/fuel change effects.

---

Vision System (VisionConfig.cs)

- Unit type base vision config
- Unit type × terrain vision bonus matrix
- Weapon type base vision + independent vision mode
- Global default terrain vision bonus
- Watchtower fan/ray vision
- Flare temporary vision

---

Save System (ExtraSave.cs)

- 10 unit custom slots (save any Export property)
- 10 weapon custom slots
- 10 facility custom slots
- 10 terrain custom slots
- 5 map save slots (full terrain + units + weapons + facilities + funds)
- Custom movement types persistence
- Custom terrain persistence
- Custom texture persistence
- Favorites system
- JSON format storage

---

Input System

Input	Function	
Left-click unit	Select / execute action	
Left-click cell	Move / attack target	
Right-click / click selected unit	Cancel selection	
Ctrl + Left-click	View unit/enemy info	
Middle-click drag	Pan map	
Scroll wheel	Zoom map	
Two-finger touch	Zoom map	
Single-finger touch	Drag map / select unit	

---

File List (47 C# Scripts)

Core Managers
- GameManager.cs - Game master
- GridManager.cs - Grid and pathfinding
- UnitManager.cs - Unit management
- WeaponManager.cs - Weapon management
- AI_Manager.cs - AI decisions
- FogOfWarManager.cs - Fog of war
- ActionMenu.cs - Action menu
- TerrainEditor.cs - Map editor
- CameraTouchController.cs - Camera control

Data & Config
- TeamHelper.cs - Faction system
- VisionConfig.cs - Vision config
- UnitProductionDatabase.cs - Unit production database
- ExtraSave.cs - Save system
- CustomConfigUI.cs - Custom config UI

Units (15 Types)
- Infantry.cs - Unit base class
- Mech.cs, Bike.cs, Oozium.cs - Infantry types
- LightTank.cs, MdTank.cs - Tank types
- Artillery.cs, Rocket.cs, PipeRunner.cs - Artillery types
- APC.cs - Transport type
- AntiAir.cs, Recon.cs, AntiTank.cs - Special vehicles
- Flare.cs - Flare type
- FlyBomb.cs - Air type

Weapons (5 Types)
- Weapon.cs - Weapon base class
- BlackCannon.cs - Black Cannon base
- LargeCannon.cs - Large Black Cannon (3×3)
- DeathRay.cs - Death Ray (3×3 + laser)
- Laser.cs - Laser (arbitrary angle)
- Crystal.cs - Black Crystal (heal/resupply)

Facilities (3 Types)
- City.cs - City base
- Base.cs - Base (production)
- AirPort.cs - Airport (air production)

Effects (7 Types)
- HitEffect.cs - Hit effect
- CannonFlash.cs - Muzzle flash
- SupplyEffect.cs - Resupply floating text
- GridSupplyEffect.cs - Facility resupply effect
- CaptureEffect.cs - Capture effect
- CaptureContestEffect.cs - Contest effect
- CaptureInterruptedEffect.cs - Interrupt effect

Other
- Grids.cs - Grid class
- UnloadUnitButton.cs - Unload unit button

---

Project Status

- Core tactical loop: Fully playable
- AI opponent: Complete (production/attack/capture/resupply/evasion/self-destruct)
- Fog of war: Complete (vision pool + flares + watchtowers)
- Map editor: Complete (spawn/edit/save/load)
- Custom system: Complete (movement types / terrain / textures)
- Mobile adaptation: Complete (touch + two-finger pinch zoom)
- Naval units: Movement types defined, units not yet implemented
- CO system: Not implemented
- Weather system: Not implemented
- Campaign mode: Not implemented
- Undo/Redo: Not implemented

---

This introduction is generated based on actual project code, containing only implemented features, not including unimplemented content from design documents.