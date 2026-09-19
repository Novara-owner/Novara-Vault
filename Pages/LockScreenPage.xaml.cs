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
    public event Action? IoErrorDetected;

    public LockScreenPage()
    {
        InitializeComponent();


        if (App.CurrentLanguage == "en-US")
        {
            LockTitle.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Assets/Fonts/Comfortaa.ttf#Comfortaa");
            LockTitle.CharacterSpacing = 0;
        }
        Novara.Services.DialogDepth.AttachContainer((Grid)Content);



        Novara.Services.DialogDepth.AttachPair(ForgotScrim, ForgotDialog, driveChrome: false);
        KeyDown += Page_KeyDown;
        Unloaded += (_, _) =>
        {
            _lockTimer?.Stop(); _lockTimer = null; _autoUnlockCts?.Cancel();


            WindowsHelloBreathing.Stop();
            ForgotHintBreathing.Stop();
            CountdownBreathing.Stop();
        };
        Loaded += (_, _) =>
        {
            _normalBorderBrush = App.GetBrush("AppBorderBrush");
            _failCount = PasswordService.GetFailCount();




            WindowsHelloText.Visibility = WindowsHelloService.IsEnabled() ? Visibility.Visible : Visibility.Collapsed;

            if (PasswordService.IsLockoutEnabled() && PasswordService.IsLockedOut(out _))
            {
                EnterLockedState();
                return;
            }


            _failCount = PasswordService.GetFailCount();
            LockEntryAnimation.Completed -= OnLockEntryCompleted;
            LockEntryAnimation.Completed += OnLockEntryCompleted;
            LockEntryAnimation.Begin();





            DispatcherQueue.TryEnqueue(FocusPasswordInput);


            if (WindowsHelloService.IsEnabled())
            {


                _autoUnlockCts?.Cancel();
                _autoUnlockCts = new CancellationTokenSource();
                _ = AutoUnlockWithWindowsHelloAsync(_autoUnlockCts.Token);
            }
        };
    }

    private void PasswordInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { e.Handled = true; _ = VerifyAsync(); }
    }









    private const int AutoProbeLimit = 64;
    private static readonly TimeSpan AutoProbeDebounce = TimeSpan.FromMilliseconds(400);
    private CancellationTokenSource? _autoUnlockCts;
    private int _autoUnlockSeq;
    private static int _autoProbeCount;

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_verifying) return;
        if (PasswordInput.Password.Length < 6) return;
        if (_autoProbeCount >= AutoProbeLimit) return;
        _autoProbeCount++;
        _autoUnlockCts?.Cancel();
        _autoUnlockCts = new CancellationTokenSource();
        var seq = ++_autoUnlockSeq;

        var pw = PasswordInput.Password;
        _ = CheckAutoUnlockAsync(pw, seq, _autoUnlockCts.Token);
    }

    private async System.Threading.Tasks.Task CheckAutoUnlockAsync(string pw, int seq, System.Threading.CancellationToken ct)
    {
        try { await System.Threading.Tasks.Task.Delay(AutoProbeDebounce, ct); } catch { return; }
        if (seq != _autoUnlockSeq || _verifying) return;
        if (PasswordInput.Password != pw) return;
        var result = await System.Threading.Tasks.Task.Run(() => App.Store?.LoadWithPassword(pw));



        if (seq != _autoUnlockSeq || _verifying || PasswordInput.Password != pw) return;
        if (result?.Status == LoadStatus.Ok)
        {
            _verifying = true;
            _failCount = 0;
            PasswordService.SetFailCount(0);
            App.ApplyStoredSettings();
            PlayExitAnimation();
            return;
        }


        if (result?.Status == LoadStatus.Corrupted) { CorruptDetected?.Invoke(); return; }
        if (result?.Status == LoadStatus.IoError) { IoErrorDetected?.Invoke(); return; }

    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e) => _ = VerifyAsync();

    private bool _unlockStarted;

    private async System.Threading.Tasks.Task VerifyAsync()
    {
        if (_verifying) return;
        _verifying = true;
        FocusSink.Focus(FocusState.Programmatic);
        await UnlockWithPasswordAsync(PasswordInput.Password);
    }





    private async void WindowsHelloText_Tapped(object sender, TappedRoutedEventArgs e)
    {
        await TryUnlockWithWindowsHelloAsync();
    }


    private async System.Threading.Tasks.Task TryUnlockWithWindowsHelloAsync(System.Threading.CancellationToken ct = default)
    {
        if (_verifying) return;
        _verifying = true;
        FocusSink.Focus(FocusState.Programmatic);
        try
        {
            ct.ThrowIfCancellationRequested();
            var result = await WindowsHelloService.RequestVerificationAsync(App.GetString("Lock_WinHello_VerifyMsg"));
            if (result == UserConsentVerificationResult.DeviceNotPresent)
            {

                WindowsHelloText.Visibility = Visibility.Collapsed;
            }
            if (result != UserConsentVerificationResult.Verified)
            {


                if (result != UserConsentVerificationResult.Canceled)
                    await ShakeAndFlashAsync();
                _verifying = false;
                return;
            }

            var pw = WindowsHelloService.TryGetPassword();
            if (string.IsNullOrEmpty(pw))
            {
                await ShakeAndFlashAsync();
                _verifying = false;
                return;
            }

            await UnlockWithPasswordAsync(pw, fromHello: true);
        }
        catch { _verifying = false; }
    }

    private async System.Threading.Tasks.Task AutoUnlockWithWindowsHelloAsync(System.Threading.CancellationToken ct)
    {
        try { await System.Threading.Tasks.Task.Delay(1000, ct); } catch (System.OperationCanceledException) { return; }
        if (ct.IsCancellationRequested) return;

        if (!await WindowsHelloService.IsAvailableAsync())
        {
            WindowsHelloText.Visibility = Visibility.Collapsed;
            return;
        }
        if (_verifying) return;
        if (LockedView.Visibility == Visibility.Visible) return;
        if (!string.IsNullOrEmpty(PasswordInput.Password)) return;
        await TryUnlockWithWindowsHelloAsync(ct);
    }

    private async System.Threading.Tasks.Task UnlockWithPasswordAsync(string pw, bool fromHello = false)
    {



        var result = await System.Threading.Tasks.Task.Run(() => App.Store?.LoadWithPassword(pw));
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
            _verifying = false;
            CorruptDetected?.Invoke();
            return;
        }
        if (result != null && result.Status == LoadStatus.IoError)
        {
            _verifying = false;
            IoErrorDetected?.Invoke();
            return;
        }




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





    private void OnLockEntryCompleted(object? sender, object e)
    {
        ForgotHintBreathing.Begin();


        WindowsHelloBreathing.Begin();
    }






