# Caraxes Pro HUB handoff

The user wants a new companion app for their Redragon Caraxes Pro, using the
useful work from FURYCUBE G9 PRO HUB as a starting point. Use a separate
project and Codex conversation. Start with a working Windows device scan and
readback, then add verified features to a C# backend and Vue UI.

## Hardware and vendor package

- Official model listing is CARAXES PRO K644 SE. The installer identifies it
  as K644CGO-RGB-PRO. The official product and software links are in README.md.
- The user supplied `37230608.zip`. Its SHA-256 is
  `6ED8475D3908134B077195503DBB167C93B4EEB98AF7D0423FE00377A46AAE61`.
  A fresh download from the official software page produced the same hash.
  The ZIP has one entry, `Redragon K644CGO-RGB-PRO Setup V1.6.6 20221214.exe`.
  The Inno Setup package was extracted under `evidence/vendor/` without
  running its installer. `app/Cfg.ini` identifies the model and VID/PID.
  `KeyboardDrv.exe` identifies the configuration HID collections and report
  formats. The extracted package has no standalone firmware file.
- At initial inventory, Windows reported a generic `25A7:FA70` 2.4G
  receiver. The wired identity was confirmed independently. The later
  2.4 GHz input checks below identify the receiver's keyboard interface.

## Verified wired USB milestone

- `258A:0049` appeared after the user switched to USB. `MI_00` and
  `MI_01/COL04` are keyboard input collections. The USB configuration pair
  under one PnP parent is `MI_01/COL05` (6-byte feature descriptor) and
  `MI_01/COL06` (1032 bytes). Both use HID usage page `FF00`, usage `0001`,
  and have no input/output reports. `COL08` is a separate 382-byte feature
  collection used for the later RGB canvas preview.
- The stock GetProfile operation selects `83 B6` using report ID `05` on
  COL05 and reads report ID `06` from COL06. Wait 20 ms between the calls.
  Windows accepted the stock 7-byte command buffer despite the 6-byte HID
  descriptor length. The result starts `06 83 B6`. Payload starts at byte 8.
- Two full baseline reads matched. A physical `Fn2 + minus` test changed
  just the low nibble of byte 55 from `5` to `1`. Byte 21 is effect `09`,
  identified as Blossoming by the matching vendor effect table. The HUB's
  write control is scoped to this effect and uses the `0`–`4` stock UI range.
- The initial write copied the 136-byte prefix sent by the vendor utility
  and zeroed part of an inactive profile on this firmware. The complete
  original baseline was immediately restored and verified over all 1032
  bytes. Writes now copy all 1024 payload bytes into report `06 03 B6`,
  modifying only the brightness nibble, then compare the complete payload
  on a fresh GetProfile. A synthetic regression fixture covers nonzero
  bytes at the end of the report.
- Real WinForms/WebView2 UI Automation verified USB status, RU/EN, and
  Apply `5 → 2`. An independent read changed only byte 55. After closing and
  reopening the HUB, Restore `2 → 5` returned the exact original 1032-byte
  report again. Raw observations and test captures stay under `evidence/`.
- A physical USB unplug/replug while the app was running showed the
  connected → disconnected → connected sequence. The brightness was read
  again automatically. An independent read after reconnect matched the
  original 1032-byte response.

## Reuse from FURYCUBE G9 PRO HUB

