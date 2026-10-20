# Monkey Play

**Created by Binu Madhavan.**

A cartoon monkey in shades and a puffer jacket that lives on your Windows desktop. It hangs by its tail from a branch near the top-right of the screen, swings, and gets up to things on its own.

## What it does

- **Hangs and swings** from a branch, with springy tail physics. Its eyes follow your cursor.
- **Habits:** scratches its head, looks around, swings higher, climbs its tail hand over hand, waves when your cursor comes near, and now and then climbs up to **sit on the branch** for a while.
- **Snacks:** pulls a banana from its jacket pocket, peels it by hand and eats it.
- **Reminders:** has a drink of water (every 30 min) and reminds you to rest your eyes (every 20 min). Both intervals can be changed.
- **Dances and sings** when music is playing (it listens to your speakers' volume level locally, no internet): it bounces to the beat, sings along in short phrases with its mouth moving two syllables a beat (wider when the music's louder), music notes float out of its mouth, and now and then it sings a line in a bubble ("Shoo-be-doo-bop ♪"). It doesn't make any sound, so your music stays as it is.
- **Sleeps** at night (10 PM – 6 AM).
- **Predicts the weather:** warns you an hour or two before rain ("Rain around 4 PM. Take an umbrella!"), and now and then tells you what today looks like, or tomorrow in the evening, with a little weather picture in its speech bubble. Right-click → **What's the weather?** to ask any time. It finds your rough location from your internet address (geojs.io, or ipapi.co as a backup) and gets the forecast from [Open-Meteo](https://open-meteo.com), free with no account. If the location is wrong (a VPN, say), add `weatherAt=11.01,76.97` (your latitude,longitude) to `settings.txt`. Its face follows the weather too: a big smile in the sun, sweating and panting in the heat, a frown in the rain, a wobbly mouth and a sweat drop in a thunderstorm, chattering teeth and a shiver in the cold, and an unimpressed flat mouth under grey skies. Turn it all off with **Weather forecasts**.
- **Opens browser tabs:** click a tab in Chrome or Edge and the monkey swings over, pulls the old page down off the screen to reveal your tab, then climbs back to its branch.
- **Blasts closed tabs:** click a tab's close (×) and the monkey flings a banana at it. BOOM: the tab flies apart and closes. It hops off its seat or wakes up to throw, and if it's off at another tab it throws as soon as it's back. Clicking the same close again while the banana is flying does nothing, so only that one tab closes.
- **Chats with you:** double-click the monkey (or right-click → **Ask Monkey...**) to ask it anything. Answers come from Claude Code installed on your PC (`npm install -g @anthropic-ai/claude-code`, signed in to your Claude account), locked down with no tools: it can't read your files, run commands or browse, and only sees what you type, plus your copied text when you tick **Use copied text**. Ask it to wave, sit or eat a banana and it does. **Monkey's brain** picks Fast (Haiku), Balanced (Sonnet) or Smartest (Opus). **New chat** starts over. Claude Code saves each conversation as a file on your PC (in `%USERPROFILE%\.claude\projects\`, in Monkey's own folder); the monkey deletes them automatically after 7 days. Change that under **Delete saved chats after** (when you press New chat, 1 day, 7 days, 30 days, or never), or delete them all from there. The conversation you're in is never deleted.
- **Checks your battery:** right-click → **How's my battery?** (or type `battery` in the chat) for a check-up: battery health (how much it holds compared to when it was new), how long a charge lasts now versus then, how often it's plugged in, and tips for your laptop, like where to turn on its 80% charge limit (HP, Lenovo, Dell, ASUS, Acer, Surface, Samsung). It reminds you to plug in at 20% and 10%, and once a day if it's been full and plugged in for a long time. It reads Windows' own battery report (`powercfg /batteryreport`) on your PC; nothing is sent anywhere, except a one-line summary if you ask Monkey about your battery in the chat. Turn the reminders off with **Battery tips & reminders**.
- **Gives your laptop a check-up:** right-click → **Laptop check-up** (or type `check my laptop` in the chat). It looks at free disk space, the SSD's own health report, memory use (and which apps use the most), how busy the processor is, days since the last restart, updates waiting for a restart, the last Windows update, whether your antivirus is on and up to date, how many apps start with Windows, temporary files and the Recycle Bin, and battery health. Then it suggests what to do, with links to the right Windows Settings page. It only looks: it never changes, deletes, installs or turns off anything, needs no admin rights, and nothing it finds leaves your PC (if you ask Monkey why your laptop is slow, a numbers-only summary goes with your question). It also warns you once a day if your main drive is almost full.
- **Tells the time:** on the hour it waves and says what time it is (not while it's asleep or you're away). Turn it off with **Hourly chime**.
- **Finds your files:** in the chat, type `find` and part of a file's name (`find GST invoice`, `find sales pdf`), and the monkey lists matching folders first, then files, newest first. Add `folder` to look for folders only (`find neema folder`). For a common name, add a folder it's inside: `find vegefoods js` finds the `js` folder inside `vegefoods-master`. Ask however you like: "i said find birthday folder", "can you find thrissur", "birthday folder", "find birthday in C", and a follow-up like "it's in C drive" repeats the last search on that drive. If nothing matches exactly, it shows folders spelled nearly the same ("marriage" finds "layana marriege", "thrisur" finds "Thrissur"). Click a name to open it, or **folder** to see it in File Explorer, or type `open 2`. `open` and a folder's exact name (`open neema`) opens it straight away. `open downloads` (or documents, desktop, pictures...) opens that folder. It searches the whole PC: your user folders through the Windows Search index, and everything else (like folders right on `C:\` or another drive) through its own list of file and folder names, made at low priority when you open the chat and kept in memory only, never saved. None of it is sent to Claude or anywhere else. Windows, program folders, AppData and key folders like `.ssh` are never searched. Right-click → **Finding files** to turn **Search the whole PC** off, or **Keep a folder private...** so the monkey never searches or shows it (click a private folder there to stop hiding it).
- **Opens your apps:** in the chat, type `open excel`, `open calculator` or `launch chrome`. It picks from the apps in your Start menu (if several match, it lists them to click), never a typed path or command. When you ask Monkey for help and an app fits, it shows an **▶ Open Excel** link in its reply; nothing opens unless you click it. It never offers apps that run commands or change Windows settings (PowerShell, Command Prompt, Registry Editor...), though you can still type `open` for those yourself. Your app list stays on your PC.
- **Reads a file you pick:** the **File...** button in the chat attaches one text, CSV, Word, Excel or PowerPoint file to your next question ("what's the total here?"). Only that file is sent, only that once.
- **Stays out of the way:** stays on top of other windows, hides during fullscreen video and games, and can be put away at any time.
- **Works with several screens:** choose which screen it lives on; it's drawn crisp at that screen's scaling (100%, 125%, 150%...).

## Controls

| Do this | What happens |
| --- | --- |
| Drag it | Stretch its tail; let go to fling it |
| Tap it | It gets bumped and grins |
| Double-click it | Opens the chat |
| Right-click it | Menu: settings, hide, start with Windows, quit |
| Hold **M** (about ¾ s) anywhere | Hide the monkey, or bring it back |
| Start the app again | Brings the monkey back if it's hidden |
| **Ctrl+M** | Opens a terminal in this folder (uses a launcher at `%USERPROFILE%\.claude\open-monkeyplay.cmd`) |

Holding M types one "m" if you're in a text box, and M doesn't key-repeat while held.

Right-click menu options: **Ask Monkey...**, **Monkey's brain**, **Finding files**, **Delete saved chats after**, **Monkey opens my tabs**, **Water & blink reminders**, **Water every**, **Eye break every**, **What's the weather?**, **Laptop check-up**, **How's my battery?**, **Battery tips & reminders**, **Weather forecasts**, **Hourly chime**, **Hide during fullscreen**, **Live on screen**, **Monkey blasts closed tabs**, **Start with Windows**, **Hide monkey**, **About Monkey Play**, **Quit monkey**. Choices are saved in `%APPDATA%\MonkeyPlay\settings.txt`.

## Android

The same monkey, on your phone (Android 8 or newer). It hangs from a branch at the top-right of the screen, over whatever app you're using, and your touches still reach the app underneath, except on the monkey itself.

- **Drag it** and let go to fling it. **Tap it** to make it grin. **Double-tap** for the time and battery. **Hold it** to open its settings.
- **Tilt your phone** and it swings that way. **Shake it** and it gets thrown about.
- Its eyes follow your finger, and it scratches its head, looks around, swings higher, climbs its tail and waves.
- It dances and sings when music is playing, sleeps at night (10 PM – 6 AM), waves when you unlock the phone, reminds you about water and eye breaks, warns you at 20% and 10% battery, and says the time on the hour.
- The notification lets you hide it or quit. Settings: size, tilt, each reminder, and starting with the phone.

Android only lets touches pass through an app's overlay when it's at most 80% opaque, so the monkey is a little see-through on the phone.

It doesn't have the Windows features that need a PC: the browser tab tricks, the chat with Claude, finding files, opening apps, the laptop check-up and the weather.

### Download for Android

On your phone, open **[Monkey-Play.apk](https://github.com/binumadhavan2-prog/monkey-play/releases/latest/download/Monkey-Play.apk)** and tap it to install. Android asks once to allow installing apps from your browser; allow it. It isn't from the Play Store, so Play Protect may call it an unknown app: tap **Install anyway**. Then open **Monkey Play**, tap **Allow Monkey over other apps**, switch it on and come back.

### Build the Android app

You need Java (JDK 17 or newer) and the Android SDK's command-line tools with `platforms;android-34` and `build-tools;34.0.0` (in `%LOCALAPPDATA%\Android\Sdk`, or set `ANDROID_HOME`). No Android Studio or Gradle.

```
android\build.cmd
```

This makes `android\Monkey Play.apk`. Copy it to your phone and open it to install (allow installing from that app when asked), or with USB debugging on: `adb install -r "android\Monkey Play.apk"`. Open **Monkey Play**, tap **Allow Monkey over other apps**, switch it on, and come back.

## Download

Get `Monkey-Play.exe` from the [latest release](https://github.com/binumadhavan2-prog/monkey-play/releases/latest) and run it. There's nothing to install.

The exe isn't digitally signed, so the first time Windows may show "Windows protected your PC". Click **More info**, then **Run anyway**.

## Build

You need Windows with .NET Framework 4 (built in to Windows 10 and 11). No other tools are needed.

```
build.cmd
```

This compiles `MonkeyPlay.cs` into `Monkey Play.exe`. Quit the monkey first if it's running.

### Test builds

Extra arguments are passed to the compiler. These switches make test builds that act right away:

| Switch | Test build |
| --- | --- |
| `-define:HABITDEMO` | Runs through its habits one after another, starting with the banana |
| `-define:DRINKDEMO` | Water break right after starting |
| `-define:SITDEMO` | Goes and sits on the branch right after starting |
| `-define:DANCEDEMO` | Pretends a song is playing, so it dances and sings right away |
| `-define:WEATHERDEMO` | Checks the weather right after starting, gives a forecast every few seconds, and shows each weather face in turn |
| `-define:CHIMEDEMO` | Pretends it just turned 4 o'clock a few seconds after starting, to show the hourly chime |
| `-define:DEMO` | Visits a spot on the browser tab bar and pulls the page down (never clicks anything) |
| `-define:HOLDDEMO` | With `DEMO`: keeps holding the tab |
| `-define:BLASTDEMO` | Blasts a pretend tab on the tab bar (never clicks anything) |
| `-define:CLICKTEST` | Simulated clicks never reach the browser; logs what they'd do to `%TEMP%\monkey_clicktest.txt` |
| `-define:KEYTEST` | Accepts simulated key presses, for testing hold-M |
| `-define:PROFILE` | Writes drawing timings to `%TEMP%\monkey_prof.txt` |

For example: `build.cmd -define:SITDEMO`

## Notes

- To hold your tab clicks and to notice hold-M, the monkey watches the mouse and keyboard system-wide (nothing is recorded or sent anywhere). Because the exe is unsigned, some antivirus tools may flag that.
- Battery and CPU: while visible it uses roughly 15% of one CPU core (it runs at 12-20 frames a second when calm, 60 when moving fast, and fewer on battery), at low priority so your own apps always come first. Hidden (hold M), it uses almost nothing.
- It lives on your main screen unless you pick another under **Live on screen**.

## Author and license

Monkey Play was created by **Binu Madhavan**. Right-click the monkey → **About Monkey Play** to see it, ask Monkey "who made you?" in the chat, or look at the exe's Properties → Details.

Copyright © 2026 Binu Madhavan. All rights reserved. You may use the app on your own computer; copying, changing or sharing the code or artwork needs permission. See [LICENSE](LICENSE).
