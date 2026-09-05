// DecryptEfsFek.cs
//
// Decrypts the "Encrypted FEK" blob from an NTFS $EFS (EFSRPC Metadata / DDF)
// attribute using a certificate + private key exported as a .pfx, per the
// on-disk layout in MS-EFSR section 2.2.2.1.5.
//
// Requires .NET 6+ (uses System.Security.Cryptography.X509Certificates).
// Run with:  dotnet run  (after `dotnet new console` and dropping this in)
// or compile directly:  csc DecryptEfsFek.cs
//
// USAGE:
//   1. Set PfxPath / PfxPassword below (or pass as args).
//   2. Set EncryptedFekHex to the 256-byte (or whatever length) Encrypted FEK
//      blob bytes extracted from the DDF entry (Offset to Encrypted FEK /
//      Encrypted FEK Length fields in the Key List Entry).
//   3. Run. It will:
//        a. Load the cert+private key from the pfx.
//        b. Print the cert's SHA-1 thumbprint (compare against the DDF's
//           stored thumbprint to confirm you have the right cert).
//        c. RSA-decrypt the blob (PKCS#1 v1.5 padding, which is what
//           classic EFS uses).
//        d. Parse the resulting structure: KeyLength / Entropy / Algorithm /
//           Reserved / Key, per MS-EFSR 2.2.2.1.5.
//        e. Print the algorithm name and the raw FEK bytes (hex).
//
// SECURITY NOTE: this prints your file's raw symmetric key to the console.
// Treat that output exactly as sensitively as the private key itself.

using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

class DecryptEfsFek
{
    // ---- CONFIGURE THESE ----
    const string PfxPath = @"C:\Users\xxluk\Desktop\cert.pfx";
    const string PfxPassword = "1234";

    // The "Encrypted FEK" bytes from the DDF Key List Entry
    // (Offset to Encrypted FEK .. + Encrypted FEK Length), as hex.
    // Replace this with your actual blob.
    const string EncryptedFekHex =
        "B914D1B8EAEB3AC18F8E2D8C2483A9CBE001961EA2B7D27B2A4FE4B4A1ABF96" +
        "51A120E982DF6CE9F87FD8B396E7B85DD5E093834EA7AFD8C0B2D132FED9FDD" +
        "DB07851E538009E92224E93894FCB436E7B19580A59918306F9C4699B2F0AE" +
        "0E7BE238F72CEBF59B9AA4BAE65D656AE400DB661E89371792B1534D62D3B1" +
        "D5EDFBD815831B19E9AD3D9C3CC5C243F6D8AE6B11E1631285E30A37689019" +
        "9FFB3127920E6F5D8FCDA1E9DA31C00173B35C2795B2BE867DC39AA62D48A3" +
        "3E0D524DF16D5AD7EF14EC4C234869E0CE816078EA28B4770A026F96CA63CC" +
        "0AE1210F77D0709209573CF1DD0820E79E5F6F5E6C0A73D3502DDA3D9BD9E5" +
        "733ED2F0332F8F";

    // Expected SHA-1 thumbprint from the DDF entry, for sanity-checking
    // that you're using the right certificate. Leave blank to skip the check.
    const string ExpectedThumbprint = "1D0D4CDDB2553A08FE1DB0D24683984E5C64EF8E";

    // Algorithm IDs as defined in MS-EFSR 2.2.13 / wincrypt.h ALG_ID
    static string AlgName(uint algId) => algId switch
    {
        0x6601 => "DES",
        0x6603 => "3DES",
        0x6604 => "DESX",
        0x6609 => "3DES_112",
        0x660e => "AES-128",
        0x660f => "AES-192",
        0x6610 => "AES-256",
        _ => $"Unknown (0x{algId:X8})"
    };

