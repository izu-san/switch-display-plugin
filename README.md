# Switch Display Plugin

Flow Launcher plugin for switching the Windows main display.

## Usage

1. Build the project.
2. Copy the build output folder to Flow Launcher's plugin directory.
3. Restart Flow Launcher.
4. Type `md` and choose the display that should become the Windows main display.

The plugin keeps the relative monitor layout by moving the selected display to `(0, 0)` and offsetting the other displays by the same amount.

## Stream setup

Type `md stream`, `md infinitas`, or `md iidx` to run the INFINITAS stream setup:

1. Launch OBS Studio.
2. Launch OneComme.
3. Set the first active `2560x1440` display as the Windows main display.
4. Open `https://p.eagate.573.jp/game/infinitas/2/api/login/login.html` in the default browser.
