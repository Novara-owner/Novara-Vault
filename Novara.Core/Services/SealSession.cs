using System.Security.Cryptography;

namespace Novara.Services;


public enum SealSessionMode
{

    Normal = 0,


    Direct = 1,
}
















public sealed class SealSession : IDisposable
{
    private byte[]? _vmk;

    private SealSession(byte[] vmk, SealSessionMode mode)
    {
        _vmk = vmk;
        Mode = mode;
    }





    public static SealSession Open(byte[] vmk, SealSessionMode mode = SealSessionMode.Normal)
    {
        ArgumentNullException.ThrowIfNull(vmk);
        if (vmk.Length != SealFormat.KeySize)
            throw new ArgumentException($"vmk must be {SealFormat.KeySize} bytes", nameof(vmk));

        return new SealSession((byte[])vmk.Clone(), mode);
    }





    internal byte[]? Vmk => _vmk;

    public bool IsUnlocked => _vmk is not null;

    public SealSessionMode Mode { get; }

    public void Dispose()
    {
        var vmk = _vmk;
        _vmk = null;

        if (vmk is not null) CryptographicOperations.ZeroMemory(vmk);
    }
}
