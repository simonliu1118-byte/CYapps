using System.ComponentModel;
using System.Runtime.InteropServices;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class DpapiSecretProtector : ISecretProtector
{
    public string Protect(ReadOnlySpan<byte> plaintext)
    {
        if (plaintext.Length == 0) return string.Empty;
        return Convert.ToBase64String(Transform(plaintext.ToArray(), protect: true));
    }

    public byte[] Unprotect(string ciphertext)
    {
        if (ciphertext.Length == 0) return [];
        return Transform(Convert.FromBase64String(ciphertext), protect: false);
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inputPointer = Marshal.AllocHGlobal(input.Length);
        try
        {
            Marshal.Copy(input, 0, inputPointer, input.Length);
            var source = new DataBlob { Size = input.Length, Data = inputPointer };
            DataBlob output;
            var succeeded = protect
                ? CryptProtectData(ref source, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output)
                : CryptUnprotectData(ref source, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output);
            if (!succeeded) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(inputPointer); }
    }

    private const int CryptProtectUiForbidden = 0x1;
    [StructLayout(LayoutKind.Sequential)] private struct DataBlob { public int Size; public IntPtr Data; }

#pragma warning disable SYSLIB1054 // Thin DPAPI boundary retained for compatibility with Go V1.1.0 data.
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
#pragma warning restore SYSLIB1054
}
