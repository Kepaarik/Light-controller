using System.Buffers.Binary;
using System.IO.Hashing;
using System.Security.Cryptography;

namespace LightSpace.Devices.Tuya;

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
    /// Кадры протокола 3.5 начинаются и кончаются своими метками.
    /// </summary>
    public const uint Prefix35 = 0x00006699;
    public const uint Suffix35 = 0x00009966;

    // Обмен сессионным ключом в 3.4 и 3.5 и команда управления этих версий
    public const uint CommandSessionStart = 3;
    public const uint CommandSessionResponse = 4;
    public const uint CommandSessionFinish = 5;
    public const uint CommandControlNew = 13;

    /// <summary>
    /// Размер заголовка кадра 3.5: метка, два пустых байта, номер, команда, длина.
    /// </summary>
    public const int Header35Size = 18;

    private const int GcmNonceSize = 12;
    private const int GcmTagSize = 16;
    private const int HmacSize = 32;

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

    /// <summary>
    /// AES-ECB без дополнения: блок ровно 16 байт, так считается сессионный ключ 3.4.
    /// </summary>
    public static byte[] EncryptBlock(byte[] key, byte[] block)
    {
        using var aes = Aes.Create();
        aes.Key = key;

        return aes.EncryptEcb(block, PaddingMode.None);
    }

    /// <summary>
    /// Кадр 3.4: как в 3.3, но вместо CRC32 — HMAC-SHA256 заголовка и данных ключом соединения.
    /// </summary>
    public static byte[] BuildFrame34(uint sequence, uint command, byte[] payload, byte[] hmacKey)
    {
        var frame = new byte[16 + payload.Length + HmacSize + 4];
        var span = frame.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span[0..], Prefix);
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], sequence);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], command);
        BinaryPrimitives.WriteUInt32BigEndian(span[12..], (uint)(payload.Length + HmacSize + 4));
        payload.CopyTo(span[16..]);

        HMACSHA256.HashData(hmacKey, span[..(16 + payload.Length)], span[(16 + payload.Length)..]);
        BinaryPrimitives.WriteUInt32BigEndian(span[^4..], Suffix);

        return frame;
    }

    /// <summary>
    /// Команда и данные кадра 3.4 без заголовка, HMAC и суффикса; код возврата ответа, если он есть, тоже отрезается.
    /// </summary>
    /// <exception cref="InvalidDataException">Кадр испорчен.</exception>
    public static (uint Command, byte[] Payload) ParseFrame34(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 16 + HmacSize + 4 || BinaryPrimitives.ReadUInt32BigEndian(frame) != Prefix)
            throw new InvalidDataException("Ответ Tuya — не кадр 3.4");

        var command = BinaryPrimitives.ReadUInt32BigEndian(frame[8..]);
        var data = frame[16..^(HmacSize + 4)];

        // Зашифрованные данные кратны 16 байтам; лишние 4 байта впереди — код возврата
        if (data.Length % 16 == 4)
            data = data[4..];

        return (command, data.ToArray());
    }

    /// <summary>
    /// Кадр 3.5: заголовок, затем nonce, данные, зашифрованные AES-GCM, и метка проверки; заголовок без метки
    /// защищён как дополнительные данные GCM.
    /// </summary>
    public static byte[] BuildFrame35(uint sequence, uint command, byte[] plaintext, byte[] key, byte[] nonce)
    {
        var length = GcmNonceSize + plaintext.Length + GcmTagSize;
        var frame = new byte[Header35Size + length + 4];
        var span = frame.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span[0..], Prefix35);
        BinaryPrimitives.WriteUInt32BigEndian(span[6..], sequence);
        BinaryPrimitives.WriteUInt32BigEndian(span[10..], command);
        BinaryPrimitives.WriteUInt32BigEndian(span[14..], (uint)length);

        var body = span[Header35Size..];
        nonce.AsSpan(0, GcmNonceSize).CopyTo(body);

        using var gcm = new AesGcm(key, GcmTagSize);
        gcm.Encrypt(nonce.AsSpan(0, GcmNonceSize), plaintext, body.Slice(GcmNonceSize, plaintext.Length),
            body.Slice(GcmNonceSize + plaintext.Length, GcmTagSize), span[4..Header35Size]);

        BinaryPrimitives.WriteUInt32BigEndian(span[^4..], Suffix35);

        return frame;
    }

    /// <summary>
    /// Команда и расшифрованные данные кадра 3.5.
    /// </summary>
    /// <exception cref="InvalidDataException">Кадр испорчен.</exception>
    /// <exception cref="AuthenticationTagMismatchException">Кадр зашифрован другим ключом.</exception>
    public static (uint Command, byte[] Payload) ParseFrame35(ReadOnlySpan<byte> frame, byte[] key)
    {
        if (frame.Length < Header35Size + GcmNonceSize + GcmTagSize + 4 || BinaryPrimitives.ReadUInt32BigEndian(frame) != Prefix35)
            throw new InvalidDataException("Ответ Tuya — не кадр 3.5");

        var command = BinaryPrimitives.ReadUInt32BigEndian(frame[10..]);
        var length = (int)BinaryPrimitives.ReadUInt32BigEndian(frame[14..]);

        if (length < GcmNonceSize + GcmTagSize || Header35Size + length + 4 > frame.Length)
            throw new InvalidDataException("Кадр Tuya 3.5 обрезан");

        var body = frame.Slice(Header35Size, length);
        var ciphertext = body[GcmNonceSize..^GcmTagSize];
        var plaintext = new byte[ciphertext.Length];

        using var gcm = new AesGcm(key, GcmTagSize);
        gcm.Decrypt(body[..GcmNonceSize], ciphertext, body[^GcmTagSize..], plaintext, frame[4..Header35Size]);

        return (command, plaintext);
    }

    /// <summary>
    /// Сессионный ключ 3.4 и 3.5 из случайных чисел обеих сторон: их XOR, зашифрованный локальным ключом.
    /// </summary>
    public static byte[] SessionKey(string version, byte[] localKey, byte[] localNonce, byte[] remoteNonce)
    {
        var mixed = new byte[16];

        for (var i = 0; i < mixed.Length; i++)
            mixed[i] = (byte)(localNonce[i] ^ remoteNonce[i]);

        if (version == "3.4")
            return EncryptBlock(localKey, mixed);

        // В 3.5 — AES-GCM с первыми 12 байтами нашего числа как nonce; ключ — сам шифротекст, без метки
        var key = new byte[16];
        var tag = new byte[GcmTagSize];

        using var gcm = new AesGcm(localKey, GcmTagSize);
        gcm.Encrypt(localNonce.AsSpan(0, GcmNonceSize), mixed, key, tag);

        return key;
    }
}
