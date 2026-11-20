using System.Security.Cryptography;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealSessionTests
{
    private static byte[] Key(byte seed = 0x5A)
    {
        var key = new byte[SealFormat.KeySize];
        Array.Fill(key, seed);
        return key;
    }

    [Fact]
    public void Open_KeepsItsOwnCopyOfTheKey()
    {
        var caller = Key();
        var session = SealSession.Open(caller);


        CryptographicOperations.ZeroMemory(caller);

        Assert.True(session.IsUnlocked);
        Assert.NotSame(caller, session.Vmk);
        Assert.All(session.Vmk!, b => Assert.Equal(0x5A, b));
    }

    [Fact]
    public void Dispose_ZeroesTheBufferAndDropsTheReference()
    {
        var session = SealSession.Open(Key());
        var held = session.Vmk!;

        session.Dispose();

        Assert.False(session.IsUnlocked);
        Assert.Null(session.Vmk);
        Assert.All(held, b => Assert.Equal(0, b));
    }


    [Fact]
    public void Dispose_IsIdempotent()
    {
        var session = SealSession.Open(Key());

        session.Dispose();
        session.Dispose();
        session.Dispose();

        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public void Open_RejectsAWrongSizedKey()
    {
        Assert.Throws<ArgumentException>(() => SealSession.Open(new byte[SealFormat.KeySize - 1]));
        Assert.Throws<ArgumentException>(() => SealSession.Open(new byte[SealFormat.KeySize + 1]));
        Assert.Throws<ArgumentNullException>(() => SealSession.Open(null!));
    }

    [Fact]
    public void Mode_IsRecorded()
    {
        using var normal = SealSession.Open(Key());
        using var direct = SealSession.Open(Key(), SealSessionMode.Direct);

        Assert.Equal(SealSessionMode.Normal, normal.Mode);
        Assert.Equal(SealSessionMode.Direct, direct.Mode);
    }
}