Reference repository at `../FURYCUBE-G9-PRO`, base commit `d2f14a8`,
[public source](https://github.com/fxpw/FURYCUBE-G9-PRO-HUB). Check the current
working tree before using changes after this commit. Its source is MIT-licensed.

| Area | Reference files and what carries over |
| --- | --- |
| App shell and UI | `tools/build-vue-hub.ps1`, `src/G9VueShell.cs`, `src/G9WebApi.cs`, `frontend/src/bridge.ts`, `frontend/src/store.ts` and `frontend/src/i18n.ts`. Reuse C# + WebView2 packaging, a validated message bridge, fake browser state and RU/EN labels. Give the keyboard window its own app ID and storage. |
| Settings and profiles | `src/G9ProfileLibrary.cs`, `frontend/src/components/ProfilesView.vue`. Reuse schema validation, local profile import/export and explicit apply after device readback. Design the keyboard's own settings schema. |
| Key assignments and macros | `frontend/src/components/AssignmentsView.vue`, `frontend/src/components/MacroEditor.vue`, `src/G9Macro.cs`. Reuse controls and semantic macro steps. Define the keyboard's key layout and wire format from observed behavior. |
| Device lifecycle and testing | `src/G9Startup.cs`, `frontend/tests/hub.spec.ts`. Reuse single instance, autostart as a preference, reconnect generation checks and fake bridge tests. On this PC, input filters affected G9 device tests. Verify the keyboard against its own PnP state and ordinary typing. |

G9 firmware, receiver packets, HID interface IDs, DPI controls and OLED renderer
are specific to the mouse. The new device's first UI should contain connection
status, a keyboard layout and RGB settings. Expose key assignments, macros and
profiles as the corresponding capabilities are verified.

## Next hardware work

The user requirements for Caps/Fn layers, the `caps.ahk` migration profile,
and per-key RGB drawing are in [PLAN.md](PLAN.md). Start by checking the
physical layer activators and the keyboard's existing mappings over USB.

Confirm other RGB effects and map device profile selection, key assignments,
and macro storage independently before enabling editors for those features.
Verify 2.4 GHz and Bluetooth separately. Use `tools/Build-Hub.ps1` to build
the Windows app and `tools/Test-RgbSettings.ps1` for the report regression.

## Layer editor, key test, and RGB canvas

- Isolated physical taps established the `MI_01/COL03` four-byte USB Fn reports.
  Fn1 sends `03 20 00 01` and `03 20 00 00`. Fn2 sends `03 20 01 01` and
  `03 00 00 00`. `PhysicalKeys.cs` receives those alongside keyboard Raw Input.
  `KeyTester.vue` shows the physical positions and last delivered Windows key.
- The legacy `caps.exe` consumed Caps before Raw Input could see it. The HUB
  observes Caps through a Windows hook and has an explicit all-keyboards
  runtime switch for the 26-assignment `caps.ahk` preset. The user exited
  the elevated legacy process and physically confirmed the Caps combinations
  in the test tab. Fn and Base assignment edits are local drafts.
- The official utility uses `COL08` for live RGB. The 382-byte report starts
  `08 0A 7A 01` and holds RGB triples indexed by the model's Cfg.ini LED slots.
  The user saw red Esc, green A, and blue Space during a USB probe. `GetFeature`
  on this collection did not return the current frame. The HUB verifies that
  the stored profile is stable, reactivates it after the five-second preview,
  and checks the complete configuration again. The physical probe recovered
  the exact original 1032-byte response.
- The released UI was tested via Windows UI Automation. A drawing preview
  returned the success status and an independent 1032-byte read matched
  the starting configuration. The Caps runtime was enabled in Key test.
  The later autostart checks below cover removal of the legacy task.
- The stock key matrix read using selector `84 D4` produced two identical
  1032-byte replies with header `06 84 D4 00 84 00 00 00`. The dump lives in
  `evidence/usb-stock-matrix.bin`. The record format and individual Fn layer
  storage still require verification before exposing device remapping.
- Key navigation and rendering use the single layout in `frontend/src/keyLayout.ts`.
  Build checks, browser fixture tests, and live checks run independently.

## Configurable layer activators

- The Vue editor and key tester share the persisted layer profile and resolve
  the same captions from the current physical key state. Choose a unique
  switch in the editor for each held layer. Existing v1 assignments load with
  their previous activators as defaults.
- `src/LayerRemap.cs` applies Base and layers with standard Windows keys.
  Standard keys preserve a quick standalone tap. The verified raw Fn events
  show the board's stock layer and suspend Windows translation while held.
  Physical Fn firmware writes are still pending verification.
- Physical USB check used a temporary Space activator for the Caps preset.
  The user confirmed `W → ↑` on the test layout and `ArrowUp` as the Windows
  result. The original Caps activator was then restored.

## Named layers, scoped testing, and live lighting

- The v3 profile migrates v1/v2 bindings and activators, and saves named,
  user-created layers. Editing uses the Caraxes layout for source keys and a
  separate full virtual keyboard for destination keys, including F1–F24,
  navigation, numpad, and multimedia. Existing factory Fn layers stay as
  stock previews. New Windows layer modifiers are standard keys.
- A Windows layer modifier suppresses its down/up pair on both tap and hold.
  The key test loads the draft in a foreground-only hook scope even with the
  global mode disabled. The hook finishes a consumed pair if focus changes.
  The global hook lacks device identity. Live input inspection found only
  kbdclass in the keyboard class UpperFilters, with the Interception-style
  keyboard service stopped. There is no per-device global mode in this build.
- Lighting, effects, and drawing share one tab. Live modes use the proven
  COL08 frame format with wave, aurora, comet, breathing, and an equalizer.
  The equalizer captures default Windows playback with NAudio WASAPI loopback
  and processes 12 frequency bands in memory. Normal stop and normal close
  reapply and verify the unchanged stored profile.
- Windows UI Automation started all five effects. Independent USB reads after
  stop and close during a wave matched all 1032 original bytes. An actual
  950 Hz playback probe peaked in the expected band. Browser smoke tests and
  the workspace and RGB regression scripts passed.

## Layer management and device-only preflight

- Layer creation and editing now use a draft with Save and Cancel. Deletion
  is confirmed separately. A removed Caps preset can be loaded again.
  Playwright covered save, cancel, renaming, new modifier and action, deletion
  after reload, and the key tester. The actual updated WebView2 window showed
  editing and deletion controls. No user's saved layer was deleted for testing.
- Device-only mapping outside the HUB is planned in [PLAN.md](PLAN.md).
  The signed Interception keyboard service exists but is stopped. Neither
  Caraxes keyboard collection has it in its per-device filters, and the
  read-only API probe did not acquire a driver context. The current PowerShell
  session is non-elevated. Driver attachment and firmware remain unchanged.

## Authorized wired filter trial (2026-11-23)

- An elevated trial appended Interception only to the Caraxes MI_01/COL04
  device's UpperFilters and restarted only that collection. PnP reported
  its live stack as kbdclass, keyboard, kbdhid, HidUsb. No input masks were
  set. MI_00 was kept available during the trial.
- All ten keyboard control paths opened, but GET_HARDWARE_ID returned
  ERROR_INVALID_FUNCTION on each. Inspection of this installed driver
  confirmed that the connect handler accepts keyboard class indices 0–9.
  Windows showed KeyboardClass names through index 22 after the trial.
  The selected collection's exact class index was not captured.
- The temporary value was removed, COL04 restarted, and independent PnP
  checks found both Caraxes collections OK with no per-device UpperFilters.
  The keyboard class still lists only kbdclass. The dormant keyboard
  driver remained loaded (Manual) because the elevated stop returned
  1052. No reboot, mouse filter operation, or firmware change was made.
- Local evidence is under `evidence/input-filter-trial-20261123/`. The test
  did not enable device-only remapping or verify Caps outside the HUB.

## 2.4 GHz input and physical key display

- With the wired Caraxes disconnected, the user physically pressed W,
  Caps Lock and left Shift. Both transitions of each key were captured
  through the receiver's `25A7:FA70 / MI_00` keyboard interface. Separate
  Fn1/Fn2 reports were not observed in the monitored input classes.
- The HUB allows the exact tested receiver interface path through Raw Input.
  The local binding lives in `%LOCALAPPDATA%\Caraxes-Pro-HUB\receiver-keyboard.txt`.
  No other collection is accepted on the basis of the receiver VID/PID.
  The header indicates receiver presence independently of the wired settings
  channel. Radio input is available to Key test. Per-device suppression
  outside the HUB is still pending.
- The layers editor and key test show only the active destination at a
  mapped position. The key test reports the event received by WebView and
  shows arrows with their Windows key names. The two timestamped histories
  show physical and Windows transitions newest first. The bounded local `key-test.log`
  records raw/consumed transitions, mapping send success, and WebView key
  codes while the test has focus. USB Fn reports still drive layer previews.
- Windows navigation output uses extended scan codes. A focused tester can
  retry a mapped key when it arrives unconsumed from the known keyboard via
  Raw Input. Other applications use the Windows hook. Logging also records
  window close reasons and WebView process faults during a test session.
  The user verified Caps + W over 2.4 GHz. The live test log showed physical
  W, successful ArrowUp dispatch and ArrowUp down/up in WebView. During the
  focused retry, the original W is marked as source in the file and appears
  only in the physical column. Both final columns were verified in the
  desktop accessibility tree and in the browser fixture.
- By explicit user request the existing Windows-wide runtime starts with
  the HUB by default. An explicit stop persists across launches. It still
  refuses to start while `caps.exe` is running.
- The receiver exposed one vendor Feature report, `MI_01/COL06`, page
  `FF02`, usage `0002`, length 8, ID `06`. Its advertised report read
  succeeded. It has no collection matching the wired RGB settings or
  382-byte frame transport. No feature writes were sent to the receiver.
  Evidence stays under `evidence/radio-input-20261123/`.

## Tray and lighting recovery

- Minimize and the window close button hide the same WinForms instance.
  Windows layers continue to run. The notification icon has Open and Exit
  actions. A second executable launch signals the existing instance to show
  its window. Native hide/reopen checks preserved the PID, restored the
  WebView and found Windows layers still enabled. Explorer recorded the icon
  registration. The custom taskbar hides the native shell notification area.
- With the keyboard dark over USB, two stable reads identified mode `0x15`
  and a difference of three bytes from an earlier lit profile. `Fn2 + 0`
  did not change the stored report during this test. Changing only byte 21
  to `0x09` preserved the other 1031 bytes and restored visible lighting at
  brightness 5. The user confirmed the keyboard was lit.
- Windows UI Automation tested **Restore Blossoming**, **Restore previous
  mode**, and recovery again. Independent paired readbacks matched all 1032
  expected bytes at each step. Lit `0x09` was left active. Recovery is gated
  to the verified starting mode and undo is tied to the device and resulting
  profile. Evidence is in `evidence/tray-lighting-20261123/`.
- With the cable disconnected, the user confirmed the same lighting color
  on 2.4 GHz. With the HUB still hidden in the tray, an independent Windows
  window recorded four ArrowUp down/up pairs from the physical Caps test.
  PnP showed the receiver and zero wired Caraxes collections. These checks
  verify retention and the tray runtime. Direct radio RGB control is pending.

## Direct 2.4 GHz RGB investigation (2026-11-23)

- Read the live USB configuration for receiver `25A7:FA70`, revision
  `0300`. It exposes one configuration with two HID interfaces, each with
  alternate setting 0 and one 8-byte interrupt IN endpoint. No vendor
  interface or HID LampArray was present. Standard keyboard LED output and
  a separate 8-byte `FF02` Feature report are available through HID.
- Instrumented the official V1.6.6 `KeyboardDrv.exe` startup with only the
  receiver attached. It enumerated nine receiver HID collections and opened
  none. A scratch configuration substituting the receiver VID/PID opened all
  nine collections and issued zero Feature reads or writes. Both x86 and x64
  tracing controls observed a successful GET of receiver report `06`, eight
  bytes. The unchanged receiver reply was `06 00 02 00 00 00 7A AA`.
- The wired settings and live RGB transports have no demonstrated equivalent
  in the receiver. A receiver-specific RGB protocol or a matching utility
  trace that actually controls RGB on 2.4 GHz is still needed. Evidence is
  under `evidence/radio-rgb-20261123/`. The probes made no hardware writes.

## Hardware identification for 2.4 GHz RGB (2026-11-23)

- Manufacturer FCC exhibit `TUVET-8858A`, document `6637475`, contains
  internal photos of the `K644CGO-RGB-PRO` / `ET-8858` sample. The test
  report identifies hardware `8858-A TX V1` and software `2A56`. Photo 003
  shows board silk `8858-A-US,UK BR TX V1`. Photo 004 shows the main U1,
  a separate U3 near the antenna, and pads J3. U1 appears to be marked
  `BYK916`; U3 is unreadable. These are reference photos, and the user's
  board revision, chip markings, and J3 pinout still need confirmation.
  The photos do not show the USB receiver PCB.
- Tecware Phantom+ Elite captures from OpenRGB issue 3637 list the same
  wired and receiver VID/PID pair as Caraxes. All lighting traffic in the
  three captures uses the wired-format Feature reports 05, 06, and 08
  (6, 1032, and 382 bytes respectively). They contain no 8-byte receiver
  lighting trace. Capture files and FCC photo crops are under
  `evidence/radio-rgb-20261123/`.
- `sinowisp` at `242f7921` has no Caraxes profile. Its standard `read`
  cycle unconditionally sends Enable Firmware before reading and can set
  a flash byte. It was inspected offline only. If U1 is `BYK916`, the
  published hardware reader `sinodude-serial` uses clocked, four-wire
  SinoWealth ICP and a controlled target power cycle. The user has an
  FT232RL USB-UART adapter. A confirmed UART could be monitored with
  this adapter after verifying logic levels and pinout.
- Next, obtain macro photos of the actual keyboard U1, U3, J3 and receiver
  IC, including PCB revision. Establish which interface can be read and
  preserve the stock firmware if reading is supported. Direct USB receiver
  control still needs receiver-side protocol evidence. No commands were
  sent to enter a bootloader, write RGB or flash, or alter pairing.

## Receiver report in the HUB (2026-11-23)

- In radio-only mode, **Lighting → Read receiver report** requests the
  observed Feature `06` from `25A7:FA70 / MI_01 / COL06`. The backend
  resolves it to the same USB parent as the bound Raw Input keyboard,
  then checks revision `0300`, page `FF02`, usage `0002`, and 8-byte length.
  The operation shows the raw reply and does not send a SetFeature command.
- Built the native HUB and checked the button in the live WebView2 window
  in RU and EN with the receiver present and no wired keyboard. Both reads
  showed `06 00 02 00 00 00 7A AA` and RGB Apply stayed disabled. The
  workspace and RGB regression checks passed. Logs are under
  `evidence/radio-rgb-20261123/receiver-hub-uia.txt`.

## Caps + Esc parity with stock Fn1 + Esc (2026-11-23)

- The active profile already used physical Caps as its layer key. The old
  `EscapeAction` sent Unicode packets, which WebView logged as `Unidentified`.
  The same saved action now sends the physical Backquote scan code `0x29`.
  Windows produces the character for the active layout. Shift/Ctrl behavior
  of the Caps preset stays in the action; the tester shows stock Backquote
  input at its physical Esc position on the Caraxes layout.
- When Caps is reserved for a layer, the HUB checks that Caps Lock is off
  on focus and after an observed physical Caps release in the focused HUB.
- In a live 2.4 GHz test with the cable absent, the user confirmed the
  combinations and lowercase check. The log contains `Backquote` down/up
  for Caps + Esc and for stock Fn1 + Esc. Workspace, RGB and build checks
  passed. Before/after logs are under `evidence/caps-escape-20261123/`.

## Manual Windows Caps Lock (2026-11-23)

- The Layers tab shows the current Windows Caps Lock state and toggles
  between lowercase and uppercase without changing any layer assignment.
  The native host reads the Windows toggle bit in the focused HUB and
  verifies it after the synthetic key tap. While Caps is a layer trigger,
  focus and Caps release recovery respect the last chosen state for this
  session. A new process uses lowercase as its initial target.
- Tested the RU and EN controls in the updated WebView2 window with the
  receiver only. An independent WinForms window observed Caps on and `A`,
  then Caps off and `a`. Windows layers stayed enabled. The HUB was left
  on Layers with lowercase selected. Evidence is in
  `evidence/caps-lock-ui-20261123/caps-external-window.txt`.
- Older logs show Raw Input Caps transitions during a layer session. They
  did not log the toggle bit at those moments, so the specific cause of
  the previously reported unexpected uppercase state remains unconfirmed.

## Autostart, workspace icon, and Caps reservation (2026-11-23)

- Settings reads and changes the current-user `Redragon Caraxes Pro HUB`
  Run value. `--minimized` initializes the same HUB in the tray without
  showing the form. A regular launch opens that instance. Both installed
  HUBs use the supplied Workspace favicon as their EXE, form and tray icon.
- The legacy `\caps` logon task launched `caps.bat`. Exported its task XML
  to the local HUB backups and removed the task. Both HUB logon registrations
  point to their running executables with `--minimized`.
- The Layers editor reserves Caps when assigned as a layer key. The
  foreground hook captures its transitions even if global layers are off,
  and a focus change by itself no longer sends a Caps toggle. The explicit
  case button in Layers still controls the Windows toggle state.
- Verified the checkbox against Windows Run, both icons against the source
  ICO, simulated minimized launches, the absence of `\caps`, and preserved
  case while clicking layer tabs. Browser and native workspace/RGB checks
  passed. A Windows sign-out/sign-in was not run during this check.
