# Windows app: where things stand (handover)

Written 23 November 2026, when the Windows app was first pushed. Read this with `SETUP.md` §13, `windows/README.md`, `windows/DESIGN.md` and `docs/UI-UX-PLAN.md`. It records what was in the build session's head, so a new session can pick up without it.

## Done

- **The app.** A WinUI 3 app (Windows App SDK 2.6.4, .NET 10, unpackaged and self-contained) in four projects:
  - `DesktopBuddy.Core`: models, rules, sources, auth and state, using only the base library. The JSON is compatible with the Mac app.
  - `DesktopBuddy.Presentation`: the view models, with no WinUI.
  - `DesktopBuddy.App`: the WinUI app.
  - `DesktopBuddy.Tests`: 572 xUnit tests, all passing.
- **Design.** The "Lily pad" direction (A), chosen by the owner on 14 November 2026. Everything is in `DESIGN.md`: purpose-named tokens for Light, Dark and High Contrast, the `TokenView` signature component, and motion that only fades and scales.
- **Tasks finished and reviewed by the lead:**
  - P0 (foundations).
  - T1 (tokens and components).
  - T2 (config fields and pause).
  - T3 (the Board, opened from the tray or Ctrl+Alt+N).
  - T4 (the Rail and the Peek).
  - T5 (Settings with six sections, using Community Toolkit `SettingsCard`).
- **Tray.** Left click opens the Board. The right-click menu has Open Board, Pause for 1 hour or until tomorrow, Resume, Check now, Settings and Quit. The icon gets a coral dot while items are waiting.
- **Build.** `build-windows.ps1` runs the tests, publishes, and runs `--selftest` (12 platform checks). With `-Share` it also makes the zip and its `.sha256`.

## Open decisions for the owner

1. **Rail and Peek window shape.** The current pill is cut with a window region, which leaves jagged edges and no shadow.
   - Recommended: native Windows 11 rounded corners (DWM, 8 px) with the system shadow.
   - The alternative is to keep the pill.
   - Once decided, T4 applies it and tidies the pill radius and off-centre tokens.

## Decided, still to write into DESIGN.md

- The Rail has no keyboard entry of its own. The Board (Ctrl+Alt+N) is the keyboard route to everything on the Rail.
- Record the live panel radius after the shape change.

## Still to do

- **T6:**
  - Move strings into `.resw`.
  - Give every control an `AutomationId`.
  - Do an accessibility sweep.
  - The Settings pages are built in C#, not XAML with `x:Bind`. This was an accepted deviation; flag it if it causes trouble.
- **T7, testing (plan §7.1):**
  - Unit tests and `--selftest`.
  - Snapshot review in light, dark and High Contrast, at 100% and 150%.
  - Keyboard and Narrator passes, a code review, and a release smoke test.
  - The plan also includes `winapp` UI automation. That needs `Microsoft.WinAppCli`, and the winget install failed twice with 0x80072ee2 (a network timeout), so retry it.
- **T8:** final review and docs.
- **Known warning:** CS8601 in `Views/Settings/SettingsKit.cs` line 340 (a possible null assignment).

## Useful facts

- **Parallel builds.** Agents building at the same time use `dotnet build --artifacts-path <scratch>/<task>`. The exe lands in `<path>/bin/DesktopBuddy.App/debug_win-x64/`.
- **Logos.** If a build folder is missing the logo PNGs, the Rail shows initials instead of logos. `LogoTile.Load` now writes "Logo file missing" to `status.log`; if you see initials, check that first.
- **Publishing.** `<EnableMsixTooling>true</EnableMsixTooling>` is required. Without it, publish leaves out the `.pri` and `.xbf` files and the app crashes on start.
- **Snapshots.** `DesktopBuddy.exe --snapshot <folder> [--only name] [--designlab]` renders screens to PNG in light and dark. `--only components` renders the living component reference.
- **Skills used for this work** (installed per machine, not in the repo):
  - From Microsoft (`npx skills add microsoft/winappCli@…`): `winui-design`, `winui-code-review`, `winui-ui-testing` and `winui-dev-workflow`.
  - From OpenAI: `winui-app`.
  - NVIDIA SkillSpector rated:
    - `winui-design`, `winui-code-review` and `winui-ui-testing`: approve.
    - `winui-dev-workflow`: mild caution.
    - `winui-app`: caution (75 high findings), so use it with care.
