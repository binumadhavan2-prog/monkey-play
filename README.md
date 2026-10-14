# Monkey Play

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
- **Chats with you:** double-click the monkey (or right-click → **Ask Monkey...**) to ask it anything. Answers come from Claude Code installed on your PC (`npm install -g @anthropic-ai/claude-code`, signed in to your Claude account), locked down with no tools: it can't read your files, run commands or browse, and only sees what you type, plus your copied text when you tick **Use copied text**. Ask it to wave, sit or eat a banana and it does. **Monkey's brain** picks Fast (Haiku), Balanced (Sonnet) or Smartest (Opus). **New chat** starts over.
- **Tells the time:** on the hour it waves and says what time it is (not while it's asleep or you're away). Turn it off with **Hourly chime**.
- **Finds your files:** in the chat, type `find` and part of a file's name (`find GST invoice`, `find sales pdf`), and the monkey lists matching folders first, then files, newest first. Add `folder` to look for folders only (`find neema folder`). Click a name to open it, or **folder** to see it in File Explorer, or type `open 2`. `open` and a folder's exact name (`open neema`) opens it straight away. `open downloads` (or documents, desktop, pictures...) opens that folder. It searches the whole PC: your user folders through the Windows Search index, and everything else (like folders right on `C:\` or another drive) through its own list of file and folder names, made at low priority when you open the chat and kept in memory only, never saved. None of it is sent to Claude or anywhere else. Windows, program folders, AppData and key folders like `.ssh` are never searched. Right-click → **Finding files** to turn **Search the whole PC** off, or **Keep a folder private...** so the monkey never searches or shows it (click a private folder there to stop hiding it).
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

Right-click menu options: **Ask Monkey...**, **Monkey's brain**, **Finding files**, **Monkey opens my tabs**, **Water & blink reminders**, **Water every**, **Eye break every**, **What's the weather?**, **Weather forecasts**, **Hourly chime**, **Hide during fullscreen**, **Live on screen**, **Monkey blasts closed tabs**, **Start with Windows**, **Hide monkey**, **Quit monkey**. Choices are saved in `%APPDATA%\MonkeyPlay\settings.txt`.

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
