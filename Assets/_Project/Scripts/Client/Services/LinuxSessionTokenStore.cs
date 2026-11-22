using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PokeChess.Client.Services
{
    public sealed class LinuxSessionTokenStore : ISessionTokenStore
    {
        const string Secret="libsecret-1.so.0", Glib="libglib-2.0.so.0", Gio="libgio-2.0.so.0";
        [DllImport("libdl.so.2")] static extern IntPtr dlopen(string name,int flags);
        [DllImport("libdl.so.2")] static extern IntPtr dlsym(IntPtr handle,string name);
        [DllImport("libdl.so.2")] static extern int dlclose(IntPtr handle);
        [DllImport(Glib)] static extern IntPtr g_hash_table_new(IntPtr hash,IntPtr equal);
        [DllImport(Glib)] static extern bool g_hash_table_insert(IntPtr table,IntPtr key,IntPtr value);
        [DllImport(Glib)] static extern void g_hash_table_unref(IntPtr table);
        [DllImport(Glib)] static extern void g_error_free(IntPtr error);
        [DllImport(Gio)] static extern IntPtr g_cancellable_new();
        [DllImport(Gio)] static extern void g_cancellable_cancel(IntPtr value);
        [DllImport("libgobject-2.0.so.0")] static extern void g_object_unref(IntPtr value);
        [DllImport(Secret)] static extern IntPtr secret_password_lookupv_sync(IntPtr schema,IntPtr attributes,IntPtr cancellable,out IntPtr error);
        [DllImport(Secret)] static extern int secret_password_storev_sync(IntPtr schema,IntPtr attributes,string collection,string label,string password,IntPtr cancellable,out IntPtr error);
        [DllImport(Secret)] static extern int secret_password_clearv_sync(IntPtr schema,IntPtr attributes,IntPtr cancellable,out IntPtr error);
        [DllImport(Secret)] static extern void secret_password_free(IntPtr password);
        sealed class Request : IDisposable
        {
            readonly IntPtr module;
            readonly IntPtr key,value;
            readonly CancellationTokenSource timeout;
            readonly CancellationTokenRegistration registration;
            public readonly IntPtr Attributes, Cancellable;
            public Request(string scope)
            {
                module=dlopen(Glib,1); if(module==IntPtr.Zero) throw new NotSupportedException("GLib unavailable.");
                Attributes=g_hash_table_new(dlsym(module,"g_str_hash"),dlsym(module,"g_str_equal"));
                key=Marshal.StringToHGlobalAnsi("pokechess-session-scope"); value=Marshal.StringToHGlobalAnsi(SessionStoreKey.For(scope));
                g_hash_table_insert(Attributes,key,value);
                Cancellable=g_cancellable_new(); timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                registration=timeout.Token.Register(()=>g_cancellable_cancel(Cancellable));
            }
            public void Dispose() { registration.Dispose(); timeout.Dispose(); g_object_unref(Cancellable); g_hash_table_unref(Attributes); Marshal.FreeHGlobal(key); Marshal.FreeHGlobal(value); dlclose(module); }
        }
        static void Check(IntPtr error) { if(error!=IntPtr.Zero) { g_error_free(error); throw new InvalidOperationException("Secret Service operation failed."); } }
        public Task<string> ReadAsync(string scope) => Task.Run(() => {
            using(var request=new Request(scope)) {
                var value=secret_password_lookupv_sync(IntPtr.Zero,request.Attributes,request.Cancellable,out var error);
                try { Check(error); return value==IntPtr.Zero?null:Marshal.PtrToStringAnsi(value); }
                finally { if(value!=IntPtr.Zero) secret_password_free(value); }
            }
        });
        public Task WriteAsync(string scope,string token) => Task.Run(() => {
            if(!AccountValidation.Token(token)) throw new ArgumentException("Invalid session token.");
            using(var request=new Request(scope)) {
                int result=secret_password_storev_sync(IntPtr.Zero,request.Attributes,"default","PokeChess account session",token,request.Cancellable,out var error);
                Check(error); if(result==0) throw new InvalidOperationException("Secret Service storage failed.");
            }
        });
        public Task DeleteAsync(string scope) => Task.Run(() => { using(var request=new Request(scope)) { secret_password_clearv_sync(IntPtr.Zero,request.Attributes,request.Cancellable,out var error); Check(error); } });
    }
}
