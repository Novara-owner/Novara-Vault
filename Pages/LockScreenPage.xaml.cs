/* ========== LockScreenPage - Privacy Lock ==========
Function: Lock screen - password entry (long password, 4.7), Enter/arrow submit, wrong-password flash, 30-min lockout countdown, forget-password flow, deactivation clear (G13)
Corresponding UI: LockScreenPage.xaml.cs
Logic Range: Whole file business logic of this module
*/
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;
using Windows.UI;
using Windows.Security.Credentials.UI;
using Novara.Services;

namespace Novara.Pages;

public sealed partial class LockScreenPage : Page
{

    private const int MaxFailCount = 5;

    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(30);

    private static readonly SolidColorBrush ErrorBrush = new(Color.FromArgb(0xFF, 0xFF, 0x45, 0x45));
    private static readonly Thickness StateBorderThickness = new(2);

    private SolidColorBrush _normalBorderBrush = null!;
    private bool _verifying;
    private int _failCount;
    private DispatcherTimer? _lockTimer;

    public event Action? UnlockSucceeded;

    public event Action? CorruptDetected;
    public event Action? IoErrorDetected; // E3-12: transient read failure - MainWindow shows a retry-only dialog (NO rebuild offer)

    public LockScreenPage()
    {
        InitializeComponent();
        Novara.Services.DialogDepth.AttachContainer((Grid)Content); // dialog depth: shadow + chrome veil auto-wiring
        // ND4: ForgotOverlay is nested inside UnlockView (two levels deep) - the container scan
        // cannot reach it, so pair it explicitly. Chrome drive is pointless here: LockScreenFrame
        // covers the whole window anyway.
        Novara.Services.DialogDepth.AttachPair(ForgotScrim, ForgotDialog, driveChrome: false);
        KeyDown += Page_KeyDown;
        Unloaded += (_, _) => { _lockTimer?.Stop(); _lockTimer = null; _autoUnlockCts?.Cancel(); WindowsHelloBreathing.Stop(); }; // #43: timer Unloaded guard (prevent leak/update hidden UI)
        Loaded += (_, _) =>
        {
            _normalBorderBrush = App.GetBrush("AppBorderBrush");
            _failCount = PasswordService.GetFailCount(); // Bug 15: restore persisted fail count

            // 5.0: Windows Hello text is visible only when enabled (credential exists).
            // NH7: breathing itself starts in OnLockEntryCompleted - an early Begin here gets
            // suppressed by LockEntryAnimation (same target property, HoldEnd, starts later).
            WindowsHelloText.Visibility = WindowsHelloService.IsEnabled() ? Visibility.Visible : Visibility.Collapsed;

            if (PasswordService.IsLockoutEnabled() && PasswordService.IsLockedOut(out _))
            {
                EnterLockedState();
                return;
            }
            LockEntryAnimation.Completed -= OnLockEntryCompleted;
            LockEntryAnimation.Completed += OnLockEntryCompleted;
            LockEntryAnimation.Begin();
            PasswordInput.Focus(FocusState.Programmatic);

            // 5.0: auto-start one Windows Hello attempt when enabled (failure keeps the lock screen so the icon can retry)
            if (WindowsHelloService.IsEnabled())
                _ = AutoUnlockWithWindowsHelloAsync();
        };
    }

