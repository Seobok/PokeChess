using System;
using System.Threading.Tasks;
using PokeChess.Client.Services;

namespace PokeChess.Editor
{
    public static class AccountClientVerification
    {
        public static string Result { get; private set; } = "not_started";
        public static void Start(string url) { Result = "running"; Run(url); }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static async void Run(string url)
        {
            try {
                var service = new AccountSessionService(new UnityAccountApi(url,true));
                var username = "unity_" + Guid.NewGuid().ToString("N").Substring(0,16);
                const string password = "unity-integration-password-123";
                const string changed = "unity-integration-updated-123";
                Check(await service.RegisterAsync(username,password), "Register failed");
                var firstCode = service.PendingRecoveryCode;
                Check(AccountValidation.RecoveryCode(firstCode) && !service.IsSignedIn, "Registration recovery result");
                Check(!await service.LoginAsync(username,password), "Code acknowledgement bypass");
                service.ConfirmRecoverySaved();
                Check(await service.LoginAsync(username,password), "Login failed");
                Check(await service.ValidateAsync(), "Me failed");
                Check(!await service.ChangePasswordAsync("wrong-current-password",changed) && service.IsSignedIn, "Current-password failure lost session");
                Check(await service.RotateRecoveryCodeAsync(password), "Rotate failed");
                var rotated = service.PendingRecoveryCode; service.ConfirmRecoverySaved();
                Check(!await service.RecoverAsync(username,firstCode,changed) && service.IsSignedIn, "Old recovery code accepted/session changed");
                Check(await service.RecoverAsync(username,rotated,changed) && !service.IsSignedIn, "Recovery failed");
                Check(AccountValidation.RecoveryCode(service.PendingRecoveryCode), "Replacement recovery code missing");
                service.ConfirmRecoverySaved();
                Check(!await service.LoginAsync(username,password), "Old password accepted");
                Check(await service.LoginAsync(username,changed), "Recovered password rejected");
                Check(await service.ChangePasswordAsync(changed,password) && !service.IsSignedIn, "Change-password signout failed");
                Check(await service.LoginAsync(username,password), "New password login failed");
                Check(await service.LogoutAsync(true) && !service.IsSignedIn, "Logout-all failed");
                Result = "passed: real UnityWebRequest registration/login/me, current-password check, recovery rotation/reset, password change, logout-all";
            } catch (Exception error) { Result = "failed: " + error.GetType().Name + ": " + error.Message; }
        }
    }
}
