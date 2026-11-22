using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace PokeChess.Client.Services
{
    public interface ISessionTokenStore
    {
        Task<string> ReadAsync(string scope);
        Task WriteAsync(string scope, string token);
        Task DeleteAsync(string scope);
    }
    // Only a non-secret automatic-restore permission is stored here, never a token or password.
    public interface ISessionRestoreGuard
    {
        bool IsBlocked(string scope);
        void Block(string scope);
        void Allow(string scope);
    }
    public sealed class MemoryRestoreGuard : ISessionRestoreGuard
    {
        readonly HashSet<string> blocked = new HashSet<string>();
        public bool IsBlocked(string scope) => blocked.Contains(scope);
        public void Block(string scope) => blocked.Add(scope);
        public void Allow(string scope) => blocked.Remove(scope);
    }
    public sealed class FileRestoreGuard : ISessionRestoreGuard
    {
        readonly string directory;
        public FileRestoreGuard(string directory) { this.directory = directory; }
        string PathFor(string scope) => Path.Combine(directory, SessionStoreKey.Hash(scope) + ".blocked");
        public bool IsBlocked(string scope)
        {
            // File.Exists alone suppresses access errors. Opening the marker distinguishes denial from absence.
            try { using (File.Open(PathFor(scope), FileMode.Open, FileAccess.Read, FileShare.Read)) return true; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }
        public void Block(string scope)
        {
            Directory.CreateDirectory(directory);
            using (var file = new FileStream(PathFor(scope),FileMode.Create,FileAccess.Write,FileShare.None)) { file.WriteByte(1); file.Flush(true); }
        }
        public void Allow(string scope) { File.Delete(PathFor(scope)); }
    }
    public sealed class BrowserRestoreGuard : ISessionRestoreGuard
    {
        string Key(string scope) => "PokeChess.RestoreBlocked.v1." + SessionStoreKey.Hash(scope);
        public bool IsBlocked(string scope) => PlayerPrefs.GetInt(Key(scope),0) == 1;
        public void Block(string scope) { PlayerPrefs.SetInt(Key(scope),1); PlayerPrefs.Save(); }
        public void Allow(string scope) { PlayerPrefs.DeleteKey(Key(scope)); PlayerPrefs.Save(); }
    }
    public static class SessionStoreKey
    {
        public static string Hash(string scope)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(scope))).Replace("-", "").ToLowerInvariant();
        }
        public static string For(string scope) => "PokeChess.AccountSession.v1." + Hash(scope);
    }
    // Injectable no-persistence default keeps unit tests from accessing a real user's credential vault.
    public sealed class VolatileSessionTokenStore : ISessionTokenStore
    {
        readonly Dictionary<string,string> values=new Dictionary<string,string>();
        public Task<string> ReadAsync(string scope) => Task.FromResult(values.TryGetValue(scope,out var token)?token:null);
        public Task WriteAsync(string scope,string token) { values[scope]=token; return Task.CompletedTask; }
        public Task DeleteAsync(string scope) { values.Remove(scope); return Task.CompletedTask; }
    }
    public sealed class UnavailableSessionTokenStore : ISessionTokenStore
    {
        public Task<string> ReadAsync(string scope) => Task.FromResult<string>(null);
        public Task WriteAsync(string scope, string token) => throw new NotSupportedException("Secure session storage is unavailable.");
        public Task DeleteAsync(string scope) => Task.CompletedTask;
    }
    public static class SessionTokenStores
    {
        public static ISessionTokenStore Create()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return new WindowsSessionTokenStore();
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS
            return new AppleSessionTokenStore();
#elif UNITY_ANDROID
            return new AndroidSessionTokenStore();
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            return new LinuxSessionTokenStore();
#elif UNITY_WEBGL
            // HttpOnly cookies are managed by the browser, not read by Unity or persisted here.
            return new VolatileSessionTokenStore();
#else
            return new UnavailableSessionTokenStore();
#endif
        }
        public static ISessionRestoreGuard CreateGuard()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new BrowserRestoreGuard();
#else
            return new FileRestoreGuard(Path.Combine(Application.persistentDataPath,"AccountRestoreFlags"));
#endif
        }
    }
}
