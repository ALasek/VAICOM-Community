# VAICOM Standalone with local constrained speech recognition

This build replaces the VoiceAttack host, not VAICOM's DCS logic. The executable captures a push-to-talk utterance, recognizes it locally with constrained Vosk or Whisper, exposes that transcript through VoiceAttack-compatible `{CMD}` and segment APIs, and invokes VAICOM's original `alias.aicomms` entry point. VAICOM still owns its complete recipient, command, radio, module, and live-DCS-state matching. Exact aliases remain the first choice; when no command alias matches, a strict deterministic fuzzy fallback can recover a close command only when it clears both a confidence threshold and a winner-margin threshold.

Runtime defaults:

- Whisper model: `small.en`
- Whisper runtime: CUDA first, CPU fallback
- Default ASR: constrained Vosk grammar generated from current VAICOM aliases
- Radio 1 default push to talk: global `F13` -> `TX1`
- Radio 2 default push to talk: global `F14` -> `TX2`

VAICOM discovers the normal DCS installation and Saved Games locations. Use `--dcs-path PATH` when DCS is installed somewhere the existing VAICOM discovery does not find.

## Start

1. Run `Start-VAICOM.cmd`. The VAICOM configuration window opens automatically on the first run.
2. Open the **Host** tab, choose the recognition backend, select the microphone, then use **Bind** beside TX1 and TX2. Press either a keyboard key or a HOTAS button when prompted.
3. Start DCS.
4. Hold the PTT for the intended radio, speak a normal VAICOM phrase such as "Flight, rejoin," and release it. The console prints the TX node, transcript, and VAICOM result.

Say `Got it` while holding either standalone PTT to send a Space keypress for campaign prompts that require Space to continue. For safety, the keypress is sent only while DCS is the foreground application.

Start VAICOM before DCS so the DCS export bridge can connect immediately. Keep the console application running for the whole DCS session.

## Build from source

From a Windows PowerShell prompt in the repository root, run `./build-standalone.ps1`. It restores and builds the .NET Framework 4.7.2 projects, downloads the local Whisper and Vosk models if absent, and writes the runnable package to the Git-ignored `dist/VAICOM-Whisper-Standalone` directory.

## Useful options

```text
--model PATH                another Whisper.net GGML model
--asr vosk|hybrid|whisper   override and persist the recognition backend
--list-devices              list recording devices
--mic NUMBER                -1 is the Windows default device
--ptt-key-tx1 KEY           map a global keyboard key to TX1
--ptt-key-tx2 KEY           map a global keyboard key to TX2
--ptt-key KEY               legacy single-radio PTT key
--tx 1..6                   legacy text TX; standalone voice PTT supports TX1/TX2
--open-config               open the configuration window at startup
--dcs-path PATH             persist a custom DCS installation path
--no-install-dcs-files      initialize without touching DCS integration files
--initialize-only           initialize VAICOM and exit
--text "flight rejoin"      exercise the live VAICOM path without audio (DCS must be connected)
--self-test                 exercise VAICOM's real offline command parser
```

Press `Ctrl+Alt+C` at any time while the host is running to open, restore, and foreground the configuration window, including before DCS starts. The **Host** tab shows the microphone, recognition backend, engine and DCS status, and the two persisted PTT bindings. The standalone host forces VAICOM's extended command database on because there is no VoiceAttack profile to provide a separate recognition whitelist.

Writable VAICOM configuration, database, log, and `host-settings.json` files live in `%LOCALAPPDATA%\VAICOM-Standalone`; the application folder can therefore be read-only.

## DCS integration and recovery

VAICOM adds its block to the existing `Export.lua` while retaining unrelated integrations. It also installs the VAICOM export script and DCS-side radio hooks.

This fork adds a safety layer missing upstream: before patching an existing DCS file, it stores the exact file beside it as `*.vaicom-standalone.original`. F-14 and other temporary AIRIO replacements are restored from those bytes when the host exits. Back up the DCS and Saved Games files you customize before first use.

After a DCS update, start this host once before flying so VAICOM can rebuild its hooks. If an update changes a file that still has an old `*.vaicom-standalone.original` sidecar, preserve the new DCS file and remove the stale sidecar before starting the host; it will capture the new original.

For a full rollback, stop VAICOM and DCS, restore your backups, and remove the VAICOM block from `Saved Games\DCS\Scripts\Export.lua` plus the `Saved Games\DCS\Scripts\VAICOMPRO` folder. A DCS repair also restores files under the DCS installation, but it does not clean Saved Games.

## Mi-24P Petrovich gunner commands

Use either `Petrovich` or `Gunner` as the prefix. The most useful phrases are:

- `Gunner weapons on`
- `Gunner search boresight`
- `Gunner search forward`
- `Gunner search pilot line of sight`
- `Gunner clear search` or `Gunner stop search`
- `Gunner toggle target selection`
- `Gunner previous target`, `next target`, or `select target`
- `Gunner cycle missile`, `fire`, or `toggle R O E`
- `Gunner countermeasure interval`, `series`, `left`, `right`, or `type`
- `Gunner dispense countermeasures` or `Gunner flares`

These commands use DCS's existing Mi-24P Petrovich and ASO-2V cockpit actions. Search, target-list, fire, ROE, and prepare-weapons actions remain context-sensitive exactly like the Petrovich wheel; `weapons on` invokes prepare weapons rather than reading and forcing a known final state. Countermeasure option phrases directly cycle the named ASO-2V control and do not navigate the wheel.

## Current boundary

- Speech recognition is fully local. No transcript is sent to OpenAI, Codex, or another service. Vosk receives a grammar regenerated from VAICOM's current input aliases for every utterance; VAICOM's exact and conservative fuzzy matching still decides whether the result is a valid command.
- The standalone host disables VAICOM's optional in-cockpit kneeboard extension. That extension modifies each aircraft's `device_init.lua` and can fail DCS multiplayer integrity checks; radio and command integration do not require it.
- Matching is VAICOM's existing deterministic alias/state pipeline; semantic GPT matching is not part of this first build.
- Known ASR artifacts are corrected contextually (for example, `a board to take off` becomes `abort takeoff`). Fuzzy recovery applies only to commands, never short aliases or recipients, and logs accepted or near-threshold rejected candidates to the console.
- PTT accepts global keyboard keys and DirectInput controller buttons in background, non-exclusive mode. Controller bindings are stored by device instance identifier plus zero-based button index; a missing device is shown as disconnected and is never silently remapped.
- Radio and Jester menus remain open between standalone PTT utterances so commands such as `Take One` can traverse multiple levels. A terminal selection is closed by DCS; say `Take Twelve` to explicitly close a stock radio menu.
- The binder prefers a physical controller over a simultaneous vJoy/virtual-device edge. Joystick Gremlin or HidHide can still change which devices Windows exposes, so rebind after changing that setup.
- Utterances are processed serially. A second PTT pressed while another is active or transcribing is ignored and must be released and pressed again.
- TX nodes are mapped by VAICOM from the active aircraft and live DCS state. TX1/TX2 are normally the first two radio mappings, but single-PTT aircraft such as the AH-64D may require VAICOM's MULTI PTT mode before both nodes are enabled.
- VoiceAttack wildcard `{CMDSEGMENT:n}` values are approximated with whitespace-separated transcript tokens. Stock recipient/command aliases do not depend on this, but segment-driven frequency tuning and some AIRIO macros need live validation and may require profile-pattern-aware segmentation.