    public static void Test(string[] args)
    {
        // 1. Load certificate + private key from the exported pfx.
        // NOTE: deliberately NOT using EphemeralKeySet here. That flag is
        // reliably supported only for CNG-backed keys; EFS certs (especially
        // older ones, or anything tied to "Microsoft Enhanced Cryptographic
        // Provider v1.0" / other legacy CAPI providers) are often CAPI-backed,
        // and EphemeralKeySet + CAPI is a classic source of
        // "Invalid parameter" CryptographicExceptions on Windows.
        var cert = new X509Certificate2(
            PfxPath,
            PfxPassword,
            X509KeyStorageFlags.Exportable);

        string actualThumbprint = cert.Thumbprint; // already hex, upper-case
        Console.WriteLine($"Loaded cert: {cert.Subject}");
        Console.WriteLine($"Certificate SHA-1 thumbprint: {actualThumbprint}");

        if (!string.IsNullOrEmpty(ExpectedThumbprint))
        {
            bool match = string.Equals(actualThumbprint, ExpectedThumbprint,
                StringComparison.OrdinalIgnoreCase);
            Console.WriteLine(match
                ? "Thumbprint MATCHES the DDF entry — this is the right cert."
                : "Thumbprint DOES NOT MATCH the DDF entry — wrong cert for this DDF.");
            if (!match)
            {
                Console.WriteLine("Aborting: decrypting with a mismatched key will just produce garbage.");
                return;
            }
        }

        using RSA? rsa = cert.GetRSAPrivateKey();
        if (rsa == null)
        {
            Console.WriteLine("This certificate has no accessible RSA private key.");
            Console.WriteLine("Make sure you exported the .pfx WITH the private key, and marked it exportable.");
            return;
        }

        // Self-test: does this private key actually correspond to this
        // certificate's own public key? A matching thumbprint only proves
        // it's the same certificate -- it says nothing about whether the
        // bundled private key is the one that actually pairs with it.
        Console.WriteLine("Running key-pair self-test (encrypt with cert's public key, decrypt with private key)...");
        try
        {
            using RSA pub = cert.GetRSAPublicKey()!;
            byte[] probe = System.Text.Encoding.ASCII.GetBytes("EFS key pair self-test 12345");
            byte[] probeCt = pub.Encrypt(probe, RSAEncryptionPadding.Pkcs1);
            byte[] probePt = rsa.Decrypt(probeCt, RSAEncryptionPadding.Pkcs1);
            bool ok = probePt.AsSpan().SequenceEqual(probe);
            Console.WriteLine(ok
                ? "Self-test PASSED: private key correctly pairs with this certificate's public key."
                : "Self-test FAILED: round-trip produced different bytes than expected.");
            if (!ok)
            {
                Console.WriteLine("This private key does NOT actually correspond to this certificate's public key,");
                Console.WriteLine("despite the thumbprint matching. The .pfx is internally inconsistent -- re-export");
                Console.WriteLine("is unlikely to help unless it comes from the original source where the key was created.");
                return;
            }
        }
        catch (CryptographicException ex0)
        {
            Console.WriteLine("Self-test itself threw an exception (before even touching the DDF data): " +
                               ex0.Message + $" (HResult 0x{ex0.HResult:X8})");
            Console.WriteLine("This points to the key pair being broken/mismatched at a fundamental level,");
            Console.WriteLine("or the private key being unusable for decrypt operations at all.");
            return;
        }
        Console.WriteLine();

        byte[] encryptedFek = Convert.FromHexString(EncryptedFekHex);

        Console.WriteLine($"RSA key size    : {rsa.KeySize} bits");
        Console.WriteLine($"Ciphertext length: {encryptedFek.Length} bytes " +
                           $"(implies a {encryptedFek.Length * 8}-bit modulus)");
        if (rsa.KeySize != encryptedFek.Length * 8)
        {
            Console.WriteLine("*** MISMATCH: this private key's size does not match the ciphertext length. ***");
            Console.WriteLine("*** This is very likely why decryption is failing — wrong/reissued key. ***");
        }
        Console.WriteLine();

        byte[] decrypted;

        // 2a. Try the normal managed API path first.
        try
        {
            decrypted = rsa.Decrypt(encryptedFek, RSAEncryptionPadding.Pkcs1);
        }
        catch (CryptographicException ex1)
        {
            Console.WriteLine("RSA.Decrypt via CNG/NCrypt failed: " + ex1.Message +
                               $" (HResult 0x{ex1.HResult:X8})");
            Console.WriteLine("Falling back to manual RSA decryption (raw ModPow), " +
                               "bypassing the CNG provider entirely...");
            Console.WriteLine();
            decrypted = ManualRsaPkcs1Decrypt(rsa, encryptedFek);
        }

        // 3. Parse the decrypted "Encrypted FEK" structure per MS-EFSR 2.2.2.1.5:
        //    KeyLength (4) | Entropy (4) | Algorithm (4) | Reserved (4) | Key (KeyLength bytes)
        if (decrypted.Length < 16)
        {
            Console.WriteLine("Decrypted blob too short to be a valid FEK structure — " +
                               "wrong key, wrong padding, or wrong input bytes.");
            Console.WriteLine("Raw decrypted bytes: " + Convert.ToHexString(decrypted));
            return;
        }

        uint keyLength = BitConverter.ToUInt32(decrypted, 0);
        uint entropy = BitConverter.ToUInt32(decrypted, 4);
        uint algorithm = BitConverter.ToUInt32(decrypted, 8);
        // bytes 12..15 = Reserved, ignored

        Console.WriteLine();
        Console.WriteLine("---- Decrypted FEK structure ----");
        Console.WriteLine($"Key Length : {keyLength} bytes");
        Console.WriteLine($"Entropy    : {entropy} bits");
        Console.WriteLine($"Algorithm  : {AlgName(algorithm)} (0x{algorithm:X8})");

        if (16 + keyLength > decrypted.Length)
        {
            Console.WriteLine("WARNING: declared KeyLength runs past the decrypted buffer. " +
                               "Padding/offset is likely wrong — treat this output with suspicion.");
            keyLength = (uint)(decrypted.Length - 16);
        }

        byte[] fek = new byte[keyLength];
        Array.Copy(decrypted, 16, fek, 0, keyLength);

        Console.WriteLine($"FEK (hex)  : {Convert.ToHexString(fek)}");
        Console.WriteLine();

        // 4. Decrypt the file's raw $DATA attribute content, if provided.
        if (!string.IsNullOrEmpty(EncryptedDataPath) && System.IO.File.Exists(EncryptedDataPath))
        {
            if (algorithm != 0x6610)
            {
                Console.WriteLine($"$DATA decryption is only implemented here for AES-256 (0x6610); " +
                                   $"this file uses {AlgName(algorithm)} -- ask if you need that variant too.");
            }
            else
            {
                Console.WriteLine($"Decrypting $DATA from: {EncryptedDataPath}");
                byte[] encData = System.IO.File.ReadAllBytes(EncryptedDataPath);
                byte[] plain = DecryptEfsDataAes256(fek, encData);
                System.IO.File.WriteAllBytes(DecryptedOutputPath, plain);
                Console.WriteLine($"Wrote {plain.Length} decrypted bytes to: {DecryptedOutputPath}");
                Console.WriteLine("If the real file size isn't a multiple of 512, trim trailing bytes");
                Console.WriteLine("using the file's actual logical size (from $STANDARD_INFORMATION / directory listing).");
            }
        }
        else
        {
            Console.WriteLine("(Set EncryptedDataPath to a raw dump of the file's $DATA attribute bytes");
            Console.WriteLine(" to also decrypt the file content itself.)");
        }

        Console.WriteLine();
        Console.WriteLine("This FEK + Algorithm is what you feed into decrypting the file's");
        Console.WriteLine("$DATA attribute (CBC mode, per-EFS-version key/IV derivation rules ");
        Console.WriteLine("in MS-EFSR 2.2.3 / 2.2.3.3 — ask if you want that step scripted too).");
    }

