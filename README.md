# Tangledeep Access

A screen-reader accessibility mod for **Tangledeep** (Impact Gameworks), aiming
to make the turn-based roguelike fully playable by the blind.

## IMPORTANT: Read this First

This is my side project.  My main projects take priority.  That means that this is not as polished as, e.g., Factorio Access, and is opinionated.  Specifically:

- You will have to build from source for the foreseeable future
- Keybinding, gamepads, and the mouse are not supported
- I may disappear for long periods of time
- You are getting support for things as I play the game
- I do not take PRs unless you know me personally

This game also doesn't make a good first game even for the sighted. While not as complicated spatially as something like Factorio, the game itself assumes you know how roguelikes work and does not hold your hand in any way.

And finally this isn't complete.  The game is playable but not everything works and you probably can't yet win. Also I'm still changing things every 5 minutes.

## Installing

You need the .NET CLI and Tangledeep through Steam.  After that run build.ps1 to install the mod.

If this doesn't work tell me why and I'll look into it. That script should find your game as long as it was installed through Steam, build the mod, and then install it.

## Getting Started

Tangledeep is a turn-based roguelike. This means that nothing happens unless time advances, and you have as much time as you want to explore. The mod remaps a bunch of controls, as explained in this readme, and then offers some features.  The basic flow is like this:

- You move with q, w, e, a, d, z, x, c which form a square.  S in the center reads the position of your character
- You move an exploration cursor with u, i, o, j, l, m, comma, dot.  K in the center announces the position of this cursor.
- You get to game UIs with alt+number keys.
- A factorio-like scanner exists on home, end, page up, page down

There are a number of navigational aids which add audio cues.  You can learn these by playing the game. The one that is on by default is wall tones. The noise you hear every time you move indicates how far walls are in the 4 cardinal directions.  Navigational aids are toggled with ctrl + the f keys, and can be run exactly once with shift + f keys.  In this case, wall tones are controlled with f1.  There is also an object radar on f2, which sweeps everything in view and pings each thing by direction.

Some announcements are read from your combat log, a feature provided by the game.  You can navigate the combat log with ctrl + brackets.  Some story information goes here as well.

Many things have tooltips.  You read these with `k`.

UIs are like Factorio Access for those who are familiar. For those who aren't, actions and the like often show up on rows adjacent to the item in a menu. That is, moving left and right is as possible as moving up and down.

Finally, note that the main menu has some focus issues and text boxes do not work. This does not pose a major problem, as text boxes are used in very few places and we hack around setting your character's name. This will be fixed.


## Controls

### Gameplay and Combat

- Move, target: q w e a d z x c, or arrow keys
- Use flask: semicolon
- Use town escape: p
- Toggle between equipped weapons: brackets, or f5-f8 to swap to a slot directly (the new weapon and its slot number are announced)
- Use stairs, confirm: enter
- Use monster mallet: t
- Fire ranged weapon: f
- Use shovel: v
- Pick up item: g
- Announce your status (hp, effects, etc): y
- Cycle through your pets/summons: ctrl + y (next), ctrl + shift + y (previous) — announces the ally's name, direction, and status
- Repeat the current pet/summon's status: shift + y
- Command the current pet/summon (follow distance, abilities, come here, attack, dismiss): alt + y
- Read your current tile (map, coordinates, terrain, items, exits): s
- Repeat the last thing spoken: apostrophe
- Step through the combat log: ctrl + left bracket / ctrl + right bracket
- Announce hostiles near you: h
- Announce points of interest near you (powerups, items, gold, treasure sparkles, journal pages, containers, fountains, altars, and stairs): ctrl + h
- Announce terrain near you: alt + h
- Move the exploration cursor: u i o j l m comma dot
- Announce what the exploration cursor is on: k
- Examine what the exploration cursor is on: shift + k
- Skip the exploration cursor to a change: shift+cursor keys
- Toggle whether the exploration cursor returns to your character on move: alt+k
- Recenter the exploration cursor on your character: ctrl+k
- Open the game's help system: f1

### Navigation aids

Audio cues you turn on and off. Ctrl + an f-key toggles an aid; shift + the same f-key runs it exactly once.

- Wall echo (on by default): f1 — tones for how far the walls are in the 4 cardinal directions, played as you move
- Object radar: f2 — sweeps everything in view and pings each thing by direction

