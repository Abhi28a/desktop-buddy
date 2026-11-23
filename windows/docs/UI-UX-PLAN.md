# Desktop Buddy for Windows: UI and UX redesign plan

**Lead:** Opus (plans, sets the design system, reviews every task, signs off).
**Status:** approved on 14 November 2026 with the recommended decisions. P0, T1 and T2 are done; T3, T4 and T5 are in progress.

**Decisions taken:**
1. Direction **A, Lily pad** (chosen from `docs/directions/`). The design system is recorded in `../DESIGN.md`.
2. Community Toolkit `SettingsControls` and `CommunityToolkit.Mvvm` are allowed.
3. The global shortcut is `Ctrl+Alt+N`.
4. Installing Microsoft's `winapp` tool failed twice with a network timeout (`0x80072ee2`). Retry before T7.
5. Windows notifications: the T2 spike found that toasts and their buttons work while the app runs, but clicking a toast after the app has quit does nothing, and it needs an explicit app ID plus a Start-menu shortcut. **Not built.** Revisit when cold activation is solved.
**Applies to:** `windows/` on the `feature/windows-app` branch. The Mac app and `DesktopBuddy.Core` behaviour don't change.

Sources this plan follows: Microsoft's `winui-design`, `winui-code-review` and `winui-ui-testing` skills, OpenAI's `winui-app` skill, and the owner's design skills (`impeccable`, `top-design`, `hallmark`, `high-end-visual-design`). Quotes from them are marked.

---

## 1. What's wrong today

The owner's verdict: the UI "is not at all good". The audit agrees. This is a **redesign, not a polish**: keep what the product does, its wording and its native behaviour, and treat today's look as what to move away from (`impeccable`: "Never split the difference into polish on the discarded look").

### The experience (biggest problems)

1. **Four surfaces show the same list.** The pop-up, the dock, the notification list and the tray flyout each show the same notifications in slightly different ways. Nobody can tell which one to use.
2. **The tray flyout is a control panel, not a glance.** Account on/off switches sit at the top of the thing you open dozens of times a day, one misclick away from turning off a whole account. "Sign In Again…" sits in the middle of the list.
3. **Triage is slow.** Done and snooze only appear on hover. There's no keyboard path and no global shortcut. Each card is four lines of similar-weight text, so you can't scan it.
4. **The pop-up ignores Windows.** It interrupts during presentations, full-screen video and Windows "Do not disturb". The spinning countdown ring reads as "loading". "View more ⌄" is ambiguous.
5. **The dock reads as a second taskbar.** A grey slab with a logo, name and red badge for every client sits next to the real taskbar and the tray.
6. **The Settings sidebar is overloaded.** Accounts, clients and app settings are all in one long NavigationView with unexplained dots and yellow and grey numbers. Microsoft's skill lists "Reflexive NavigationView Left" and long mixed lists as anti-patterns.

### The look

- Stock Fluent with nothing to remember it by (`top-design`: "No signature moment | Competent but forgettable").
- Logos are repeated: the group header logo and the card logo appear side by side. Nested surfaces: a card inside a card inside acrylic (`hallmark` bans "a card inside a card").
- Pure white and near-black surfaces, a grey 1px border on everything, and default red badges.
- Errors are yellow text on dark: hard to read and colour-only (Microsoft: "never colour-only").
- Every setting is its own separate card, so pages are long stacks of identical boxes. The four service rows should be one group.
- Code-behind UI with hard-coded sizes and strings, which can't be localised, themed for High Contrast or reliably UI-tested (no AutomationIds).

---

## 2. Principles for the redesign

