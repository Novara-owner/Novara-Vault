using System;
using System.IO;
using Novara.Services;
using Xunit;

namespace Novara.Tests;



public sealed class PasswordLockoutTests : IDisposable
{
    private readonly string _dir;

    public PasswordLockoutTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "novara-lockout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        PasswordService.SetBaseDir(_dir);
        PasswordService.DeleteLockout();
    }

    public void Dispose()
    {
        PasswordService.SetBaseDir(null);
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void FourFailures_DoNotLock_FifthLocks()
    {
        for (var i = 0; i < PasswordService.MaxFailCount - 1; i++)
            PasswordService.RegisterFailedAttempt();
        Assert.False(PasswordService.IsLockoutActive(out _));

        PasswordService.RegisterFailedAttempt();
        Assert.True(PasswordService.IsLockoutActive(out var remaining));
        Assert.True(remaining > TimeSpan.Zero);
    }

    [Fact]
    public void SuccessfulAttempt_ResetsTheCounter()
    {
        PasswordService.RegisterFailedAttempt();
        PasswordService.RegisterFailedAttempt();
        Assert.Equal(2, PasswordService.GetFailCount());

        PasswordService.RegisterSuccessfulAttempt();
        Assert.Equal(0, PasswordService.GetFailCount());

        for (var i = 0; i < PasswordService.MaxFailCount - 1; i++)
            PasswordService.RegisterFailedAttempt();
        Assert.False(PasswordService.IsLockoutActive(out _));
    }

    [Fact]
    public void DisabledLockout_DoesNotLock()
    {
        PasswordService.SetLockoutEnabled(false);
        try
        {
            for (var i = 0; i < PasswordService.MaxFailCount + 2; i++)
                PasswordService.RegisterFailedAttempt();
            Assert.False(PasswordService.IsLockoutActive(out _));
        }
        finally
        {
            PasswordService.SetLockoutEnabled(true);
        }
    }
}