### The Hotbar

The game has two hotbars ("bars"). The mod drops the game's swap mechanic and makes both
bars directly reachable instead — bar 1 on the bare keys, bar 2 with Ctrl held:

- Use a bar 1 slot: 1-8
- Use a bar 2 slot: Ctrl+1-8
- Read bar 1: backtick
- Read bar 2: Ctrl+backtick
- Assign the selected thing to bar 1: 1-8 in the appropriate menu (skills for abilities,
  consumables for items)
- Assign the selected thing to bar 2: Ctrl+1-8 in that menu

You can't clear hotbars, but you can reassign any slot whenever you want.

### Scanner

The mod offers a factorio-like scanner:

- Move the exploration cursor to the scanner: home
- Toggle whether the exploration cursor jumps to things as you scan them: alt+home
- Examine what the scanner is on: shift + home
- Refresh the scan: end
- Move between items in a category: page up/down
- Move between categories: ctrl + page up/down

## UI

Open with alt + one of the following:

- Inventory: 1
- Equipment: 2
- Skills: 3
- Character screen: 4
- Journal (recipes, rumors, monsterpedia): q
- Open the options menu/save/quit: escape from the main game screen

Navigating and reading:

- Move around: arrow keys or movement keys
- Move as far in a direction as possible: shift +  movement keys
- Read tooltip: k
- Compare item to equipped: ctrl + k
- Favorite or trash an item: f or minus
- Confirm past a warning (e.g. selling a favorited item): ctrl + enter
- Adjust inline sliders: left/right arrow, add shift for bigger increments
- Close a UI: escape. The game does not always play a sound for this, that's not a mod bug.

### Journal (recipes, rumors, monsterpedia)

The journal (alt + q) has a tab bar as its first row — left/right moves across the tabs
(recipes, rumors, monsterpedia) and confirm switches to the focused tab; the selected tab
is read as "selected". From any tab move down into that tab's content. (The game's combat
log tab is not included here — read it in-game with ctrl + the bracket keys instead.)

- Recipes: each known recipe is a row, marked "can make" when you have the ingredients.
  `k` reads its full description (ingredients, healing, effects); confirm cooks it, but
  only next to a cooking station and with the ingredients on hand (otherwise it says why).
- Rumors: your active quests. Each is a row; the objective text is read on focus, `k`
  reads the rewards. Move right to "abandon rumor" and confirm to give a quest up (this
  opens the usual confirmation).
- Monsterpedia: every monster in the compendium, one per row — its name once you've
  defeated one, else "undiscovered". `k` reads the game's progressive entry: more detail
  (stats, weapon, abilities, attributes) unlocks as your kill count for that kind grows.
  This is a long flat list for now; a search is planned.

### Cooking station

The town cooking station (talk to the chef and choose to cook) is a little workbench,
laid out as rows:

- Frying pan: three ingredient slots, a seasoning slot, and the last dish cooked.
  Confirm on a filled slot takes that item back out of the pan.
- Ingredients / seasonings: two rows listing what you can cook with (name and how many
  you have); confirm adds one to the pan, `k` reads its tooltip.
- Actions: cook, clear pan, repeat last meal, exit.

Fill the pan with at least two ingredients (plus an optional seasoning) and cook — the
dish you made is read out. As in the normal game there is no preview; you cook to find
out what a combination makes.

### Monster Corral, Pets, and Breeding

The monster corral (talk to the ranch keeper, then "View Monsters") is navigated like
any other UI. Each monster is a row; left/right moves through its actions:

- Make pet: take the monster out as your active pet (it must be happy enough)
- Feed: opens a food list — pick a food to feed it; its reaction is read out
- Groom: opens the grooming menu (a normal dialog)
- Release: asks for confirmation, then lets the monster go for good
- `k` on a monster reads its full info: powers, weapon power, core stats, beauty
  effect, known food likes/dislikes, and how it feels about every other monster

Breeding (share a romantic meal with the keeper) lists your monsters; confirm on a
monster to select it, pick two, then "breed selected". With two chosen, the header
reads each one's feelings toward the other and whether they're willing.

Naming a newly tamed or bred monster uses a text box, which is not yet supported, so
you can't type a custom name — but the naming prompt's "Random" and "That's the name!"
buttons both work (the latter lets the keeper name it for you), so nothing blocks.
