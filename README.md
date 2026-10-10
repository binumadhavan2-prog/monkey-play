# Monkey Play

A cartoon monkey in shades and a puffer jacket that lives on your Windows desktop. It hangs by its tail from a branch near the top-right of the screen, swings, and gets up to things on its own.

## What it does

- **Hangs and swings** from a branch, with springy tail physics. Its eyes follow your cursor.
- **Habits:** scratches its head, looks around, swings higher, climbs its tail hand over hand, waves when your cursor comes near, and now and then climbs up to **sit on the branch** for a while.
- **Snacks:** pulls a banana from its jacket pocket, peels it by hand and eats it.
- **Reminders:** has a drink of water (every 30 min) and reminds you to rest your eyes (every 20 min). Both intervals can be changed.
- **Dances** when music is playing (it listens to your speakers' volume level locally, no internet).
- **Sleeps** at night (10 PM – 6 AM).
- **Opens browser tabs:** click a tab in Chrome or Edge and the monkey swings over, pulls the old page down off the screen to reveal your tab, then climbs back to its branch.
- **Blasts closed tabs:** click a tab's close (×) and the monkey flings a banana at it. BOOM: the tab flies apart and closes.
- **Stays out of the way:** stays on top of other windows, hides during fullscreen video and games, and can be put away at any time.

## Controls

| Do this | What happens |
| --- | --- |
| Drag it | Stretch its tail; let go to fling it |
| Tap it | It gets bumped and grins |
| Right-click it | Menu: settings, hide, start with Windows, quit |
| Hold **M** (about ¾ s) anywhere | Hide the monkey, or bring it back |
| Start the app again | Brings the monkey back if it's hidden |
| **Ctrl+M** | Opens a terminal in this folder (uses a launcher at `%USERPROFILE%\.claude\open-monkeyplay.cmd`) |

Holding M types one "m" if you're in a text box, and M doesn't key-repeat while held.

Right-click menu options: **Monkey opens my tabs**, **Monkey blasts closed tabs**, **Water & blink reminders**, **Water every**, **Eye break every**, **Hide during fullscreen**, **Start with Windows**, **Hide monkey**, **Quit monkey**. Choices are saved in `%APPDATA%\MonkeyPlay\settings.txt`.

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
| `-define:DEMO` | Visits a spot on the browser tab bar and pulls the page down (never clicks anything) |
| `-define:HOLDDEMO` | With `DEMO`: keeps holding the tab |
| `-define:BLASTDEMO` | Blasts a pretend tab on the tab bar (never clicks anything) |
| `-define:CLICKTEST` | Simulated clicks never reach the browser; logs what they'd do to `%TEMP%\monkey_clicktest.txt` |
| `-define:KEYTEST` | Accepts simulated key presses, for testing hold-M |
| `-define:PROFILE` | Writes drawing timings to `%TEMP%\monkey_prof.txt` |

For example: `build.cmd -define:SITDEMO`

## Notes

- To hold your tab clicks and to notice hold-M, the monkey watches the mouse and keyboard system-wide (nothing is recorded or sent anywhere). Because the exe is unsigned, some antivirus tools may flag that.
- It uses your main monitor.