    private void PasswordInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { e.Handled = true; _ = VerifyAsync(); }
    }

    // ---- Auto-unlock: as soon as the entered password matches, unlock without Enter/arrow. ----
    // A long password has no fixed length, so "done typing" cannot be known - probe on every
    // change instead: debounce, verify PBKDF2 on the thread pool (UI stays responsive), unlock on
    // success, stay silent on failure (the user may still be typing; Enter/arrow still submit
    // explicitly and report errors). Failures here do NOT count toward the 5-strike lockout.
    // E1-07 (2026-08-10): brute-force hardening - probes are capped per program run (~64, resets on
    // restart) and the debounce is 400ms, so automated guessing is slow and bounded while normal
    // typing (probe count = keystrokes after the 6th char) stays far below the cap.
    private const int AutoProbeLimit = 64;
    private static readonly TimeSpan AutoProbeDebounce = TimeSpan.FromMilliseconds(400);
    private CancellationTokenSource? _autoUnlockCts;
    private int _autoUnlockSeq;
    private int _autoProbeCount;

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_verifying) return;
        if (PasswordInput.Password.Length < 6) return; // below the min length - nothing to check yet
        if (_autoProbeCount >= AutoProbeLimit) return; // E1-07: probe budget exhausted for this run (Enter/arrow still work)
        _autoProbeCount++;
        _autoUnlockCts?.Cancel();
        _autoUnlockCts = new CancellationTokenSource();
        var seq = ++_autoUnlockSeq;
        var pw = PasswordInput.Password;
        _ = CheckAutoUnlockAsync(pw, seq, _autoUnlockCts.Token);
    }

    private async System.Threading.Tasks.Task CheckAutoUnlockAsync(string pw, int seq, System.Threading.CancellationToken ct)
    {
        try { await System.Threading.Tasks.Task.Delay(AutoProbeDebounce, ct); } catch { return; } // debounced / superseded
        if (seq != _autoUnlockSeq || _verifying) return;
        if (PasswordInput.Password != pw) return; // the user kept typing - a newer probe is queued
        var result = await System.Threading.Tasks.Task.Run(() => App.Store?.LoadWithPassword(pw));
        if (seq != _autoUnlockSeq || _verifying) return; // E1-06: superseded / a submit already won - drop this stale probe
        if (result?.Status == LoadStatus.Ok)
        {
            _verifying = true; // same success path as VerifyAsync (auto-unlock)
            _failCount = 0;
            PasswordService.SetFailCount(0);
            App.ApplyStoredSettings();
            PlayExitAnimation();
            return;
        }
        // E5-14: only a wrong password is silent by design (the user may not have finished typing);
        // corruption / IO failures must surface exactly like the manual VerifyAsync path.
        if (result?.Status == LoadStatus.Corrupted) { CorruptDetected?.Invoke(); return; }
        if (result?.Status == LoadStatus.IoError) { IoErrorDetected?.Invoke(); return; }
        // wrong password: silent by design - the user may not have finished typing
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e) => _ = VerifyAsync();

    private bool _unlockStarted; // E1-06: one-shot guard for the exit animation / UnlockSucceeded

    private async System.Threading.Tasks.Task VerifyAsync()
    {
        if (_verifying) return; // E1-06: re-entry guard (double Enter / Enter + arrow)
        _verifying = true;
        FocusSink.Focus(FocusState.Programmatic);
        await UnlockWithPasswordAsync(PasswordInput.Password);
    }

    /* ========== Windows Hello Unlock ==========
    Function: pop native Hello prompt -> read PasswordVault -> unlock via the shared path.
    Corresponding UI: LockScreenPage.xaml (WindowsHelloText)
    */
    private async void WindowsHelloText_Tapped(object sender, TappedRoutedEventArgs e)
    {
        await TryUnlockWithWindowsHelloAsync();
    }

    /* ========== Windows Hello Unlock (shared) ========== */
    private async System.Threading.Tasks.Task TryUnlockWithWindowsHelloAsync()
    {
        if (_verifying) return;
        _verifying = true;
        FocusSink.Focus(FocusState.Programmatic);
        try
        {
            var result = await WindowsHelloService.RequestVerificationAsync(App.GetString("Lock_WinHello_VerifyMsg"));
            if (result == UserConsentVerificationResult.DeviceNotPresent)
            {
                // NH14: Hello vanished mid-session (PIN/biometrics removed) - retire the dead entrance.
                WindowsHelloText.Visibility = Visibility.Collapsed;
            }
            if (result != UserConsentVerificationResult.Verified)
            {
                // Failed (non-cancel) reuses the wrong-password shake; a manual cancel stays silent
                // (the native Hello prompt already reports its own error).
                if (result != UserConsentVerificationResult.Canceled)
                    await ShakeAndFlashAsync();
                _verifying = false;
                return;
            }

            var pw = WindowsHelloService.TryGetPassword();
            if (string.IsNullOrEmpty(pw))
            {
                await ShakeAndFlashAsync(); // credential missing (anomalous) -> shake
                _verifying = false;
                return;
            }

            await UnlockWithPasswordAsync(pw, fromHello: true);
        }
        catch { _verifying = false; }
    }

    private async System.Threading.Tasks.Task AutoUnlockWithWindowsHelloAsync()
    {
        try { await System.Threading.Tasks.Task.Delay(1000); } catch { return; } // wait for the entrance animation to settle
        // NH14: availability gate - no configured PIN/biometrics means no entrance and no native prompt.
        if (!await WindowsHelloService.IsAvailableAsync())
        {
            WindowsHelloText.Visibility = Visibility.Collapsed;
            return;
        }
        if (_verifying) return;
        if (LockedView.Visibility == Visibility.Visible) return; // locked out - never auto-attempt
        if (!string.IsNullOrEmpty(PasswordInput.Password)) return; // the user is typing a password - do not interrupt
        await TryUnlockWithWindowsHelloAsync();
    }

    private async System.Threading.Tasks.Task UnlockWithPasswordAsync(string pw, bool fromHello = false)
    {
        var result = App.Store?.LoadWithPassword(pw);
        if (result != null && result.Status == LoadStatus.Ok)
        {
            _failCount = 0;
            PasswordService.SetFailCount(0);
            App.ApplyStoredSettings();
            PlayExitAnimation();
            return;
        }

        if (result != null && result.Status == LoadStatus.Corrupted)
        {
            _verifying = false; // Bug 20: reset flag so input works after the corrupt dialog closes
            CorruptDetected?.Invoke();
            return;
        }
        if (result != null && result.Status == LoadStatus.IoError)
        {
            _verifying = false; // E3-12: transient IO failure - retry dialog, never the rebuild offer
            IoErrorDetected?.Invoke();
            return;
        }

        // NH15: the Hello gesture already passed biometric verification - a stale vault credential
        // is an anomaly, not a wrong-password strike. Retire the credential and never count it
        // toward the 5-strike lockout; the password channel takes over cleanly.
        if (fromHello)
        {
            WindowsHelloService.Disable();
            WindowsHelloText.Visibility = Visibility.Collapsed;
            await ShakeAndFlashAsync();
            _verifying = false;
            return;
        }

        _failCount++;
        PasswordService.SetFailCount(_failCount);
        if (PasswordService.IsLockoutEnabled() && _failCount >= MaxFailCount)
        {
            PasswordService.SetLockoutUntil(DateTime.Now + LockDuration);
            EnterLockedState();
            return;
        }

        await ShakeAndFlashAsync();
        ClearInput();
        _verifying = false;
    }

    // ================================================================

    // ================================================================

    private void OnLockEntryCompleted(object? sender, object e)
    {
        ForgotHintBreathing.Begin();
        // NH7: start the Hello breathing only AFTER the entrance animation finished - starting it
        // in Loaded let LockEntryAnimation (same target property, HoldEnd) suppress it forever.
        WindowsHelloBreathing.Begin();
    }

    /* ========== LockScreen Locked State ==========
Function: 5-fail lockout: red countdown breath animation, timer until lockout expiry, auto return to entry, forget-password flow
Corresponding UI: LockScreenPage.xaml.cs
Logic Range: Below methods in this region
*/
private void EnterLockedState()
    {
        _verifying = false;
        LockEntryAnimation.Stop();
        ForgotHintBreathing.Stop();
        UnlockView.Visibility = Visibility.Collapsed;
        LockedView.Visibility = Visibility.Visible;
        UpdateLockCountdown();
        LockedViewEntry.Begin();
        CountdownBreathing.Begin();
        _lockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _lockTimer.Tick += (_, _) => UpdateLockCountdown();
        _lockTimer.Start();
    }

    private void UpdateLockCountdown()
    {
        var remain = PasswordService.GetRemainingLockout(); 
        if (remain <= TimeSpan.Zero)
        {
            ExitLockedState();
            return;
        }
        LockCountdownText.Text = remain.ToString(@"hh\:mm\:ss");
    }

    private void ExitLockedState()
    {
        _lockTimer?.Stop();
        _lockTimer = null;
        CountdownBreathing.Stop();
        LockedView.Visibility = Visibility.Collapsed;
        UnlockView.Visibility = Visibility.Visible;
        _failCount = 0;
        PasswordService.ClearLockout(); 
        PasswordService.SetFailCount(0);
        ClearInput();
        // Fix: replay entrance animation to restore input/forgot-password visibility - LockEntryAnimation plays once on the Loaded non-locked branch;
        // when starting inside the lock period it never played, so PasswordInputPanel/ForgotPasswordText Opacity stays at XAML initial 0,
        // after countdown only the title remains visible (input & forgot-password missing)
        LockEntryAnimation.Stop();
        LockEntryAnimation.Completed -= OnLockEntryCompleted;
        LockEntryAnimation.Completed += OnLockEntryCompleted;
        LockEntryAnimation.Begin();
    }

    private async System.Threading.Tasks.Task ShakeAndFlashAsync()
    {
        var t = PasswordInputPanelTransform;
        PasswordInput.BorderBrush = ErrorBrush;
        PasswordInput.BorderThickness = StateBorderThickness;

        int[] offsets = { -8, 8, -6, 6, -4, 4, 0 };
        foreach (var off in offsets)
        {
            t.X = off;
            await System.Threading.Tasks.Task.Delay(50);
        }

        for (int i = 0; i < 2; i++)
        {
            PasswordInput.BorderBrush = _normalBorderBrush;
            await System.Threading.Tasks.Task.Delay(120);
            PasswordInput.BorderBrush = ErrorBrush;
            await System.Threading.Tasks.Task.Delay(120);
        }

        PasswordInput.BorderBrush = _normalBorderBrush;
        PasswordInput.BorderThickness = new Thickness(1);
        t.X = 0;
    }

    private void PlayExitAnimation()
    {
        if (_unlockStarted) return; // E1-06: one-shot - a second success path must not replay the exit
        _unlockStarted = true;
        ForgotHintBreathing.Stop();
        WindowsHelloBreathing.Stop(); // 5.0: stop the breathing text before it moves out
        var sb = new Storyboard();

        var tx = new DoubleAnimation { To = -240, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        Storyboard.SetTarget(tx, LockTitleTransform); Storyboard.SetTargetProperty(tx, "X"); sb.Children.Add(tx);
        var to = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(500) };
        Storyboard.SetTarget(to, LockTitle); Storyboard.SetTargetProperty(to, "Opacity"); sb.Children.Add(to);

        var bx = new DoubleAnimation { To = 240, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        Storyboard.SetTarget(bx, PasswordInputPanelTransform); Storyboard.SetTargetProperty(bx, "X"); sb.Children.Add(bx);
        var bo = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(500) };
        Storyboard.SetTarget(bo, PasswordInputPanel); Storyboard.SetTargetProperty(bo, "Opacity"); sb.Children.Add(bo);

        var fy = new DoubleAnimation { To = 60, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        Storyboard.SetTarget(fy, ForgotTransform); Storyboard.SetTargetProperty(fy, "Y"); sb.Children.Add(fy);
        var fo = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(500) };
        Storyboard.SetTarget(fo, ForgotPasswordText); Storyboard.SetTargetProperty(fo, "Opacity"); sb.Children.Add(fo);

        // 5.0: the Windows Hello text moves out left (same direction as the title)
        var wx = new DoubleAnimation { To = -240, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        Storyboard.SetTarget(wx, WindowsHelloTextTransform); Storyboard.SetTargetProperty(wx, "X"); sb.Children.Add(wx);
        var wo = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(500) };
        Storyboard.SetTarget(wo, WindowsHelloText); Storyboard.SetTargetProperty(wo, "Opacity"); sb.Children.Add(wo);

        sb.Completed += (_, _) => UnlockSucceeded?.Invoke();
        sb.Begin();
    }

    public void ClearInput() // D1 (Round 5): clear password on window deactivation (rule G13)
    {
        PasswordInput.Password = string.Empty;
        if (LockedView.Visibility == Visibility.Visible) return; 
        PasswordInput.Focus(FocusState.Programmatic);
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        if (ForgotOverlay.Visibility == Visibility.Visible) { HideForgotDialog(); e.Handled = true; }
    }

    // ================================================================

    // ================================================================
    private void ForgotPassword_Tapped(object sender, TappedRoutedEventArgs e)
    {
        ShowForgotDialog();
    }

    private bool _forgotAnimating;   // N4T-06: hide-animation re-entry guard
    private bool _forgotConfirming;  // N4T-06: confirm double-click guard - double-click ran ResetDatabase twice + UnlockSucceeded twice

    private void ShowForgotDialog()
    {
        if (_forgotAnimating) return; // N4T-06: a running hide-fade would otherwise yank the freshly shown dialog back down
        _forgotConfirming = false; // N4T-06: fresh dialog session
        ForgotDialogTransform.ScaleX = 0.92;
        ForgotDialogTransform.ScaleY = 0.92;
        ForgotDialogTransform.TranslateY = 20;
        ForgotDialog.Opacity = 0;
        ForgotScrim.Opacity = 0;
        ForgotOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ForgotScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ForgotDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ForgotDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Begin();
    }

    private void HideForgotDialog()
    {
        if (_forgotAnimating) return; // N4T-06: M2 - a second hide during the fade re-ran Completed early
        _forgotAnimating = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ForgotScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ForgotDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ForgotDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Completed += (_, _) => { ForgotOverlay.Visibility = Visibility.Collapsed; _forgotAnimating = false; }; // N4T-06: re-arm only after fully hidden
        sb.Begin();
    }

    private void ForgotClose_Click(object sender, RoutedEventArgs e) => HideForgotDialog();
    private void ForgotCancel_Click(object sender, RoutedEventArgs e) => HideForgotDialog();

    private void ForgotScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, ForgotScrim)) HideForgotDialog();
    }

    private void ForgotConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_forgotConfirming) return; // N5T-06: double-click ran the wipe twice and UnlockSucceeded twice (double entry animation)
        if (_verifying) return; // N5T2-07: an auto-probe LoadWithPassword is in flight - its Ok reply must not double-unlock after the wipe
        _forgotConfirming = true;
        _verifying = true;
        _autoUnlockSeq++; // N5T2-07: invalidate any probe already past its pre-await check
        try
        {
            // N4S-03: align with SettingsPage.PerformReset (E4-07) - reset FIRST and only delete the
            // password files when the empty db actually landed on disk. The old order (delete first,
            // ignore ResetDatabase result) left the old encrypted db on disk with no password file when
            // the reset failed: next startup rejects every password with no recovery path.
            var resetResult = App.Store?.ResetDatabase();
            if (resetResult == null || resetResult.Status != LoadStatus.EmptyCreated)
            {
                HideForgotDialog();
                _ = ShakeAndFlashAsync(); // reuse the wrong-password feedback - the wipe did not happen, stay locked
                return;
            }
            PasswordService.Delete();
            PasswordService.DeleteLockout();
            WindowsHelloService.Disable(); // 5.0: forgot-password wipes the DB - drop the Hello credential
            Services.StickySync.ClearAllNotes(); // E3-07 parity with PerformReset - desktop notes of the wiped db must not ghost around
            HideForgotDialog();
            UnlockSucceeded?.Invoke();
        }
        finally { _verifying = false; } // N5T2-07: always release, success or stay-locked failure
    }
}
