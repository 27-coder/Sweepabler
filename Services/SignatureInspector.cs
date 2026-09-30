using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ProperAppUpdater.Services;

/// <summary>
/// Result of a full Authenticode trust check on an installer.
/// <see cref="IsTrusted"/> means Windows validated the signature end to end:
/// the embedded hash matches the file (not tampered) AND the certificate chains
/// to a trusted root. This is NOT the same as merely reading the signer name.
/// </summary>
public readonly record struct SignatureResult(bool IsTrusted, bool HasSignature, string SignerName, string StatusText);

public static class SignatureInspector
{
    // WINTRUST_ACTION_GENERIC_VERIFY_V2
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_WHOLECHAIN = 1;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT = 0x00000080;

    private const uint TRUST_E_NOSIGNATURE = 0x800B0100;
    private const uint TRUST_E_BAD_DIGEST = 0x80096010;
    private const uint TRUST_E_SUBJECT_NOT_TRUSTED = 0x800B0004;
    private const uint CERT_E_UNTRUSTEDROOT = 0x800B0109;
    private const uint CERT_E_CHAINING = 0x800B010A;
    private const uint CERT_E_EXPIRED = 0x800B0101;
    private const uint CERT_E_REVOKED = 0x800B010C;
    private const uint CRYPT_E_SECURITY_SETTINGS = 0x80092026;

    /// <summary>
    /// Validates the Authenticode signature of <paramref name="installerPath"/> using
    /// the OS trust provider (WinVerifyTrust), then reads the signer name for display.
    /// </summary>
    public static SignatureResult Verify(string installerPath)
    {
        int result;
        try
        {
            result = RunWinVerifyTrust(installerPath);
        }
        catch (Exception)
        {
            // If the trust provider itself cannot be invoked, treat as not trusted
            // rather than throwing — the caller decides what to do with an unverified file.
            return new SignatureResult(false, false, TryGetSignerName(installerPath), "trust check unavailable");
        }

        var code = unchecked((uint)result);
        var hasSignature = code != TRUST_E_NOSIGNATURE;
        var trusted = result == 0;
        var signer = hasSignature ? TryGetSignerName(installerPath) : string.Empty;
        return new SignatureResult(trusted, hasSignature, signer, DescribeStatus(code));
    }

    private static int RunWinVerifyTrust(string installerPath)
    {
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = installerPath,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero
        };

        var pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        var pData = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, false);

            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = pFile,
                dwStateAction = WTD_STATEACTION_VERIFY,
                dwProvFlags = WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT
            };
            Marshal.StructureToPtr(data, pData, false);

            var result = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, pData);

            // Always release the provider state handle WinVerifyTrust allocated.
            var afterVerify = Marshal.PtrToStructure<WINTRUST_DATA>(pData);
            afterVerify.dwStateAction = WTD_STATEACTION_CLOSE;
            Marshal.StructureToPtr(afterVerify, pData, false);
            WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, pData);

            return result;
        }
        finally
        {
            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(pFile);
            Marshal.FreeHGlobal(pFile);
            Marshal.FreeHGlobal(pData);
        }
    }

    /// <summary>Reads the signer subject common name, if present. Does not validate the signature.</summary>
    public static string TryGetSignerName(string installerPath)
    {
        try
        {
#pragma warning disable SYSLIB0057 // No X509CertificateLoader API extracts an Authenticode cert from a signed PE file.
            using var certificate = X509Certificate.CreateFromSignedFile(installerPath);
#pragma warning restore SYSLIB0057
            using var certificate2 = new X509Certificate2(certificate);
            return certificate2.GetNameInfo(X509NameType.SimpleName, false);
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string DescribeStatus(uint code)
    {
        return code switch
        {
            0 => "valid signature",
            TRUST_E_NOSIGNATURE => "not signed",
            TRUST_E_BAD_DIGEST => "file tampered after signing (bad digest)",
            TRUST_E_SUBJECT_NOT_TRUSTED => "signature not trusted",
            CERT_E_UNTRUSTEDROOT => "untrusted root certificate",
            CERT_E_CHAINING => "broken certificate chain",
            CERT_E_EXPIRED => "expired certificate",
            CERT_E_REVOKED => "revoked certificate",
            CRYPT_E_SECURITY_SETTINGS => "blocked by local security policy",
            _ => $"unverified signature (0x{code:X8})"
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, IntPtr pWVTData);
}
