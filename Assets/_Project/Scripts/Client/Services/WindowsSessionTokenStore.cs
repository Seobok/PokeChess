using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PokeChess.Client.Services
{
    public sealed class WindowsSessionTokenStore : ISessionTokenStore
    {
        const uint Generic = 1, PersistLocalMachine = 2;
        const int NotFound = 1168;
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct Credential
        {
            public uint Flags, Type;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
            [MarshalAs(UnmanagedType.LPWStr)] public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist, AttributeCount;
            public IntPtr Attributes;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetAlias;
            [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
        }
        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] static extern bool Read(string target, uint type, uint flags, out IntPtr credential);
        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] static extern bool Write(ref Credential credential, uint flags);
        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] static extern bool Delete(string target, uint type, uint flags);
        [DllImport("advapi32.dll")] static extern void CredFree(IntPtr credential);
        public Task<string> ReadAsync(string scope) => Task.Run(() => {
            if (!Read(SessionStoreKey.For(scope),Generic,0,out var pointer)) {
                int error = Marshal.GetLastWin32Error(); if (error == NotFound) return null; throw new Win32Exception(error);
            }
            try {
                var credential = Marshal.PtrToStructure<Credential>(pointer);
                if (credential.CredentialBlobSize != 64) throw new InvalidOperationException("Invalid stored session format.");
                var bytes = new byte[64];
                try { Marshal.Copy(credential.CredentialBlob,bytes,0,bytes.Length); return Encoding.ASCII.GetString(bytes); }
                finally { Array.Clear(bytes,0,bytes.Length); }
            } finally { CredFree(pointer); }
        });
        public Task WriteAsync(string scope, string token) => Task.Run(() => {
            if (!AccountValidation.Token(token)) throw new ArgumentException("Invalid session token.");
            var bytes = Encoding.ASCII.GetBytes(token); var pointer = Marshal.AllocHGlobal(bytes.Length);
            try {
                Marshal.Copy(bytes,0,pointer,bytes.Length);
                var credential = new Credential { Type=Generic, TargetName=SessionStoreKey.For(scope), CredentialBlobSize=(uint)bytes.Length,
                    CredentialBlob=pointer, Persist=PersistLocalMachine, UserName="PokeChess session" };
                if (!Write(ref credential,0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            } finally { for (int i=0;i<bytes.Length;i++) Marshal.WriteByte(pointer,i,0); Marshal.FreeHGlobal(pointer); Array.Clear(bytes,0,bytes.Length); }
        });
        public Task DeleteAsync(string scope) => Task.Run(() => {
            if (!Delete(SessionStoreKey.For(scope),Generic,0)) { int error=Marshal.GetLastWin32Error(); if (error!=NotFound) throw new Win32Exception(error); }
        });
    }
}
