# Designing vibration patterns for PoE Controller

This folder decides how your controller vibrates in Pillars of Eternity. Every spell, weapon hit and big moment can have
its own pattern, and you can change any of them, or make new ones, with a text editor. No programming needed.

Patterns only play with **Vibration** set to **Aphatasia's extended** in the mod's controller settings (press View and
Menu together in the game). "Essential only" is the console version's own rumble and ignores these files.

## What's in this folder

| Folder / file | What it is | Edit it? |
|---|---|---|
| `custom/` | **Your patterns.** A file here always wins over the shipped ones. | Yes, put everything you make here. |
| `spells/` | Patterns for single spells, shipped with the mod. | Better not: updates overwrite it. Copy a file to `custom/` and edit the copy. |
| `buckets/` | The shared patterns for groups of similar spells (fire blasts, frost, lightning...) and for weapons, critical hits, knock-outs, deaths, the start of a fight (`combat_start`) and the generic preparation pulse (`preparing`). | Same as `spells/`: copy to `custom/` first. |
| `spell_ids.txt` | Every spell and ability the mod knows, with its file name and its bucket. Written by the mod at start-up. | No, just look things up. |
| `spell_index.json` | The data behind `spell_ids.txt`. | No. |

## How the mod picks a pattern

When a hit lands, the mod looks for, in this order:

1. `custom/<spell>.json`
2. `spells/<spell>.json`
3. the spell's bucket: `custom/<bucket>.json`, then `buckets/<bucket>.json`
4. for weapons and anything else: a bucket picked by the kind of attack and its damage type (for example
   `weapon_slash`, `weapon_bow`, `fire_blast`)

So you can change one spell (a file named after the spell), or a whole family of spells at once (a file named after the
bucket).

## Finding the right file name

Open `spell_ids.txt`. Each line reads:

```
Fireball | fireball.json | fire_blast | #ff5c00 #ff942e #ffc474
```

That's the in-game name, the file name to use, the bucket it belongs to, and its light colours (see "Light colours"
below). Scrolls, necklaces, traps and enemy versions
of a spell share its name, so one file covers all of them. File names are lower case, with `_` instead of spaces and
without apostrophes: "Minoletta's Minor Missiles" is `minolettas_minor_missiles.json`.

## Your first pattern in five minutes

1. Look up the spell in `spell_ids.txt`, say `fireball.json` (bucket `fire_blast`).
2. Copy `buckets/fire_blast.json` into `custom/` and rename the copy to `fireball.json`.
3. Change a value, for example make the impact twice as long (see the field list below). Save.
4. In the game, open the controller settings (View and Menu), go to **Test a vibration pattern (A)**, pick
   `custom/fireball` with left / right, and press **A**. You don't need to restart: the mod notices saved files within
   about a second.
5. When it feels right, cast a real Fireball.

## The file format

A pattern file is JSON. Here is a complete example:

```json
{
  "name": "Fireball",
  "note": "A heavy blast that rolls out over most of a second, with a crackle on top.",
  "bucket": "fire_blast",
  "motors": {
    "cast":   [ { "motor": "both", "amp": 0.12, "attack": 1.5, "fade": 1000, "length": 0, "flicker": 0.5 } ],
    "launch": [ { "motor": "low",  "amp": 0.3,  "fade": 0.05, "length": 0.1 } ],
    "impact": [
      { "motor": "low",  "amp": 1.0, "attack": 0.02, "fade": 0.25, "length": 0.8 },
      { "motor": "high", "amp": 0.6, "fade": 0.2, "length": 0.6, "delay": 0.03, "flicker": 0.6 }
    ]
  },
  "haptic": {
    "impact": [ { "motor": "low", "amp": 1.0, "attack": 0.01, "fade": 0.18, "length": 0.6 } ]
  }
}
```

### Top level

| Field | Meaning |
|---|---|
| `name` | Shown nowhere, just for you. |
| `note` | What the pattern should feel like, in words. Helps others who edit it after you. |
| `bucket` | Optional. Anything this file leaves out is taken from that bucket. A file with only an `impact` and `"bucket": "fire_blast"` still gets the bucket's cast build-up. |
| `motors` | The version for classic rumble motors: **Xbox 360, Xbox One / Series, PlayStation 4 (DualShock 4)** and most other pads. |
| `haptic` | The version for precise vibration: **PlayStation 5 (DualSense), Switch Pro / Joy-Con, Steam Deck**. |
| `dualsense` | A DualSense-only version (for the DualSense extras, coming in a later version of the mod). |
| `lights` | Optional. The spell's light colours, see "Light colours" below. |

