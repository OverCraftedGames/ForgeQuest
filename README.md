# ForgeQuest

**Run a blacksmith shop. Mine your own ore, work the forge, fill orders for
your customers, and fuse swords together to discover new, rarer recipes.**

ForgeQuest is a cozy crafting/idle game from [OverCrafted Games](https://ocgames.xyz).
Send friends out to visit your shop while you're away, chase down every
achievement, and slowly climb from a one-table workshop to a proper forge.

Currently in active development, with Windows desktop as the primary target
on the way to a Steam release.

## Status

🔨 **Early development.** The core loop (mining, crafting, orders, fusion,
achievements) is playable today; Steam integration hasn't landed yet. Expect
frequent changes and the occasional rough edge.

## Playing it

The game itself is a single self-contained web app (`www/index.html`), no
build step, no dependencies. The primary way to play is the Windows desktop
build, a small native wrapper around that same web app:

- Build/run/package instructions: [`desktop/ForgeQuestDesktop/README.md`](desktop/ForgeQuestDesktop/README.md)

An Android build also exists, preserved on the [`android` branch](../../tree/android),
though it isn't under active development right now.

## Save data

Progress saves locally and automatically, no account needed. On desktop,
saves live in the WebView2 profile under `%LOCALAPPDATA%\ForgeQuest\WebView2\`
(see the desktop README for details).

## Roadmap

- [ ] Steamworks integration (achievements, cloud saves, friends)
- [ ] Continued balance and content passes on crafting, fusion, and orders

## Contact

Questions, feedback, or bugs: [contact@ocgames.xyz](mailto:contact@ocgames.xyz)
