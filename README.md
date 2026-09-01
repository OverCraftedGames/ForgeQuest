# ForgeQuest

**Run a blacksmith shop. Mine your own ore, work the forge, fuse swords
together to discover rarer recipes, hammer them up in quality, and fill
orders to grow from a one-table workshop into something bigger.**

ForgeQuest is a cozy crafting/idle game from [OverCrafted Games](https://discord.gg/ahVpJH4WA6).

Currently in active development, with Windows desktop as the primary target
on the way to a Steam release.

## Status

🔨 **Early development, but the core loop is full-featured.** Expect
frequent changes and the occasional rough edge — Steam integration hasn't
landed yet. Latest release: [v0.1.2](../../releases/tag/v0.1.2).

## What's in the game right now

- **Crafting** — buy ore, place it on a grid-based crafting table, and forge
  swords. Discover **fusion** recipes by combining materials in the right
  arrangement for rarer, higher-value results.
- **Hammers** — hire hammers with distinct special effects (cutting craft
  time, boosting crit chance, and more) to work a table alongside you.
- **Mines** — unlock passive mines that produce ore on their own over time.
- **Orders** — fill orders for adventurer customers to turn swords into gold.
- **Expeditions** (business level 8+) — send swords out with a party of
  adventurers matched against specific slot requirements; a good outcome
  pays well above what those swords would earn through Orders, with a
  chance one comes back.
- **Achievements** and a **7-day daily login** cycle of ore and gems.
- **Friends** — add friends by code and visit their workshop in a real
  popout: watch their live crafting grid and hammer their ready crafts to
  help them out. Backed by a small dedicated server, not local-only.
- **Cosmetic avatar & title** — your achievements double as unlockable
  profile icons and titles, shown to friends on your friend card. Pick
  yours from the "Your Profile" button in the Friends tab.

## Playing it

The game itself is a single self-contained web app (`www/index.html`), no
build step, no dependencies. The primary way to play is the Windows desktop
build, a small native wrapper around that same web app:

- Build/run/package instructions: [`desktop/ForgeQuestDesktop/README.md`](desktop/ForgeQuestDesktop/README.md)
- Latest built release (no build step needed): [Releases](../../releases/latest)

An Android build also exists, preserved on the [`android` branch](../../tree/android),
though it isn't under active development right now.

## Save data

Progress saves locally and automatically, no account needed. On desktop,
saves are written directly to a plain JSON file at
`%LOCALAPPDATA%\ForgeQuest\save.json` by the app itself (not stored inside
the browser engine's own storage) — this survives the app being closed
unexpectedly and can't be silently rolled back by the browser engine's own
storage layer. Your friend code and username persist the same way, in
`%LOCALAPPDATA%\ForgeQuest\friend_identity.json`.

## Roadmap

- [ ] Steamworks integration (achievements, cloud saves)
- [ ] Continued balance and content passes on crafting, fusion, and orders

## Contact

Questions, feedback, or bugs: [contact@ocgames.xyz](mailto:contact@ocgames.xyz)
