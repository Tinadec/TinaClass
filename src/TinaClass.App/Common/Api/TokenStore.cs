using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TinaClass.App.Common.Api;

/// <summary>
/// Keeps the refresh token of a "remember me" sign-in in the operating system's secret store: the Keychain on macOS, the
/// Credential Manager on Windows, the Secret Service (<c>secret-tool</c>) on Linux. Where none is available the token is
/// kept for this run only and the person signs in again next time. Never in a plain file.
/// </summary>
public class TokenStore
{
    private readonly string _service = $"{AppInfo.PackageId} ({ApiSettings.Current.Host})";
    private const string Account = "refresh-token";
    private string? _memory;

    public virtual string? Load()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                return MacKeychain.Find(_service, Account);
            }
            if (OperatingSystem.IsWindows())
            {
                return WindowsCredentials.Read(_service);
            }
            if (OperatingSystem.IsLinux() && SecretTool.Lookup(_service, Account) is { } secret)
            {
                return secret;
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or System.ComponentModel.Win32Exception)
        {
        }
        return _memory;
    }

    public virtual void Save(string token)
    {
        _memory = token;
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                MacKeychain.Save(_service, Account, token);
            }
            else if (OperatingSystem.IsWindows())
            {
                WindowsCredentials.Write(_service, token);
            }
            else if (OperatingSystem.IsLinux())
            {
                SecretTool.Store(_service, Account, token);
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or System.ComponentModel.Win32Exception)
        {
        }
    }

    public virtual void Clear()
    {
        _memory = null;
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                MacKeychain.Delete(_service, Account);
            }
            else if (OperatingSystem.IsWindows())
            {
                WindowsCredentials.Delete(_service);
            }
            else if (OperatingSystem.IsLinux())
            {
                SecretTool.Clear(_service, Account);
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or System.ComponentModel.Win32Exception)
        {
        }
    }

    /// <summary>macOS Keychain through Security.framework (generic passwords).</summary>
    private static class MacKeychain
    {
        private const string Security = "/System/Library/Frameworks/Security.framework/Security";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        [DllImport(Security)]
        private static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength,
            byte[] account, out uint passwordLength, out IntPtr password, out IntPtr item);

        [DllImport(Security)]
        private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength,
            byte[] account, uint passwordLength, byte[] password, IntPtr item);

        [DllImport(Security)]
        private static extern int SecKeychainItemModifyAttributesAndData(IntPtr item, IntPtr attributes, uint length, byte[] data);

        [DllImport(Security)]
        private static extern int SecKeychainItemDelete(IntPtr item);

        [DllImport(Security)]
        private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);

        [DllImport(CoreFoundation)]
        private static extern void CFRelease(IntPtr value);

        public static string? Find(string service, string account)
        {
            var s = Encoding.UTF8.GetBytes(service);
            var a = Encoding.UTF8.GetBytes(account);
            if (SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a, out var length, out var data, out var item) != 0)
            {
                return null;
            }
            try
            {
                return Marshal.PtrToStringUTF8(data, (int)length);
            }
            finally
            {
                SecKeychainItemFreeContent(IntPtr.Zero, data);
                CFRelease(item);
            }
        }

        public static void Save(string service, string account, string token)
        {
            var s = Encoding.UTF8.GetBytes(service);
            var a = Encoding.UTF8.GetBytes(account);
            var p = Encoding.UTF8.GetBytes(token);
            if (SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a, out _, out var data, out var item) == 0)
            {
                SecKeychainItemFreeContent(IntPtr.Zero, data);
                SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)p.Length, p);
                CFRelease(item);
                return;
            }
            SecKeychainAddGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a, (uint)p.Length, p, IntPtr.Zero);
        }

        public static void Delete(string service, string account)
        {
            var s = Encoding.UTF8.GetBytes(service);
            var a = Encoding.UTF8.GetBytes(account);
            if (SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a, out _, out var data, out var item) == 0)
            {
                SecKeychainItemFreeContent(IntPtr.Zero, data);
                SecKeychainItemDelete(item);
                CFRelease(item);
            }
        }
    }

    /// <summary>Windows Credential Manager (generic credentials).</summary>
    private static class WindowsCredentials
    {
        private const int Generic = 1;
        private const int PersistLocalMachine = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string? Comment;
            public long LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias;
            public string? UserName;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredReadW(string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWriteW(ref Credential credential, int flags);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDeleteW(string target, int type, int flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);

        public static string? Read(string target)
        {
            if (!CredReadW(target, Generic, 0, out var pointer))
            {
                return null;
            }
            try
            {
                var credential = Marshal.PtrToStructure<Credential>(pointer);
                return Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / 2);
            }
            finally
            {
                CredFree(pointer);
            }
        }

        public static void Write(string target, string secret)
        {
            var blob = Marshal.StringToCoTaskMemUni(secret);
            try
            {
                var credential = new Credential
                {
                    Type = Generic,
                    TargetName = target,
                    CredentialBlob = blob,
                    CredentialBlobSize = secret.Length * 2,
                    Persist = PersistLocalMachine,
                    UserName = Account,
                };
                CredWriteW(ref credential, 0);
            }
            finally
            {
                Marshal.ZeroFreeCoTaskMemUnicode(blob);
            }
        }

        public static void Delete(string target) => CredDeleteW(target, Generic, 0);
    }

    /// <summary>Linux Secret Service through <c>secret-tool</c> (libsecret); the secret goes through standard input.</summary>
    private static class SecretTool
    {
        public static string? Lookup(string service, string account) => Run(null, "lookup", "service", service, "account", account);

        public static void Store(string service, string account, string secret) =>
            Run(secret, "store", "--label", service, "service", service, "account", account);

        public static void Clear(string service, string account) => Run(null, "clear", "service", service, "account", account);

        private static string? Run(string? input, params string[] arguments)
        {
            try
            {
                var start = new ProcessStartInfo("secret-tool")
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                foreach (var argument in arguments)
                {
                    start.ArgumentList.Add(argument);
                }
                using var process = Process.Start(start);
                if (process is null)
                {
                    return null;
                }
                if (input is not null)
                {
                    process.StandardInput.Write(input);
                }
                process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);
                return process.ExitCode == 0 && output.Length > 0 ? output.TrimEnd('\n') : null;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return null;
            }
        }
    }
}

/// <summary>A token store that keeps nothing beyond the process (tests, previews).</summary>
public sealed class MemoryTokenStore : TokenStore
{
    private string? _token;

    public override string? Load() => _token;

    public override void Save(string token) => _token = token;

    public override void Clear() => _token = null;
}
