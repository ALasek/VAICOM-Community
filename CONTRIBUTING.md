# Contributing to VAICOM Community noVA

This is a personal, experimental fork of [VAICOM Community](https://github.com/Penecruz/VAICOM-Community). Contributions to the standalone speech host are welcome; changes intended for the original VoiceAttack-based project should be proposed upstream instead.

## Before opening a pull request

1. Create a branch from `main`.
2. Build the standalone solution on Windows:

   ```powershell
   dotnet restore VAICOM.Standalone.sln
   dotnet build VAICOM.Standalone.sln -c Release -p:StandaloneBuild=true -m:1 --no-restore
   ```

3. Run the compiled smoke tests:

   ```powershell
   .\VAICOM.Standalone.Tests\bin\Release\net472\VAICOM.Standalone.Tests.exe
   ```

4. Update `README.md` or `STANDALONE.md` when behavior or setup changes.
5. Keep machine-specific paths, logs, models, generated packages, and AI-assistant notes out of commits.

DCS does not need to be running for the build and smoke tests. Live DCS validation is still required for changes to radio routing, aircraft integrations, menus, or installed DCS scripts.

Open pull requests against `main`. Use the [fork's issue tracker](https://github.com/ALasek/VAICOM-Community-noVA/issues) for reproducible noVA bugs and feature proposals.