1. **Operate mode** (`impeccable`): "Scanability, consistency, native expectations, and the real usage scene outrank expression. Brand lives in precise details."
2. **One job per surface.** Every surface answers one question (§3).
3. **One signature moment, everything else quiet** (`top-design`: "Design the signature moment first"; "3 things perfect beats 10 things mediocre").
4. **Native first.** Built-in WinUI controls before custom ones (Microsoft's "custom UI gate"). Mica for long-lived windows, Acrylic for transient ones. Theme resources, never colour literals. Light, Dark and High Contrast all work.
5. **Keyboard-first triage.** Every action works without a mouse.
6. **Respect the person's attention.** Follow Windows "Do not disturb", presentations and full-screen apps, as well as our own quiet hours.
7. **House rules stay:** Segoe UI Variable (the system font), no slide-in or slide-out motion, plain short copy, light and dark mode, no "AI slop".

---

## 3. The new experience model

| Surface | Its one job | Today | New |
|---|---|---|---|
| **Peek** (the pop-up) | "Something just arrived. Deal with it or ignore it." | Black card, spinner, "View more ⌄" | One item, three actions (**Open**, **Done**, **Later**), a quiet time bar, then gone. Waits while Windows is in Do not disturb, presenting or full screen. |
| **Rail** (the dock) | "Who's waiting for me?" at a glance | Grey slab with logo, name and red badge per owner | A slim strip of client tokens on the screen edge. Names show on hover and focus. A token for a meeting about to start shows how close it is. Hides in full screen. |
| **Board** (one triage panel) | "Go through everything and clear it." | The notification list and the tray flyout list | **One** panel replaces both. Opens from the tray (anchored to the tray) or from a Rail token (anchored to the Rail, filtered to that client). Keyboard-first: arrows, Enter to open, E for Done, S for Later, Ctrl+Z to undo. |
| **Tray icon** | Entry point | Left click opens a flyout with switches and a list | Left click opens the **Board**. Right click opens a short menu: Pause for 1 hour, Pause until tomorrow, Settings, Quit. The icon shows a small dot when something is waiting. |
| **Settings** | Set it up once, fix problems | One long sidebar mixing accounts, clients and preferences | Six clear sections (§5). Problems surface on **Home**. |

**Account health leaves the glance surfaces.** When something needs attention (expired sign-in, a broken service), the Board shows a single InfoBar: "IE Hub: Google Chat isn't working. **Fix**". The fix opens Settings on the right page. The account switches live only in Settings.

**Global shortcut:** a configurable shortcut opens the Board from anywhere (default `Ctrl+Alt+N`, changeable in Settings and checked for conflicts).

**Windows notifications (optional, behind a spike):** a setting "Also show in Windows notifications" would use `AppNotificationManager`, so items reach Notification Center and Focus. The `winui-app` skill warns about package-identity APIs in unpackaged apps, so task T2 checks whether this works unpackaged before anyone builds it.

---

## 4. Visual identity: three directions, one recommended

The owner picks one in Phase 0 from rendered mock-ups of the real screens (Peek, Rail, Board, a Settings page) in light and dark. Mirror test (`hallmark`): if a neighbouring app could have the same combination, change one decision.

### A. Lily pad (recommended)

The identity comes from the Dynamatix mark, which is already the app icon: brown ink, lily-pad teal, coral, sage green, slate.

- **Surfaces:** warm neutrals, not pure white or black (`top-design`): paper `#FAFAF8` and ink `#0E0E0D`, with Mica and Acrylic on top. Text is ink-tinted, not grey.
- **One accent:** lily teal for selection and the primary action. **Coral replaces red** for "waiting" counts. Teal at a lower intensity carries "all clear".
- **Signature moment, the token:** every client logo sits on a soft round pad. Its count lives in a small *notch* cut into the pad's edge, like a lily pad's slit, instead of a red bubble stuck on top. The same token appears on the Rail, the Peek and the Board, so a client always looks the same everywhere. When something arrives, its token **settles**: a fade plus a 4% scale-in, under 250 ms. No slide.
- **Type:** Segoe UI Variable Display for page titles and the Peek sender, Text for everything else, tabular numerals for counts and times. Fluent type ramp: Caption 12, Body 14, Body Strong 14, Subtitle 20, Title 28.
- **Why:** distinctive, calm, unmistakably this product, and still native Windows 11.

### B. Ledger

Editorial and nearly monochrome. The Board reads like a well-set ledger: a time column, sender, subject, and hairline rules (`DividerStrokeColorDefaultBrush`) instead of cards. One coral signal colour. The quietest and most "pro tool".
Risk: can feel austere. Logos are the only colour.

### C. Beacon

Dark-first. Each client's colour shows as a thin light bar on the Rail's screen edge that glows softly when items wait; Peek is a dark glass card with a client-coloured edge.
Risk: the most expressive, easy to tip into "gamer" or "AI slop", and the light theme is weaker.

### Design tokens (same structure whichever direction wins)

All in `Styles/Tokens.xaml` with `Light`, `Dark` and `HighContrast` theme dictionaries, named **by purpose, never by hue** (Microsoft):

| Group | Tokens |
|---|---|
| Surfaces | `SurfaceBase`, `SurfaceRaised`, `SurfaceSunken`, `SurfaceOverlay` (Acrylic), `SurfaceInverse` (Peek) |
| Text | `TextPrimary`, `TextSecondary`, `TextTertiary`, `TextOnAccent`, `TextOnInverse` |
| Signal | `Accent`, `Waiting` (coral), `Urgent` (meeting starting), `Positive`, `Caution`, `Critical` |
| Lines | `StrokeSubtle`, `StrokeFocus` (High Contrast falls back to `SystemColor*`) |
| Radius | One per nesting level, concentric (`high-end-visual-design`): window/overlay 8, card 6, control 4, token full circle |
| Space | 4 px grid: 4, 8, 12, 16, 24, 32, 48 |
| Motion | Fast 83 ms, Normal 167 ms, Slow 250 ms. Enter decelerates, exit accelerates. Only opacity and scale. Everything turns into a plain fade when Windows "Animation effects" is off. |
| Elevation | ThemeShadow 32 for flyouts and the Board, 16 for tooltips, none on cards |

---

## 5. Settings: new structure

`NavigationView` (Left, compact when the window is narrow) with six sections. A `BreadcrumbBar` for drill-in pages.

| Section | What's on it |
|---|---|
| **Home** | Status at a glance: what's connected, anything needing attention (InfoBars with Fix), today's counts, quick **Pause** and **Quiet hours**. This is where Settings opens. On first run it's the sign-in page. |
| **Accounts** | A list of accounts (token, name, email, status in words). Click to open the account page (BreadcrumbBar "Accounts › IE Hub"). The account page has the services as **one grouped card** with a status line per row, then mail filters, appearance, Sign out and Remove. **Add account** is a button on the list. |
| **Clients** | The same pattern: list, then client page (rules, chats, look-in, Needs attention). |
| **Notifications** | Peek (on, where, how long), Rail (edge, size, compact), Board shortcut, sounds per type, quiet hours, Do not disturb behaviour, screen-share privacy, Windows notifications (if T2 says yes). |
| **Reminders** | As today, in the new components. |
| **About** | Version, updates, Report a bug, the welcome tour, open at sign-in. **Team setup** appears here only when Shift is held. |

Rows use a `SettingsCard` / `SettingsExpander` pattern (Microsoft's "Settings / config tool" silhouette). Either we add the Community Toolkit's SettingsControls package, or we build two small components of our own ([§8](#8-decisions-for-the-owner), decision 2). Errors use an `InfoBar` with an icon, never yellow text. Destructive actions confirm in a `ContentDialog` with a verb button ("Remove account") and the name in the body.

---

## 6. How it's built

- **XAML and view models, not code-behind.**
  - A new `src/DesktopBuddy.Presentation` project (`net10.0`, no WinUI) holds the view models: Board, Rail, Peek, each Settings page. They wrap `AppState`, so they can be unit-tested with the existing xUnit setup.
  - Views are XAML with `{x:Bind}` (one-way or two-way where values change; `x:DataType` on templates).
- **Strings** move to `Strings/en-us/Resources.resw` with `x:Uid` (localisable, no concatenated sentences).
- **AutomationIds** on every interactive control (`AutomationProperties.AutomationId`), and names on every icon-only button. Tests select by these, so they stay stable.
- **Board list:** a virtualised `ListView` grouped by owner, with no `ScrollViewer` wrapped around it.
- **Windows-aware attention:** `SHQueryUserNotificationState` (busy, presentation, full screen, quiet time) decides whether Peek shows. `RegisterHotKey` handles the global shortcut.
- **Core stays as is.** New settings fields (shortcut, Peek and Rail options, Windows-notifications switch) are added to `AppConfig` with defaults, so old `config.json` files still load. They're covered by tests.
- The existing `--snapshot` and `--selftest` modes keep working and gain the new screens.

---

## 7. Tasks for agents

Opus leads: it writes every brief, reviews every diff and screenshot, sends fixes back to the same agent, and signs off each phase. Model choice follows `model-routing`: Sonnet for well-specified building, Haiku only for bulk read-only checks, Opus for the design system, decisions and review.

| # | Task | Model, effort | Files it owns | Starts after | Done when |
|---|---|---|---|---|---|
| **P0** | **Direction boards.** Render directions A, B and C as real WinUI mock-ups (Peek, Rail, Board, one Settings page; light and dark) through `--snapshot`, and show them to the owner. | Opus | `Views/DesignLab/*` (thrown away afterwards) | Owner approves this plan | Owner picks a direction |
| **T1** | **Design system.** `Tokens.xaml` (Light, Dark, HighContrast), type and control styles, motion helper (respects "Animation effects"), and base components: `Token` (logo pad plus notch count), `SettingsCard`/`SettingsExpander` (or the Toolkit's), `StatusLine`, empty and error states. A `DESIGN.md` recording every decision. | **Opus** | `Styles/*`, `Ui/Components/*`, `windows/DESIGN.md` | P0 | Component gallery screens render in all three themes. Opus review passes. |
| **T2** | **Platform and Core additions.** Windows attention state (`SHQueryUserNotificationState`), the global shortcut (`RegisterHotKey`, conflict check), new `AppConfig` fields with tests, the `DesktopBuddy.Presentation` project skeleton. **Spike:** does `AppNotificationManager` work unpackaged? Report yes or no with evidence; build nothing. | Sonnet, high | `Platform/Attention.cs`, `Platform/Hotkey.cs`, `Core/Models.cs` (new fields only), `src/DesktopBuddy.Presentation/*` (skeleton) | Plan approved (runs alongside P0 and T1) | Unit tests for the new fields pass. Spike report delivered. |
| **T3** | **Board.** View model and XAML. Replaces `ListWindow` and the tray flyout list. Anchors to the tray or the Rail, groups by owner, keyboard triage, Undo, the InfoBar for problems, empty state. | Sonnet, high | `Views/Board/*`, `Presentation/Board*` | T1, T2 | View-model tests pass. Snapshots in three themes reviewed by Opus. Keyboard path works end to end. |
| **T4** | **Rail and Peek.** Tokens on the edge, hover and focus names, meeting urgency, the "settle" signature moment. Peek with Open, Done and Later, the time bar, attention-aware. The tray icon dot and right-click menu. | Sonnet, high | `Views/Rail/*`, `Views/Peek/*`, `App.Overlays.cs`, `Presentation/Rail*`, `Presentation/Peek*` | T1, T2 | As for T3, plus a live check on screen at 100% and 150% scaling |
| **T5** | **Settings rebuild.** The six sections from §5, BreadcrumbBar drill-in, grouped service card, InfoBars, dialogs. All existing flows kept: sign-in, wrong account, chat picker, label picker, team setup, report a bug, tour. | Sonnet, high | `Views/Settings/*` (replaced), `App.Settings.cs`, `Presentation/Settings*` | T1, T2 | Every page renders in three themes. Every old flow still reachable. Opus review passes. |
| **T6** | **Strings and accessibility sweep.** Move remaining strings to `.resw`, AutomationIds and names everywhere, focus visuals, tab order. | Sonnet, medium | Touches T3–T5 files, so runs only after they finish | T3, T4, T5 | Automated AutomationId audit (T7) passes. |
| **T7** | **Testing** (§7.1) | Sonnet, high; **Opus reviews the results** | `tests/*`, `windows/ui-tests/*` | Each layer as its feature lands; full run after T6 | All gates in §7.1 green |
| **T8** | **Final review and docs.** `winui-code-review` checklist over the whole app, fixes, updated screenshots, `windows/README.md` and SETUP.md §13. | **Opus** | Docs, small fixes | T7 | Owner sign-off |

T3, T4 and T5 run **in parallel**: they own different files and depend only on T1's components and T2's view-model skeleton.

**Hand-over rules (so no two agents edit the same file):**
- **Board API (T3):** T3 exposes one entry point, `Board.Show(anchor: Tray | Rail(owner), filter)`, and never edits `App.Overlays.cs` or `App.xaml.cs`.
- **Old windows (T4):** T4 calls the Board API from the Rail, and deletes the old `ListWindow`, `ListContent`, `TrayFlyout` and `TrayContent` once the Board lands.
- **Tray behaviour (Opus, in T1):** the tray icon's left-click, right-click menu and dot live in `App.xaml.cs`, which Opus owns. Opus changes them in T1 and adds a hook (`partial void ShowBoard(...)`) that T3's file implements.
- **Shared project file:** `DesktopBuddy.Presentation.csproj` is created by T2. T3, T4 and T5 only add their own files to it.

### 7.1 Testing task (T7)

| Layer | What | How | Gate |
|---|---|---|---|
| Unit | The existing 335 tests, plus view-model tests: Board grouping, filtering, keyboard commands, Undo; Peek queueing and attention rules; Settings page logic; new config fields load from old files | xUnit (`dotnet test windows/DesktopBuddy.slnx`) | 100% pass, no flaky runs over 3 repeats |
| Platform | `DesktopBuddy.exe --selftest`, extended: hotkey register and unregister, attention-state read | `build-windows.ps1` | All pass |
| Visual regression | Every screen in **Light, Dark and High Contrast**, at **100% and 150%** scaling, with long names and empty and error states (`break-ui` worst-case data) | `--snapshot`, then **Opus reviews every PNG** against a checklist: clipping, contrast, alignment, token use, no colour-only meaning | Opus sign-off |
| UI automation | `windows/ui-tests/ui-tests.ps1` using Microsoft's `winapp ui` harness: first run lands on sign-in; tray click opens the Board; Board keyboard triage (arrows, E, S, Ctrl+Z); Rail token opens the filtered Board; Peek Done and Later; Settings navigation and drill-in; remove-account dialog; AutomationId audit with no empty IDs. Screenshots captured and viewed. | `winapp ui` locally, and in Windows Sandbox if enabled. **Needs the winapp tool** ([§8](#8-decisions-for-the-owner), decision 4). | `test-results.json` all passed; screenshots reviewed |
| Accessibility | Keyboard-only pass of every flow, Narrator spot-check (names and order), High Contrast, text scaling 150% | Manual script run by the agent, results recorded | No dead ends, everything named |
| Code review | Microsoft's `winui-code-review` checklist: x:Bind, view models, theme resources only, no `.Result`/`.Wait()`, virtualised lists, security | Opus with the checklist | No errors open |
| Release smoke | `build-windows.ps1 -Share`, then first-run launch of the **published** build with a throwaway data folder, window found and captured | Script | App opens, Settings and Board render |

---

## 8. Decisions for the owner

1. **Direction:** A (Lily pad, recommended), B (Ledger) or C (Beacon). The final pick happens after seeing the P0 mock-ups.
2. **Third-party packages:** the house rule is "no third-party libraries unless the owner agrees". Allow Microsoft's Community Toolkit packages (`SettingsControls` for the settings rows, `CommunityToolkit.Mvvm` for view models)? Recommended: **yes**. They're .NET Foundation projects that Microsoft's own skills recommend, and they save us building less accessible copies. If not, T1 builds both pieces itself.
3. **Global shortcut default:** `Ctrl+Alt+N`, or something else.
4. **Install Microsoft's `winapp` tool** (`winget install Microsoft.WinAppCli`). It's needed for the UI-automation tests and for Microsoft's sample search. My attempt to install it was blocked by the safety check, so you need to run it or allow it.
5. **Windows notifications option:** worth exploring (T2 spike), or keep Desktop Buddy's own Peek only.

---

## 9. Order of work

```
Owner approves plan + decisions 2–5
        │
        ├── P0 Direction boards (Opus) ──► Owner picks direction
        │                                        │
        └── T2 Platform + Core (Sonnet) ─────────┤
                                                 ▼
                                 T1 Design system (Opus)
                                                 │
                      ┌──────────────────────────┼──────────────────────────┐
                      ▼                          ▼                          ▼
                 T3 Board (Sonnet)     T4 Rail + Peek (Sonnet)     T5 Settings (Sonnet)
                      └──────────────────────────┼──────────────────────────┘
                                                 ▼
                                  T6 Strings + accessibility (Sonnet)
                                                 ▼
                                  T7 Testing (Sonnet, Opus reviews)
                                                 ▼
                                  T8 Final review + docs (Opus) ──► Owner sign-off
```
