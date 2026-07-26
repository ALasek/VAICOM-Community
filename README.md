# VAICOM Standalone Speech

> [!IMPORTANT]
> This is my personal, vibe-coded, experimental fork of [VAICOM Community](https://github.com/Penecruz/VAICOM-Community). It is not an official VAICOM release and is not maintained, supported, or endorsed by the upstream VAICOM Community team.

This fork lets VAICOM handle supported DCS radio commands without using VoiceAttack as its speech and push-to-talk host. It keeps VAICOM's existing DCS integration, command database, aliases, radio routing, and live game-state matching, while adding a standalone local speech host.

The "vibe-coded" label is deliberate: this was built through iterative AI-assisted development and local testing. It has automated smoke tests and has been exercised in DCS, but it should still be treated as experimental software that modifies DCS integration files.

## What this fork adds

- Fully local speech recognition; audio and transcripts are not sent to Codex, OpenAI, or another cloud service.
- Constrained Vosk recognition by default, optimized for VAICOM's short command phrases.
- Optional local Whisper fallback.
- Persistent microphone selection and recognition status in the VAICOM configuration window.
- Global keyboard and DirectInput/HOTAS bindings for TX1 and TX2.
- `Ctrl+Alt+C` to open the VAICOM configuration window while the standalone host is running.
- Deterministic command cleanup and conservative fuzzy recovery for common transcription errors.
- Voice-driven stock radio-menu traversal, selected campaign prompts, and a focused set of Mi-24P Petrovich gunner commands.
- Backups of DCS files before the standalone host patches them.

## Scope

This replaces the VoiceAttack **host used by VAICOM**, not VoiceAttack as a general automation product. VAICOM's normal recipient, command, radio, module, and live-DCS-state logic remains authoritative.

Current limitations include:

- Standalone voice PTT is implemented for TX1 and TX2. Additional transmit nodes remain future work.
- VoiceAttack wildcard segment behavior is only approximated, so some frequency-tuning and AIRIO profile macros may need further work.
- The optional in-cockpit VAICOM kneeboard extension is disabled to avoid its aircraft-file modifications and associated multiplayer integrity-check problems.
- Recognition and DCS behavior still need broader testing across aircraft, missions, accents, microphones, and multiplayer environments.

See [STANDALONE.md](STANDALONE.md) for the complete operating notes, command examples, recovery instructions, and current technical boundary.

## Build and run

This is currently a source-first experimental fork, not a polished installer release.

1. From Windows PowerShell in the repository root, run `./build-standalone.ps1`.
2. The script restores and builds the .NET Framework 4.7.2 projects, downloads the local recognition models when needed, and creates `dist/VAICOM-Whisper-Standalone`.
3. Run `Start-VAICOM.cmd` from that generated directory.
4. In the **Host** tab, select a microphone and bind TX1 and TX2.
5. Start DCS and leave the standalone host running for the session.

Start with [the detailed setup guide](STANDALONE.md) before using it against a DCS installation. Back up any DCS or Saved Games scripts you maintain manually.

## Upstream project and attribution

VAICOM and its DCS integration were created by the original VAICOM developers and are maintained by the VAICOM Community project. This fork would not exist without that work.

- [VAICOM Community source and official project information](https://github.com/Penecruz/VAICOM-Community)
- [VAICOM Community releases](https://github.com/Penecruz/VAICOMPRO-Community/releases/latest)
- [VAICOM Community Discord](https://discord.gg/7c22BHNSCS)

Use this fork's issue tracker for problems introduced by the standalone speech host. Do not ask the upstream maintainers to support these experimental changes.

## License

The project remains under the upstream [MIT License](LICENCE.md). Existing copyright, license, and attribution notices are retained.
