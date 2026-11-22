using System;
using System.Threading.Tasks;
using UnityEngine;

namespace PokeChess.Client.Services
{
    public sealed class AndroidSessionTokenStore : ISessionTokenStore
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        static T Call<T>(string method,string scope,string token=null)
        {
            using(var unity=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using(var activity=unity.GetStatic<AndroidJavaObject>("currentActivity"))
            using(var context=activity.Call<AndroidJavaObject>("getApplicationContext"))
            using(var store=new AndroidJavaClass("com.pokechess.security.SecretSessionStore"))
                return token==null?store.CallStatic<T>(method,context,SessionStoreKey.For(scope)):store.CallStatic<T>(method,context,SessionStoreKey.For(scope),token);
        }
        public Task<string> ReadAsync(string scope) => Task.FromResult(Call<string>("read",scope));
        public Task WriteAsync(string scope,string token)
        {
            if(!AccountValidation.Token(token)) throw new ArgumentException("Invalid session token.");
            if(!Call<bool>("write",scope,token)) throw new InvalidOperationException("Keystore write failed.");
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string scope) { if(!Call<bool>("delete",scope)) throw new InvalidOperationException("Keystore delete failed."); return Task.CompletedTask; }
#else
        public Task<string> ReadAsync(string scope) => throw new PlatformNotSupportedException();
        public Task WriteAsync(string scope,string token) => throw new PlatformNotSupportedException();
        public Task DeleteAsync(string scope) => throw new PlatformNotSupportedException();
#endif
    }
}