You don't need all three. If a version is missing, the mod uses another one: `dualsense` falls back to `haptic`, then
`motors`; `haptic` falls back to `motors`; `motors` falls back to `haptic`.

### Phases

Each version has up to five phases. Each phase is a list of layers that play at the same time.

| Phase | When it plays |
|---|---|
| `cast` | Only for **your main character's own** attacks and spells: from the moment the attack starts until it lands. Good for a faint build-up. |
| `launch` | Only for your main character's own attacks and spells: the moment the attack is released (a bow string, a gun kick, a spell leaving the hand). |
| `impact` | A hit lands (on your party, or by your party on an enemy), or a spell takes effect: a heal, a buff, a curse, someone knocked prone or put to sleep. |
| `prepare` | Turn-based mode only: a spell with a long cast is being prepared (the caster's turn is still open and the spell lands later in the turn order). Loops until that character's turn ends. Use `"length": 0` (= until the turn ends) and a slow `pulse`. A spell without one uses its bucket's, then `buckets/preparing.json`. |
| `linger` | Every later tick of a pulsing spell (Chill Fog, Returning Storm, Moonwell, Consecrated Ground...). If it's missing, `impact` is used. |

### Layers

A layer is one shape on one motor. Every field except `motor` has a default, so you only write what you need.

| Field | Default | Meaning |
|---|---|---|
| `motor` | `"both"` | `"low"` = the big, heavy motor (a deep thud), `"high"` = the small, fast one (a sharp buzz), `"both"` = both. On a DualSense, Switch or Deck the two are less different, but `low` still feels deeper. |
| `amp` | `1.0` | Peak strength, 0 to 1. See "How strong it really is" below. |
| `attack` | `0` | Seconds to rise from nothing to the peak. 0 = it hits at once. |
| `fade` | `0.1` | How quickly it dies down after the peak. Small = a short knock (0.03 to 0.1), large = it rings on (0.3 to 0.6). 1000 or more = it stays at full strength. |
| `length` | `0.2` | Total length in seconds; the layer stops after this. **0 in the `cast` phase means "until the attack lands".** |
| `delay` | `0` | Seconds after the phase starts before this layer begins. Use it to put a crackle just after a thud. |
| `pulse` | `0` | Pulses per second. 0 = smooth. 4 to 8 = a slow throb, 20 to 40 = a buzz or stutter. |
| `duty` | `0.5` | With `pulse`: the share of each pulse that is "on". 0.3 = short ticks, 0.7 = mostly on with short gaps. |
| `flicker` | `0` | 0 to 1: uneven noise on top, like crackling fire or a fizz. 0.5 is clearly uneven, 0.9 is very ragged. |
| `repeat` | `0` | Play the layer this many extra times. Good for missiles, chain lightning, a heartbeat. |
| `every` | `0.1` | With `repeat`: seconds between the starts of the repeats. |
| `falloff` | `1.0` | With `repeat`: each repeat is this much weaker. 0.8 = each one 80 % of the one before. |

## Light colours

Controllers with lights (the DualSense lightbar, in a later version of the mod) take their colours from the spell's
own in-game art. The mod ships colours read from every spell's icon, so Fireball glows in its oranges and Blessing in
its gold. To change them, add `lights` to the spell's file in `custom/`:

```json
"lights": { "colors": ["#ff5c00", "#ff942e", "#ffc474"] }
```

Two or three colours work best: the light moves between them. Colours are `#rrggbb` (any colour picker shows them).
Bright, saturated colours show best; dark and grey ones barely light up. The mod looks for colours in this order:
your `custom/` file, the shipped `spells/` file, the colours read from the spell's icon (in `spell_ids.txt`), then the
spell's bucket. A file can set only `lights` and nothing else; the vibration then still comes from the shipped pattern.

## How strong it really is

The `amp` values are the pattern at full strength. In a fight the mod scales every hit:

- **Size of the hit:** 0.1 + the damage as a share of the target's maximum endurance. A scratch plays at about a tenth of
  your `amp`, a hit that takes the whole bar plays at full `amp`.
- **Who is involved:** full strength when your main character deals or takes the hit, 40 % when only a companion or
  summon is involved. Fights between enemies don't vibrate.
- **Critical hits:** 30 % stronger, with the `critical` bucket played on top.
- **Spells without damage** (heals, buffs, curses, sleep, prone) play at 60 % of your `amp`, since there is no hit size to
  go by.
- The player's **Vibration strength** setting multiplies everything.

So design at full strength (as the test bench plays it) and let the game turn it down for small hits. The `cast` and
`launch` phases always play at your `amp` values.

## Real-time and turn-based

Fights in real time (with pause) move fast, so the mod keeps it lean there:

| | Turn-based | Real-time |
|---|---|---|
| Hits (scaled by damage and who is involved) | yes | yes |
| Spells with their own file (`spells/`, `custom/`) | yes | yes |
| Other spells: their bucket | yes | no, they play like a plain hit of their damage type |
| Heals / buffs / curses without their own file | yes | no |
| `cast` and `launch` | yes | no |
| `prepare` | yes | does not exist in real time |
| Critical overlay (`critical`) | yes | no (crits are still 30 % stronger) |
| Combat start, knock-out, death | yes | no |
| Speed | as written | every pattern runs 40 % faster and shorter |

So a pattern that feels right in turn-based plays at 60 % of its length in real time. Test with the test bench (it
plays as written) and then in a real-time fight.

## Tips

- **Beats and silence beat smooth fades.** On rumble motors every smooth swell and fade-out feels about the same.
  What makes a pattern recognisable is rhythm: hard blocks (`"attack": 0, "fade": 1000` and a short `length`) with
  real gaps between them (`delay`). A spark, 0.07 s of nothing, then the boom feels completely different from one
  long rumble. Keep blocks and gaps at 0.04 s or more, motors can't go faster than that.
- **One missile, one hit.** Missiles, bouncing spells (Firebug, Chain Lightning) and pulsing spells land every hit
  on its own, and each one plays your `impact` (or `linger`). So design a single hit, not the whole volley. Hits of one
  spell on one target that land almost together are spaced 0.13 s apart, so three missiles are felt as three taps.

- **Motors need time.** Classic rumble motors take a few hundredths of a second to spin up and to stop. Very short layers
  (under 0.05 s) or fast pulses (above 30 per second) blur on them. Make the `motors` version rounder and slower than the
  `haptic` one.
- **Thud versus buzz.** Use `low` for weight and impact, `high` for sharpness and texture. Most good patterns use both:
  a `low` hit and a `high` detail on top.
- **Less is more.** A pattern that runs at full strength for a whole second gets tiring in a long fight. Save the big
  ones for big spells.
- **Something stronger is always on top.** Everything playing at once is mixed by taking the stronger value per motor,
  so a weak pattern never cuts off a strong one.
- **Keep the feel of the family.** If your Fireball feels completely unlike every other fire spell, players can't read
  what's happening. Start from the spell's bucket and change it from there.

## Testing and troubleshooting

- **Test bench:** controller settings, **Test a vibration pattern (A)**. Left / right go through every file in `buckets/`,
  `spells/` and `custom/`; A plays the preparation pulse (if there is one, for 2.5 seconds), the cast build-up (for one
  second), then launch and impact, on the controller you're
  holding.
- **Live reload:** save the file, wait a second, test again. No restart.
- **Nothing happens?** Check that Vibration is set to Aphatasia's extended, that the file name matches `spell_ids.txt`
  exactly, and that the file is valid JSON. Common mistakes: a missing comma between two layers, a comma after the last
  layer, single quotes instead of double quotes. Any online JSON checker finds these.
- **A file that can't be read is skipped.** The mod then uses the next pattern in the lookup order, so a typo never breaks
  your game. With a development build of the mod, the log (`BepInEx/LogOutput.log`) names the file it couldn't read, and
  names the pattern used for every vibration.
- **Steam Input:** on PC, turn Steam Input off for Pillars of Eternity (Steam, Properties, Controller). With it on, the
  game only sees a virtual Xbox pad, so a DualSense gets the `motors` version and none of its extras. On the Steam Deck,
  leave it on.

## Sharing your patterns

A pattern is a single small file, so sharing is easy: post your `custom/*.json` files, and others drop them into their
own `custom/` folder. Please keep the `note` field filled in, so the next person knows what you were going for.
