# Desktop Buddy for Windows: design system ("Lily pad")

The owner chose direction **A, Lily pad** on 14 November 2026 from the boards in `docs/directions/`. This file records every design decision. Build only from it. If something you need isn't here, ask the lead; don't invent a new style.

Plan and task list: `docs/UI-UX-PLAN.md`. Living reference: `DesktopBuddy.exe --snapshot <folder> --only components` renders every component in light and dark.

---

## 1. The idea

Calm paper and ink, with one living detail. The colours come from the Dynamatix tree logo:
- ink brown for text;
- lily-pad teal for the accent;
- coral for "waiting".

The **client token** (§4) is the signature, and it looks the same on every surface. Everything else stays quiet (Microsoft's guidance for an app like this, "Operate mode": "Brand lives in precise details").

**Never:**
- pure white or pure black surfaces;
- default red badges;
- a card inside a card;
- grey 1 px borders on everything;
- yellow or other colour-only error text;
- emoji used as icons;
- tracked-capitals eyebrow labels;
- spinners as timers;
- slide-in or slide-out motion.

## 2. Tokens (`Styles/Tokens.xaml`)

Use them only through `{ThemeResource Db…}` in XAML, or `Ui.Tokens.Brush(element, key)` in code. Never write colour literals in views. Light, Dark and HighContrast are all defined.

| Purpose | Key | Use |
|---|---|---|
| Surfaces | `DbSurfaceBaseBrush` | Panels (Board, Rail, Peek in dark), page backgrounds |
| | `DbSurfaceRaisedBrush` | Cards and settings rows on a base surface |
| | `DbSurfaceSunkenBrush` | Settings sidebar, keycaps, wells |
| | `DbSurfaceInverseBrush` | The Peek in light mode (ink card) |
| | `DbSurfaceSelectedBrush` / `DbSurfaceHoverBrush` | Selected and hovered rows |
| Text | `DbTextPrimaryBrush` / `Secondary` / `Tertiary` | All text. Tertiary only for non-essential meta. |
| | `DbTextOnAccentBrush`, `DbTextOnInverseBrush`, `DbTextOnInverseSecondaryBrush`, `DbTextOnWaitingBrush` | Text on those fills |
| Signals | `DbAccentBrush` | Selection, the one primary action, links |
| | `DbWaitingBrush` | Waiting counts (the notch), the big number on Home |
| | `DbUrgentBrush` | Meeting countdown ring, "starts in" time |
| | `DbPositiveBrush`, `DbCautionBrush`, `DbCriticalBrush` (+ `…SubtleBrush` washes) | Status |
| Lines | `DbStrokeSubtleBrush`, `DbStrokeStrongBrush`, `DbStrokeOnInverseBrush` | Panel edges, outlined buttons |
| Pad | `DbPadBrush` | The white pad behind a logo |
| Service glyphs | `DbSourceMailBrush`, `DbSourceChatBrush`, `DbSourceMeetingBrush`, `DbSourceTaskBrush`, `DbSourceReminderBrush` | The mail, chat, meeting, task and reminder icons on rows and the Peek. Use these, **not** `ItemSource.Tint()` (the Mac's bright colours fail contrast on paper). At least 3.8:1 on base and selected rows. |

All text pairs pass WCAG 4.5:1. The calculated values are in the header of `Tokens.xaml`. The app accent (`SystemAccentColor*`) is overridden to lily teal, so built-in switches and buttons match.

**Radius,** one per nesting level and concentric: `DbRadiusPanel` 14 (Board, Rail, Peek), `DbRadiusCard` 10, `DbRadiusRow` 8, `DbRadiusControl` 6, `DbRadiusPill` 16 (buttons). Tokens are full circles.

**Space:** 4 px grid (`DbSpace1`…`DbSpace12` = 4, 8, 12, 16, 24, 32, 48). Panel padding is 16–18. Row padding is 18 horizontal, 7–8 vertical.

## 3. Type (`Styles/Type.xaml`)

Segoe UI Variable (the system font). The Display axis is only for titles and the Peek sender. Never set a raw `FontSize` in a view; use these styles.

| Style | Use |
|---|---|
| `DbPageTitleStyle` | Settings page titles ("Home") |
| `DbPanelTitleStyle` | "Waiting" on the Board, the sender on the Peek |
| `DbSectionStyle` | Group headings in a page ("Needs attention") |
| `DbRowTitleStyle` | A row's first line (sender, setting name), trims |
| `DbSecondaryStyle` | A row's second line (subject, description), trims |
| `DbCaptionStyle` | Meta: times, counts, "Gatehouse · Gmail" |
| `DbFigureStyle` | The big waiting number on Home |
| `DbBodyStyle` | Running text |

**Counts and times use tabular numerals:** `Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular)`.

**Copy:** sentence case, plain English, short. Errors say what to do. Buttons are verbs.

## 4. Components (`Ui/Components/`)

| Component | What it is | Rules |
|---|---|---|
| **`TokenView`** (the signature) | Logo on a round white pad, a soft ring in the client's colour, the waiting count in a **notch** cut into the ring, an optional meeting countdown ring (`Urgency` 0…1) | Sizes: 24 in lists, 40 on the Rail, 44 on the Peek, 36 on Home. Below 30 px the notch is a dot; show the number as text beside it. Set `CutBrush` to the surface it sits on if that isn't `DbSurfaceBaseBrush` (on the light Peek, use `DbSurfaceInverseBrush`). Reminders: `IsReminders = true`. |
| **`StatusLine`** | Glyph, title, optional detail and an optional verb button | Severity has its own glyph (never colour-only). Set `AutomationKey` for tests. Use it for the Board's problem line and Home's "Needs attention". |
| **`Keycap`** | A key for shortcut hints | Decorative; the label beside it carries the meaning |
| Buttons | `DbPrimaryButtonStyle` (one per surface), `DbSecondaryButtonStyle`, `DbQuietButtonStyle`, `DbIconButtonStyle` | Icon buttons always get `AutomationProperties.Name` and a tooltip |
| Surfaces | `DbPanelStyle` (Border), `DbCardStyle` (Border) | No card inside a card. In lists, rows don't get their own boxes. |
| Settings rows | Community Toolkit `SettingsCard` and `SettingsExpander` (`CommunityToolkit.WinUI.Controls.SettingsControls`, owner-approved) | Retheme them with our tokens: override `SettingsCardBackground`, `SettingsCardBorderBrush` (and their PointerOver, Pressed and Disabled variants) to `DbSurfaceRaisedBrush` and `DbStrokeSubtleBrush` in the page or app resources. Check the real key names in the package's `SettingsCard.xaml` before relying on them. Give the content at least 680 px of width, or the controls wrap under the header. |
| `LogoTile` (`Ui/LogoTile.cs`) | Older square tile | Don't use it in new screens; `TokenView` replaces it. |

## 5. Motion (`Ui/Motion.cs`)

Only opacity and scale move, never position.
- **Durations:** 83 / 167 / 250 ms.
- **Easing:** enter decelerates, exit accelerates.
- **Animation effects off:** `Motion.Enabled` is false and every change is instant.

| Moment | Call |
|---|---|
| A new token joins the Rail, or the Peek appears (the signature "settle") | `Motion.Settle(element)` (fade plus 96%→100%, 250 ms) |
| The Board opens | `Motion.FadeIn(element)` |
| Anything closes | `Motion.FadeOut(element, done)` |

**Never:** bouncing, looping idle motion, or animating layout properties.

## 6. Surfaces (see `docs/UI-UX-PLAN.md` §3)

| Surface | Shape | Material |
|---|---|---|
| **Peek** (top centre) | 400 wide, `DbRadiusPanel`, token 44, sender in `DbPanelTitleStyle`. **Open** (primary), **Done**, **Later** (secondary pills). A 2 px time line along the bottom edge (accent). "N more waiting" at the right. | Light: inverse ink card. Dark: paper card (`DbSurfaceInverseBrush` in each theme, which flips). |
| **Rail** (screen edge) | A floating pill, 60 wide, tokens 40, 12 apart. Names show in a tooltip on hover and focus. Meeting tokens carry the urgency ring. | `DbSurfaceBaseBrush`, soft shadow |
| **Board** (above the tray, or beside the Rail) | 404 × up to 560, `DbRadiusPanel`. Header "Waiting" with a coral count pill, Clear all, Settings. One `StatusLine` when something's broken. Groups: token 24, name, count. Rows: source glyph in the service colour, sender (`DbRowTitleStyle`), subject (`DbSecondaryStyle`), time on the right. Selected row: `DbSurfaceSelectedBrush` plus a 3 px accent bar in the row padding. Keycap hints along the bottom. | `DbSurfaceBaseBrush`, soft shadow, `ThemeShadow` 32 when live |
| **Settings** | `NavigationView` Left with six sections. Content on `DbSurfaceBaseBrush`, sidebar on `DbSurfaceSunkenBrush`. Home hero card with the figure and tokens. | Mica window |

**Elevation:** panels get a soft two-layer shadow in snapshots and `ThemeShadow` (32) live. Cards have no shadow.

## 7. Accessibility

- **AutomationIds:** every interactive control gets an `AutomationProperties.AutomationId` (stable; tests use it). Every icon-only button gets an `AutomationProperties.Name`.
- **Keyboard:** everything is reachable by keyboard. Focus is always visible (use the system focus visual). Esc closes panels.
- **High Contrast:** the tokens map to `SystemColor*` brushes. Check every screen in a Contrast theme.
- **Text scaling:** layouts must survive 150% text scale. Nothing has a fixed text height.
- **Never colour-only:** status always has a glyph and words.