private void EnterLockedState()
    {
        _verifying = false;
        _autoUnlockCts?.Cancel();
        _autoUnlockSeq++;
        PasswordInput.Password = "";
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



        LockEntryAnimation.Stop();
        LockEntryAnimation.Completed -= OnLockEntryCompleted;
        LockEntryAnimation.Completed += OnLockEntryCompleted;
        LockEntryAnimation.Begin();
    }

    private bool _shakeRunning;
    private async System.Threading.Tasks.Task ShakeAndFlashAsync()
    {
        if (_shakeRunning) return;
        _shakeRunning = true;
        try
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
        finally { _shakeRunning = false; }
    }

    private void PlayExitAnimation()
    {
        if (_unlockStarted) return;
        _unlockStarted = true;
        ForgotHintBreathing.Stop();
        WindowsHelloBreathing.Stop();
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


        var wx = new DoubleAnimation { To = -240, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        Storyboard.SetTarget(wx, WindowsHelloTextTransform); Storyboard.SetTargetProperty(wx, "X"); sb.Children.Add(wx);
        var wo = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(500) };
        Storyboard.SetTarget(wo, WindowsHelloText); Storyboard.SetTargetProperty(wo, "Opacity"); sb.Children.Add(wo);

        sb.Completed += (_, _) => UnlockSucceeded?.Invoke();
        sb.Begin();
    }

    public void ClearInput()
    {
        PasswordInput.Password = string.Empty;
        if (LockedView.Visibility == Visibility.Visible) return;
        PasswordInput.Focus(FocusState.Programmatic);
    }








    public void FocusPasswordInput()
    {
        if (LockedView.Visibility == Visibility.Visible) return;
        if (ForgotOverlay.Visibility == Visibility.Visible) return;
        PasswordInput.Focus(FocusState.Programmatic);
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        if (ForgotOverlay.Visibility == Visibility.Visible) { HideForgotDialog(); e.Handled = true; }
    }




    private void ForgotPassword_Tapped(object sender, TappedRoutedEventArgs e)
    {
        ShowForgotDialog();
    }

    private bool _forgotAnimating;
    private bool _forgotConfirming;

    private void ShowForgotDialog()
    {
        if (_forgotAnimating) return;
        _forgotConfirming = false;
        ForgotDangerIcon.Data = App.CreateGeometry(IconData.Danger);
        ForgotDialogTransform.ScaleX = 0.94; ForgotDialogTransform.ScaleY = 0.94; ForgotDialogTransform.TranslateY = 24;
        ForgotDialog.Opacity = 0;
        ForgotScrim.Opacity = 0;
        ForgotOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ForgotScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ForgotDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ForgotDialogTransform);
        sb.Begin();

        ForgotConfirmButton.IsEnabled = false;
        ForgotConfirmButton.Opacity = Motion.CooldownDisabledOpacity;
        ForgotCd.Completed -= OnForgotCdCompleted;
        ForgotCd.Completed += OnForgotCdCompleted;
        ForgotCd.Start(Motion.CooldownSeconds);
    }

    private void OnForgotCdCompleted()
    {
        ForgotCd.Completed -= OnForgotCdCompleted;
        ForgotConfirmButton.IsEnabled = true;
        ForgotConfirmButton.Opacity = 1;
    }

    private void HideForgotDialog()
    {
        if (_forgotAnimating) return;
        _forgotAnimating = true;
        ForgotCd.Reset();
        ForgotConfirmButton.IsEnabled = true;
        ForgotConfirmButton.Opacity = 1;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ForgotScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ForgotDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        Motion.AddDialogHideTransform(sb, ForgotDialogTransform);
        sb.Completed += (_, _) => { ForgotOverlay.Visibility = Visibility.Collapsed; _forgotAnimating = false; };
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
        if (_forgotConfirming) return;
        if (_verifying) return;
        _forgotConfirming = true;
        _verifying = true;
        _autoUnlockSeq++;
        try
        {




            var resetResult = App.Store?.ResetDatabase();
            if (resetResult == null || resetResult.Status != LoadStatus.EmptyCreated)
            {
                HideForgotDialog();
                _ = ShakeAndFlashAsync();
                return;
            }
            PasswordService.Delete();
            PasswordService.DeleteLockout();
            WindowsHelloService.Disable();
            Services.StickySync.ClearAllNotes();
            Services.SyncService.MarkRestorePendingConfirm();
            HideForgotDialog();
            PlayExitAnimation();
        }
        finally { _verifying = false; }
    }
}