    // ---- Set this to a raw binary dump of the file's $DATA attribute bytes ----
    // (i.e. the on-disk ciphertext, exactly as it sits in the $DATA attribute --
    // NOT the EFSRPC "raw export" wire format, which adds its own framing).
    const string EncryptedDataPath = @"C:\Users\xxluk\Desktop\encrypted";
    const string DecryptedOutputPath = @"C:\Users\xxluk\Desktop\decrypted";

    // Decrypts EFS-encrypted $DATA content that uses AES-256.
    //
    // Format (reverse-engineered, implemented identically in ntfsprogs'
    // ntfsdecrypt.c): the stream is divided into independent 512-byte
    // "encryption units". Each unit is standard AES-256-CBC using the FEK
    // as the key, with a 16-byte IV that is NOT secret -- it's derived
    // purely from the unit's index (0-based, incrementing once per 512
    // bytes) via two fixed 64-bit constants:
    //
    //   IV[0..7]  = LE64(0x5816657be9161312 + unit_index)
    //   IV[8..15] = LE64(0x1989adbe44918961 + unit_index)
    //
    // This IV exists only to stop identical plaintext in different units
    // from producing identical ciphertext (CBC chaining resets every 512
    // bytes) -- all security comes from the FEK, the constants are public.
    static byte[] DecryptEfsDataAes256(byte[] fek, byte[] encryptedData)
    {
        if (fek.Length != 32)
            throw new ArgumentException($"Expected a 32-byte AES-256 FEK, got {fek.Length} bytes.");
        if (encryptedData.Length % 512 != 0)
            Console.WriteLine($"WARNING: encrypted data length ({encryptedData.Length}) isn't a multiple of " +
                               "512 -- the trailing partial unit will be skipped.");

        int unitCount = encryptedData.Length / 512;
        byte[] output = new byte[unitCount * 512];

        using Aes aes = Aes.Create();
        aes.Key = fek;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None; // EFS units are always full 16-byte-aligned blocks

        for (int unit = 0; unit < unitCount; unit++)
        {
            const ulong c1 = 0x5816657be9161312UL;
            const ulong c2 = 0x1989adbe44918961UL;
            ulong lo = unchecked(c1 + (ulong)unit);
            ulong hi = unchecked(c2 + (ulong)unit);

            byte[] iv = new byte[16];
            BitConverter.GetBytes(lo).CopyTo(iv, 0); // little-endian, matches BitConverter default on x86/x64
            BitConverter.GetBytes(hi).CopyTo(iv, 8);

            aes.IV = iv;
            using ICryptoTransform dec = aes.CreateDecryptor();
            byte[] unitCipher = new byte[512];
            Array.Copy(encryptedData, unit * 512, unitCipher, 0, 512);
            byte[] unitPlain = dec.TransformFinalBlock(unitCipher, 0, 512);
            Array.Copy(unitPlain, 0, output, unit * 512, 512);
        }

        return output;
    }

