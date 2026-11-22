using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PokeChess.Client.Services
{
    public sealed class AppleSessionTokenStore : ISessionTokenStore
    {
#if UNITY_IOS && !UNITY_EDITOR
        const string Security="__Internal", Core="__Internal", SystemLib="__Internal";
#else
        const string Security="/System/Library/Frameworks/Security.framework/Security";
        const string Core="/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        const string SystemLib="/usr/lib/libSystem.B.dylib";
#endif
        const int NotFound=-25300, Duplicate=-25299;
        [DllImport(SystemLib)] static extern IntPtr dlopen(string path,int mode);
        [DllImport(SystemLib)] static extern IntPtr dlsym(IntPtr handle,string name);
        [DllImport(SystemLib)] static extern int dlclose(IntPtr handle);
        [DllImport(Core)] static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator,IntPtr capacity,IntPtr keys,IntPtr values);
        [DllImport(Core)] static extern void CFDictionarySetValue(IntPtr dictionary,IntPtr key,IntPtr value);
        [DllImport(Core)] static extern IntPtr CFStringCreateWithCString(IntPtr allocator,string value,uint encoding);
        [DllImport(Core)] static extern IntPtr CFDataCreate(IntPtr allocator,byte[] bytes,IntPtr length);
        [DllImport(Core)] static extern IntPtr CFDataGetBytePtr(IntPtr data);
        [DllImport(Core)] static extern IntPtr CFDataGetLength(IntPtr data);
        [DllImport(Core)] static extern void CFRelease(IntPtr value);
        [DllImport(Security)] static extern int SecItemCopyMatching(IntPtr query,out IntPtr result);
        [DllImport(Security)] static extern int SecItemAdd(IntPtr attributes,IntPtr result);
        [DllImport(Security)] static extern int SecItemUpdate(IntPtr query,IntPtr attributes);
        [DllImport(Security)] static extern int SecItemDelete(IntPtr query);
        sealed class Query : IDisposable
        {
            readonly IntPtr security = dlopen("/System/Library/Frameworks/Security.framework/Security",1);
            readonly IntPtr core = dlopen("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation",1);
            readonly List<IntPtr> owned = new List<IntPtr>();
            public readonly IntPtr Value = CFDictionaryCreateMutable(IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
            IntPtr Constant(string name)
            {
                var address=dlsym(name.StartsWith("kCF",StringComparison.Ordinal)?core:security,name);
                if(address==IntPtr.Zero) throw new InvalidOperationException("Keychain constant unavailable.");
                return Marshal.ReadIntPtr(address);
            }
            public void Symbol(string key,string value) => CFDictionarySetValue(Value,Constant(key),Constant(value));
            public void Text(string key,string value)
            { var pointer=CFStringCreateWithCString(IntPtr.Zero,value,0x08000100); if(pointer==IntPtr.Zero) throw new InvalidOperationException(); owned.Add(pointer); CFDictionarySetValue(Value,Constant(key),pointer); }
            public void Data(byte[] bytes)
            { var pointer=CFDataCreate(IntPtr.Zero,bytes,(IntPtr)bytes.Length); owned.Add(pointer); CFDictionarySetValue(Value,Constant("kSecValueData"),pointer); }
            public Query(string scope,bool identity=true)
            {
                if (identity) {
                    Symbol("kSecClass","kSecClassGenericPassword"); Text("kSecAttrService",SessionStoreKey.For(scope)); Text("kSecAttrAccount","session");
                    Symbol("kSecAttrSynchronizable","kCFBooleanFalse");
                    Symbol("kSecUseAuthenticationUI","kSecUseAuthenticationUIFail");
                }
            }
            public void Dispose() { CFRelease(Value); foreach(var pointer in owned) CFRelease(pointer); if(security!=IntPtr.Zero)dlclose(security); if(core!=IntPtr.Zero)dlclose(core); }
        }
        static void Check(int status) { if(status!=0) throw new InvalidOperationException("Keychain operation failed ("+status+")."); }
        public Task<string> ReadAsync(string scope) => Task.Run(() => {
            using(var query=new Query(scope)) {
                query.Symbol("kSecReturnData","kCFBooleanTrue"); query.Symbol("kSecMatchLimit","kSecMatchLimitOne");
                int status=SecItemCopyMatching(query.Value,out var data); if(status==NotFound) return null; Check(status);
                try { if(CFDataGetLength(data).ToInt64()!=64) throw new InvalidOperationException("Invalid stored session format."); var bytes=new byte[64]; try { Marshal.Copy(CFDataGetBytePtr(data),bytes,0,64); return Encoding.ASCII.GetString(bytes); } finally { Array.Clear(bytes,0,bytes.Length); } }
                finally { CFRelease(data); }
            }
        });
        public Task WriteAsync(string scope,string token) => Task.Run(() => {
            if(!AccountValidation.Token(token)) throw new ArgumentException("Invalid session token.");
            var bytes=Encoding.ASCII.GetBytes(token);
            try {
                using(var query=new Query(scope)) using(var attributes=new Query(scope,false)) {
                    attributes.Data(bytes);
#if UNITY_IOS && !UNITY_EDITOR
                    attributes.Symbol("kSecAttrAccessible","kSecAttrAccessibleWhenUnlockedThisDeviceOnly");
#endif
                    int status=SecItemUpdate(query.Value,attributes.Value);
                    if(status==NotFound) {
                        query.Data(bytes);
#if UNITY_IOS && !UNITY_EDITOR
                        query.Symbol("kSecAttrAccessible","kSecAttrAccessibleWhenUnlockedThisDeviceOnly");
#endif
                        status=SecItemAdd(query.Value,IntPtr.Zero);
                        if(status==Duplicate) status=SecItemUpdate(query.Value,attributes.Value);
                    }
                    Check(status);
                }
            } finally { Array.Clear(bytes,0,bytes.Length); }
        });
        public Task DeleteAsync(string scope) => Task.Run(() => { using(var query=new Query(scope)) { int status=SecItemDelete(query.Value); if(status!=NotFound) Check(status); } });
    }
}
