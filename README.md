# ForgeQuest — Android Wrapper (Capacitor)

This is a Capacitor-wrapped Android build of the ForgeQuest HTML game.

## What's included
- `www/index.html` — the game itself (same file as the standalone web version)
- `android/` — a full, ready-to-build Android Studio / Gradle project
- `capacitor.config.json` — app id `com.forgequest.app`, app name "ForgeQuest"

## Why this needs to be built on your own machine
This project was assembled in a sandboxed environment without access to Google's
Android/Gradle servers, so the Gradle build itself could not be run here. Everything
up to that point (npm install, `cap add android`, asset copying) is done and verified.

## How to finish the build

### Option A — Android Studio (easiest)
1. Install Android Studio: https://developer.android.com/studio
2. Open the `android/` folder as a project (File → Open).
3. Let Gradle sync (this is the step that needs internet — it downloads the
   Android Gradle Plugin, Gradle itself, and SDK platform files automatically).
4. Click Run ▶ with an emulator or a plugged-in device, or
   Build → Generate Signed Bundle/APK for a real APK/AAB to install or upload.

### Option B — Command line
Requires the Android SDK + `ANDROID_HOME` set up already.
```
cd android
./gradlew assembleDebug
```
The APK will land in `android/app/build/outputs/apk/debug/app-debug.apk`.

## Updating the game later
If you edit the HTML file, just replace `www/index.html` with the new version and run:
```
npm install
npx cap sync android
```
then rebuild in Android Studio.

## Icons / splash screen
Capacitor ships default placeholder icons. To set your own, use:
```
npx @capacitor/assets generate --iconBackgroundColor '#1b1712' --splashBackgroundColor '#1b1712'
```
(after dropping a 1024x1024 icon.png and splash.png into a `resources/` folder — see
https://capacitorjs.com/docs/guides/splash-screens-and-icons for details).