    // Performs textbook RSA decryption (c^d mod n) directly via BigInteger,
    // then manually strips PKCS#1 v1.5 Type 2 padding. This entirely bypasses
    // CNG/NCryptDecrypt (and its provider-specific "Invalid parameter"
    // rejections) -- it only needs the raw key material, which is available
    // as long as the key came from an exportable .pfx.
    //
    // Tries both byte orders for the ciphertext: standard big-endian (the
    // convention BigInteger/CNG/OpenSSL/PKCS#1 all use), and reversed
    // (little-endian), because legacy Windows CryptoAPI (CALG_RSA_KEYX,
    // used by "...Cryptographic Provider" CSPs) stores RSA key blobs and
    // CryptEncrypt output byte-reversed relative to the standard convention.
    // $EFS data written via that legacy path inherits that byte order.
    static byte[] ManualRsaPkcs1Decrypt(RSA rsa, byte[] ciphertext)
    {
        RSAParameters p;
        try
        {
            p = rsa.ExportParameters(includePrivateParameters: true);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "Could not export raw private key parameters -- the key is marked " +
                "non-exportable at the CNG level (e.g. smartcard/HSM-backed, or the " +
                ".pfx was imported without the Exportable flag). Manual decryption " +
                "requires the raw modulus/exponent, so this path can't proceed. " +
                "Original error: " + ex.Message);
        }

        if (p.D == null || p.Modulus == null)
            throw new InvalidOperationException("Exported key parameters are missing D or Modulus.");

        var n = new BigInteger(p.Modulus, isUnsigned: true, isBigEndian: true);
        var d = new BigInteger(p.D, isUnsigned: true, isBigEndian: true);
        int keySizeBytes = p.Modulus.Length;

        // Attempt 1: standard big-endian ciphertext (the "normal" convention).
        var attempt1 = TryUnpad(ModExp(ciphertext, isBigEndian: true, n, d, keySizeBytes));
        if (attempt1 != null)
        {
            Console.WriteLine("Decrypted successfully treating ciphertext as BIG-ENDIAN (standard).");
            return attempt1;
        }

        // Attempt 2: byte-reversed ciphertext (legacy CryptoAPI convention).
        var attempt2 = TryUnpad(ModExp(ciphertext, isBigEndian: false, n, d, keySizeBytes));
        if (attempt2 != null)
        {
            Console.WriteLine("Decrypted successfully treating ciphertext as LITTLE-ENDIAN " +
                               "(legacy CryptoAPI byte order) -- standard big-endian interpretation failed.");
            return attempt2;
        }

        throw new InvalidOperationException(
            "Decrypted output does not have valid PKCS#1 v1.5 padding in EITHER byte order " +
            "-- this almost always means the wrong private key was used, or the ciphertext " +
            "bytes don't exactly match the DDF's Encrypted FEK field.");
    }

    static byte[] ModExp(byte[] ciphertext, bool isBigEndian, BigInteger n, BigInteger d, int keySizeBytes)
    {
        var c = new BigInteger(ciphertext, isUnsigned: true, isBigEndian: isBigEndian);
        BigInteger m = BigInteger.ModPow(c, d, n);
        byte[] mBytes = m.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (mBytes.Length < keySizeBytes)
        {
            byte[] padded = new byte[keySizeBytes];
            Array.Copy(mBytes, 0, padded, keySizeBytes - mBytes.Length, mBytes.Length);
            mBytes = padded;
        }
        return mBytes;
    }

    // Returns the unpadded message if mBytes is valid PKCS#1 v1.5 Type 2
    // padding (0x00 0x02 PS 0x00 M), or null if not.
    static byte[]? TryUnpad(byte[] mBytes)
    {
        if (mBytes.Length < 11 || mBytes[0] != 0x00 || mBytes[1] != 0x02)
            return null;

        int sepIndex = Array.IndexOf(mBytes, (byte)0x00, 2);
        if (sepIndex < 0)
            return null;

        int msgLen = mBytes.Length - sepIndex - 1;
        byte[] message = new byte[msgLen];
        Array.Copy(mBytes, sepIndex + 1, message, 0, msgLen);
        return message;
    }
}
