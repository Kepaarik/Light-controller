using System.Buffers.Binary;
using System.IO.Hashing;
using System.Security.Cryptography;

namespace LedHub.Protocols.Tuya;

/// <summary>
/// Наборы DP (data points), которыми Tuya-контроллеры описывают состояние ленты.
/// </summary>
public enum TuyaDpScheme
{
    /// <summary>DP 20–24: большинство современных лент.</summary>
    V2,

    /// <summary>DP 1–5: старые контроллеры.</summary>
    V1
}

/// <summary>
/// Кадры и шифрование локального протокола Tuya.
/// </summary>
public static class TuyaProtocol
{
    public const uint Prefix = 0x000055AA;
    public const uint Suffix = 0x0000AA55;
    public const uint CommandControl = 7;

    /// <summary>
    /// Общий ключ, которым устройства шифруют широковещательные анонсы на порту 6667.
    /// </summary>
    public static readonly byte[] BroadcastKey = MD5.HashData("yGAdlopoPVldABfn"u8.ToArray());

    public static byte[] Encrypt(byte[] key, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;

        return aes.EncryptEcb(data, PaddingMode.PKCS7);
    }

    public static byte[] Decrypt(byte[] key, ReadOnlySpan<byte> data)
    {
        using var aes = Aes.Create();
        aes.Key = key;

        return aes.DecryptEcb(data, PaddingMode.PKCS7);
    }

    /// <summary>
    /// Собирает кадр: префикс, номер, команда, длина, полезная нагрузка, CRC32, суффикс.
    /// </summary>
    public static byte[] BuildFrame(uint sequence, uint command, byte[] payload)
    {
        var frame = new byte[16 + payload.Length + 8];
        var span = frame.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span[0..], Prefix);
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], sequence);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], command);
        BinaryPrimitives.WriteUInt32BigEndian(span[12..], (uint)(payload.Length + 8));
        payload.CopyTo(span[16..]);

        var crc = Crc32.HashToUInt32(span[..(16 + payload.Length)]);
        BinaryPrimitives.WriteUInt32BigEndian(span[(16 + payload.Length)..], crc);
        BinaryPrimitives.WriteUInt32BigEndian(span[(20 + payload.Length)..], Suffix);

        return frame;
    }
}
