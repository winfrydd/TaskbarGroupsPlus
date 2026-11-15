# Taskbar Groups

Put several programs behind **one taskbar button**. Click it and a small panel pops up with
your programs; click one to open it.

This is a rewrite of [tjackenpacken/taskbar-groups](https://github.com/tjackenpacken/taskbar-groups)
(MIT), which was left unfinished in 2021. It reads and writes the same config format, so
existing groups and taskbar pins keep working, and the old version can still open groups
saved by this one.

## Using it

1. Run `TaskbarGroups.exe` and click **New group**.
2. Name the group, add programs (button, or drag them in from Explorer, the desktop or the
   Start menu, including Store apps), and click **Save**.
3. Click **Show shortcut**, right-click the highlighted file and choose **Pin to taskbar**.

### In the popup

| Action | Result |
|---|---|
| Click | Open the program and close the panel |
| Ctrl+click or middle-click | Open it and keep the panel open |
| Right-click | Open / Run as administrator / Open file location / Edit group |
| `1`–`9`, `0` | Open the program in that position |
| Arrow keys / Tab, then Enter | Pick and open |
| Ctrl+Enter | Open all (if enabled for the group) |
| Esc or click elsewhere | Close |

## What changed from the original

**Fixed**
- The icon cache was never built at save time (the loop's condition was wrong), and folder
  icons were saved as `.png` but read as `.jpg`, so they were re-extracted on every click.
- Two programs with the same file name (two `launcher.exe`s) overwrote each other's icons.
- With more than one monitor, the popup could use the wrong monitor's taskbar.
- Some custom background colours crashed the popup (hover colour went past 255).
- The "must choose an icon" check never fired.
- Every popup click wrote and deleted a test file in the program folder. If that failed, it
  started a second, elevated copy and kept running.
- The editor blocked on a GitHub version check every time it opened.

**New**
- Sharp icons at any DPI, with no shortcut arrows. Store apps, folders and documents work too.
- Rounded Windows 11 popup, keyboard navigation, right-click menu, Ctrl/middle-click to open several.
- Optional names under icons, three icon sizes, light / dark / custom background.
- Per-program **Run as administrator**.
- Drag to reorder, drag in from the Start menu, live **Preview** while editing.
- Renaming moves the group properly. Deleting sends it to the Recycle Bin.
- Missing programs are flagged instead of crashing. Errors go to `error.log`.
- Down to two files plus config: no third-party DLLs, no 3 MB `Windows.winmd`.

**Speed.** A warm popup opens in about 135 ms (process start to visible window), about the
same as the original (~155 ms) and close to the floor for any .NET window. The first click
after a pause is about twice as fast, and the first click on a newly saved group no longer
stalls on icon extraction.

## Building

Requires the .NET 10 SDK.

```
dotnet publish src/TaskbarGroups -c Release -o publish
```

Or build and install over an existing folder (it backs up first and keeps `config\` and `Shortcuts\`):

```
powershell -ExecutionPolicy Bypass -File install.ps1 -Target D:\Programs\TaskbarGroups
```

Runs on any installed .NET 10 (or newer) Desktop Runtime.

## License

MIT. Original work © 2020 Jack Schierbeck; this rewrite © 2026 winfryd.
