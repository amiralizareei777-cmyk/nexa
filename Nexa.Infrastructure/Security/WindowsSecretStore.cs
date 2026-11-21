using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace Nexa.Infrastructure.Security;

/// <summary>
/// Stores secrets securely using Windows Credential Manager.
/// </summary>
public class WindowsSecretStore : ISecretStore
{
    private const string TargetPrefix = "Nexa/";

    public Task StoreSecretAsync(string key, string value)
    {
        var credential = new CREDENTIAL
        {
            Type = CRED_TYPE.GENERIC,
            TargetName = Marshal.StringToCoTaskMemUni(TargetPrefix + key),
            CredentialBlob = Marshal.StringToCoTaskMemUni(value),
            CredentialBlobSize = (uint)Encoding.Unicode.GetByteCount(value),
            Persist = CRED_PERSIST.LOCAL_MACHINE,
            AttributeCount = 0,
            UserName = Marshal.StringToCoTaskMemUni(Environment.UserName)
        };

        try
        {
            bool result = CredWrite(ref credential, 0);
            if (!result)
            {
                throw new InvalidOperationException($"Failed to store secret. Error code: {Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            if (credential.TargetName != IntPtr.Zero) Marshal.FreeCoTaskMem(credential.TargetName);
            if (credential.CredentialBlob != IntPtr.Zero) Marshal.ZeroFreeCoTaskMemUnicode(credential.CredentialBlob);
            if (credential.UserName != IntPtr.Zero) Marshal.FreeCoTaskMem(credential.UserName);
        }

        return Task.CompletedTask;
    }

    public Task<string?> GetSecretAsync(string key)
    {
        IntPtr credPtr = IntPtr.Zero;
        try
        {
            bool result = CredRead(TargetPrefix + key, CRED_TYPE.GENERIC, 0, out credPtr);
            if (!result)
            {
                return Task.FromResult<string?>(null);
            }

            var credential = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            string secret = Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2) ?? "";
            return Task.FromResult<string?>(secret);
        }
        finally
        {
            if (credPtr != IntPtr.Zero)
            {
                CredFree(credPtr);
            }
        }
    }

    public Task DeleteSecretAsync(string key)
    {
        CredDelete(TargetPrefix + key, CRED_TYPE.GENERIC, 0);
        return Task.CompletedTask;
    }

    public async Task<bool> SecretExistsAsync(string key)
    {
        var secret = await GetSecretAsync(key);
        return !string.IsNullOrEmpty(secret);
    }

    #region Native Interop

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, CRED_TYPE type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] UInt32 flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(string target, CRED_TYPE type, int flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree([In] IntPtr cred);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public UInt32 Flags;
        public CRED_TYPE Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public UInt32 CredentialBlobSize;
        public IntPtr CredentialBlob;
        public CRED_PERSIST Persist;
        public UInt32 AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    private enum CRED_TYPE : uint
    {
        GENERIC = 1
    }

    private enum CRED_PERSIST : uint
    {
        SESSION = 1,
        LOCAL_MACHINE = 2,
        ENTERPRISE = 3
    }

    #endregion
}
