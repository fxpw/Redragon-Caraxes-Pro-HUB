# Redragon Caraxes Pro HUB

Independent Windows companion for the Redragon Caraxes Pro K644 SE
(K644CGO-RGB-PRO). The C# host uses WebView2 to display a Vue UI in English or
Russian. [Redragon product page](https://redragonshop.com/products/caraxes-k644-keyboard-with-larger-size-keycaps) · [Manual and software](https://redragonshop.com/pages/software)

## Build and run

Use 64-bit Windows with .NET Framework 4.8, the WebView2 Evergreen Runtime,
and Node.js 22.12+ for frontend builds. Run these commands in PowerShell from
the project directory.

```powershell
.\tools\Build-Hub.ps1
.\bin\Caraxes-Pro-HUB.exe
```

To run the built app on another Windows PC, keep `bin` together. It contains
the executable, the WebView2 loader, and the compiled UI.

Minimize the window or click its close button to keep the HUB in the Windows
tray. Double-click its icon or run the executable again to open the existing
window. **Exit HUB** in the tray menu stops Windows layers and live effects.
Enable **Settings → Start HUB when I sign in to Windows** to start the HUB
in the notification area for the current Windows user.

## USB and lighting

1. Set the bottom switch to **OFF** and plug in a USB data cable to use wired
   mode. The HUB shows the connection and reads the keyboard automatically.
2. For the verified Blossoming effect (`09`), choose a brightness from `0` to
   `4` and select **Apply**. The HUB reads the settings before and after the
   write and checks the entire configuration block it sends.
3. **Restore** writes the previous level and verifies it. It is available
   after restarting the HUB when the same USB device and configuration still
   match. **Read again** refreshes changes made on the keyboard.

One stored raw level, `5`, was observed and restored successfully on the
physical keyboard. The selector uses the stock utility's `0`–`4` range.
The brightness view illustrates the board and the read brightness.

When the HUB reads the dark mode `0x15` with a usable stored brightness,
**Restore Blossoming** selects the known working effect. The HUB changes only
the mode byte. **Restore previous mode** is available while the verified
configuration still matches the saved recovery record.

## Layers and key test

In **Layers**, create a layer, edit its name, and choose a **Modifier** from
the Caraxes layout. The same key can activate only one layer. Click any
ordinary input key on the layout and choose its action from the full virtual
keyboard with F1–F24, navigation, numpad and media keys.
Physical Fn1/Fn2 use verified USB events. The layout shows their stock shortcuts.
[Redragon Caraxes Pro manual](https://redragon.ru/data/files/20230830/99d33025.pdf)

Select a layer and use **Edit layer** to change its name, modifier and
assignments together. **Save layer** updates the local profile and the test
view. **Delete layer** removes the selected layer after confirmation. Use
**Apply assignments** to update a running Windows-wide profile.

The Caps tab starts with all 26 assignments from `caps.ahk`. Windows layers
start with the HUB by default. Use **Disable layers in Windows** to turn them
off across launches. The runtime applies the base assignments and any held
layer that uses a standard key. The modifier is used for its layer on tap and hold.
Choose a standard key for custom Fn-layer assignments in Windows. Physical
Fn keys retain the keyboard's stock shortcuts. This mode applies to all
keyboards in the Windows session while the HUB runs. Exit the legacy
`caps.exe` before enabling it. **Key test** applies the saved local profile and
applies the test profile while the HUB has focus. For a mapped position,
the diagram displays its current action. The result field shows the event
received by WebView. The two histories show physical events and Windows
results with timestamps, newest first. The local log is
`%LOCALAPPDATA%\Caraxes-Pro-HUB\key-test.log`. It records key codes, mapping
results and received events while the test is in the foreground. System-wide
per-device suppression requires an input path that identifies the device.

On the test PC, Key test can also read the confirmed 2.4 GHz receiver.
The exact Raw Input device path is stored in
`%LOCALAPPDATA%\Caraxes-Pro-HUB\receiver-keyboard.txt` after the receiver is
identified with physical presses. This path can change if the receiver is
moved to a different USB port or PC. The receiver indicator reports USB
presence. A receiver can remain present when the keyboard is asleep.

## Lighting

The **Lighting** tab combines stock brightness, live effects, and the RGB
canvas. Rainbow Wave, Equalizer, Aurora, Comet, and Breathing stream over USB
while the HUB runs. The equalizer reads 12 frequency bands from the default
Windows playback output using WASAPI loopback. Audio is processed in memory.
**Restore stock lighting** and normal application close return to the saved
keyboard effect and verify its stored configuration.

Paint a key or drag across the layout. The canvas supports a custom color,
eraser, undo and redo, and named local drawings. **Show on keyboard** previews
the drawing for five seconds over USB while the stock Blossoming effect is
selected. The HUB reactivates the previous effect and confirms the stored
configuration by reading it again. Fn1 has no drawable LED in the stock
model configuration and is excluded from the canvas.
The tested keyboard retained its saved Blossoming lighting color after
switching from USB to 2.4 GHz. The receiver has no verified channel for
HUB lighting operations.
Use `Fn2 + 0` on the keyboard to toggle its built-in lighting.

## Diagnostics

```powershell
.\tools\Get-KeyboardInventory.ps1
.\tools\Test-RgbSettings.ps1
.\tools\Test-Workspace.ps1
```

The inventory uses Plug and Play. The regression test uses a synthetic
configuration with data in inactive profiles, so it runs without a keyboard.
See [HANDOFF.md](HANDOFF.md) for USB descriptors, protocol evidence, and
references to the FURYCUBE source.

The remaining hardware work is tracked in [PLAN.md](PLAN.md).

## License

Caraxes Pro HUB is licensed under the [MIT License](LICENSE).
