# Accessibility

English · [简体中文](ACCESSIBILITY.zh-CN.md)

Novara is built on WinUI 3, which provides the UI Automation (UIA) layer, keyboard navigation and system theme integration out of the box. This page records what the app does for accessibility and what conventions the code follows.

## Keyboard access

- Every surface is reachable by keyboard; Tab and arrow keys move focus, Esc closes dialogs and menus, Enter confirms.
- Global shortcuts: `Ctrl+K` global search, `Ctrl+T` tools, `Ctrl+,` settings, `Ctrl+Shift+Backspace` recycle bin, `Ctrl+Shift+N` quick capture. Shortcuts are suppressed while the lock screen, the welcome page or a text input holds focus, so they never fire in the wrong context.
- Dialogs follow a consistent structure (title row, content, bottom action row) with the primary action as a real button, so screen readers announce the focused action directly.

## Screen-reader support

- All controls are standard WinUI controls and expose their UI Automation peers automatically.
- **Every icon-only button carries an `AutomationProperties.Name`** (145 buttons at the time of writing, verified by a repository guard). The names are localized: they resolve through the same five-language dictionary as the visible UI, so a screen reader announces 「关闭」/ "Close" / 「閉じる」 in the user's own language.
- Text-bearing buttons expose their label through their content and need no extra property.

## Visual accessibility

- Focus visuals are the WinUI default system focus rectangles — always drawn, never suppressed.
- Light and dark themes follow the system setting; the brand and danger accent colours are defined per theme with contrast in mind, and paper-style themes soften the accents while keeping text contrast.
- Motion respects the app's animation switch (Settings → 显示模式), so users sensitive to movement can turn the decorative animation system off.

## Conventions for new code

- An icon-only button must carry `AutomationProperties.Name` resolved from the string dictionary (`{x:Bind app:App.GetString('...')}`); a guard in the repository's static-check suite fails the build if an icon-only button ships unnamed.
- New interactive elements must remain reachable by keyboard — no pointer-only gestures for a primary action.
- New colours must land as named theme resources (contrast-reviewed per theme), not inline literals.

If you hit an accessibility barrier that this page claims is covered, please open an issue (or use the private channel in [SECURITY.md](../SECURITY.md) for anything security-related) — the fastest way to get it fixed is a precise description of where focus or announcement goes wrong.

---

*Last updated: 2026-10-13*
