# ForgeQuest

A blacksmith-shop crafting game — mine ore, run the forge, fulfill orders,
fuse and discover new sword recipes.

**Active focus right now: Windows desktop (Steam track).** This is a small
HTML/JS game (`www/index.html`) wrapped as a native-feeling Windows desktop
app, with an eye toward eventual Steam release.

## What's in this branch

- `www/index.html` — the game itself: all HTML/CSS/JS, self-contained, no
  build step. This is the canonical, single source of truth for game logic —
  edit this file, everything else just wraps it.
- `desktop/ForgeQuestDesktop/` — the Windows desktop wrapper. A thin WinForms
  shell hosting a `Microsoft.Web.WebView2` control pointed at a small local
  HTTP server the app spins up itself (needed for stable `localStorage`
  behavior — see that folder's own `README.md` for the full why/how).

Full build, run, and packaging instructions live in
[`desktop/ForgeQuestDesktop/README.md`](desktop/ForgeQuestDesktop/README.md).

## Android

The Android (Capacitor) wrapper isn't part of active development right now.
It's preserved as-is on the [`android` branch](../../tree/android) and can be
picked back up later without losing any of that work.

## Save data

Local save via `localStorage`, versioned (`forgequest_save_v1`). On desktop
this lives in the WebView2 profile under
`%LOCALAPPDATA%\ForgeQuest\WebView2\` — see the desktop README for details.

## Steam

No Steamworks integration yet in this codebase — that's expected to land as
its own layer around the existing save/friends systems once the desktop
build is stable. (There's a separate Unity prototype,
`Unity_Restart/ForgeQuest_Steam`, exploring a native Steam-first rebuild;
this repo and that one are independent efforts for now.)
