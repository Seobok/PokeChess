using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using PokeChess.Client.Services;

namespace PokeChess.Client.UI
{
    public enum AccountScreen { Login, Register, Recover, Manage }
    public sealed class AccountAuthenticationView : MonoBehaviour
    {
        private RectTransform canvas, card;
        private GameObject modal, tabs, form, codePanel, manageActions, sessionActions;
        private UnityEngine.UI.Text status, accountLabel, title;
        private UnityEngine.UI.InputField username, password, newPassword, confirm, recovery, issued;
        private UnityEngine.UI.Button submit, close, offline, acknowledge;
        private UnityEngine.UI.Toggle saved;
        private readonly List<UnityEngine.UI.Selectable> controls = new List<UnityEngine.UI.Selectable>();
        private AccountScreen screen;
        private double nextCheck;
        private bool wasRestoring;
        private AccountSessionService Service => AccountSessionService.Instance;
        private void Awake()
        {
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null) {
                var events = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
                events.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
            }
            canvas = UiPrefab.Instantiate("AccountCanvas", transform);
            modal = canvas.Find("Modal").gameObject; card = (RectTransform)modal.transform.Find("Card");
            var body = card.Find("Body");
            title = UiPrefab.Get<UnityEngine.UI.Text>(body, "Title");
            status = UiPrefab.Get<UnityEngine.UI.Text>(body, "Status");
            accountLabel = UiPrefab.Get<UnityEngine.UI.Text>(canvas, "AccountButton/Label");
            UiPrefab.Bind(canvas, "AccountButton", () => { modal.SetActive(true); Render(); });
            tabs = body.Find("Tabs").gameObject;
            sessionActions = body.Find("SessionActions").gameObject;
            UiPrefab.Bind(sessionActions.transform,"Restore",RetrySavedLogin);
            UiPrefab.Bind(sessionActions.transform,"Forget",ForgetSavedLogin);
            UiPrefab.Bind(tabs.transform, "Login", () => SelectScreen(AccountScreen.Login));
            UiPrefab.Bind(tabs.transform, "Register", () => SelectScreen(AccountScreen.Register));
            UiPrefab.Bind(tabs.transform, "Recover", () => SelectScreen(AccountScreen.Recover));
            form = body.Find("Form").gameObject;
            username = UiPrefab.Get<UnityEngine.UI.InputField>(form.transform, "Username/Input");
            password = UiPrefab.Get<UnityEngine.UI.InputField>(form.transform, "Password/Input");
            recovery = UiPrefab.Get<UnityEngine.UI.InputField>(form.transform, "Recovery/Input");
            newPassword = UiPrefab.Get<UnityEngine.UI.InputField>(form.transform, "NewPassword/Input");
            confirm = UiPrefab.Get<UnityEngine.UI.InputField>(form.transform, "Confirm/Input");
            submit = UiPrefab.Bind(body, "Submit", Submit);
            manageActions = body.Find("ManageActions").gameObject;
            UiPrefab.Bind(manageActions.transform, "Rotate", Rotate);
            UiPrefab.Bind(manageActions.transform, "LogoutRow/Logout", () => Logout(false));
            UiPrefab.Bind(manageActions.transform, "LogoutRow/LogoutAll", () => Logout(true));
            UiPrefab.Bind(manageActions.transform, "OnlineRetry", RetryOnline);
            codePanel = body.Find("RecoveryCode").gameObject;
            issued = UiPrefab.Get<UnityEngine.UI.InputField>(codePanel.transform, "Issued/Input");
            saved = UiPrefab.Get<UnityEngine.UI.Toggle>(codePanel.transform, "Saved");
            acknowledge = UiPrefab.Bind(codePanel.transform, "Acknowledge", () => {
                if (!saved.isOn) return;
                issued.text = ""; saved.isOn = false; Service.ConfirmRecoverySaved();
                SelectScreen(Service.IsSignedIn ? AccountScreen.Manage : AccountScreen.Login);
                status.text = Service.IsSignedIn ? "New recovery code saved." : "Recovery code saved. Sign in to continue.";
            });
            saved.onValueChanged.AddListener(_ => acknowledge.interactable = saved.isOn);
            close = UiPrefab.Bind(body, "Footer/Close", Close);
            offline = UiPrefab.Bind(body, "Footer/Offline", () => {
                if (Service.PendingRecoveryCode != null || (Service.IsBusy && !Service.IsRestoring)) return;
                Close(); FindFirstObjectByType<RoomFlowView>()?.StartLocalMatch();
            });
            controls.AddRange(canvas.GetComponentsInChildren<UnityEngine.UI.Selectable>(true));
            SelectScreen(Service.IsSignedIn ? AccountScreen.Manage : AccountScreen.Login);
            modal.SetActive(true);
        }
        private void OnEnable() { Service.Changed += Render; OnlineAuthenticationService.Instance.Changed += Render; Render(); }
        private void OnDisable() { Service.Changed -= Render; OnlineAuthenticationService.Instance.Changed -= Render; ClearSensitiveFields(); if (issued != null) issued.text = ""; }
        public void SelectScreen(AccountScreen value)
        {
            if (Service.IsBusy || Service.PendingRecoveryCode != null) return;
            ClearSensitiveFields(); screen = value; Render();
        }
        private void ClearSensitiveFields()
        { if (password == null) return; password.text = newPassword.text = confirm.text = recovery.text = ""; }
        private void Close() { if ((Service.IsBusy && !Service.IsRestoring) || Service.PendingRecoveryCode != null) return; ClearSensitiveFields(); modal.SetActive(false); }
        private void Update()
        {
            if (card != null) card.sizeDelta = new Vector2(Mathf.Min(620, canvas.rect.width - 24), 720);
            if (Service.IsSignedIn && !Service.IsBusy && Service.PendingRecoveryCode == null && Time.realtimeSinceStartupAsDouble >= nextCheck) {
                nextCheck = Time.realtimeSinceStartupAsDouble + 60; _ = Service.ValidateAsync();
            }
            if (!modal.activeSelf || Keyboard.current == null || !Keyboard.current.tabKey.wasPressedThisFrame) return;
            var fields = new[] { username, password, recovery, newPassword, confirm };
            var active = new List<UnityEngine.UI.InputField>();
            foreach (var field in fields) if (field.gameObject.activeInHierarchy && field.interactable) active.Add(field);
            if (active.Count == 0) return;
            var current = active.FindIndex(field => field.isFocused);
            var direction = Keyboard.current.shiftKey.isPressed ? -1 : 1;
            active[(current + direction + active.Count) % active.Count].Select();
        }
        private void OnApplicationFocus(bool focus) { if (focus) nextCheck = 0; }
        private void Render()
        {
            if (status == null) return;
            if (wasRestoring && !Service.IsRestoring && Service.IsSignedIn) modal.SetActive(false);
            wasRestoring = Service.IsRestoring;
            if (Service.IsSignedIn && screen != AccountScreen.Manage) { ClearSensitiveFields(); screen = AccountScreen.Manage; }
            if (!Service.IsSignedIn && screen == AccountScreen.Manage) { ClearSensitiveFields(); screen = AccountScreen.Login; }
            bool pending = Service.PendingRecoveryCode != null;
            foreach (var control in controls) control.interactable = !Service.IsBusy && !pending;
            bool isLogin = screen == AccountScreen.Login, isRegister = screen == AccountScreen.Register;
            bool isRecovery = screen == AccountScreen.Recover, isManage = screen == AccountScreen.Manage;
            title.text = pending ? "SAVE RECOVERY CODE" : isLogin ? "WELCOME BACK" : isRegister ? "CREATE ACCOUNT" : isRecovery ? "ACCOUNT RECOVERY" : "YOUR ACCOUNT";
            accountLabel.text = Service.IsSignedIn ? "Account / signed in" : "Account / offline";
            tabs.SetActive(!Service.IsSignedIn && !pending);
            sessionActions.SetActive(!Service.IsSignedIn && Service.HasSavedSession && !pending);
            form.SetActive(!pending); submit.gameObject.SetActive(!pending); manageActions.SetActive(isManage && !pending);
            username.transform.parent.gameObject.SetActive(!isManage);
            password.transform.parent.gameObject.SetActive(isLogin || isRegister || isManage);
            password.transform.parent.Find("Label").GetComponent<UnityEngine.UI.Text>().text = isManage ? "Current password" : "Password";
            recovery.transform.parent.gameObject.SetActive(isRecovery);
            newPassword.transform.parent.gameObject.SetActive(isRecovery || isManage);
            confirm.transform.parent.gameObject.SetActive(!isLogin);
            submit.GetComponentInChildren<UnityEngine.UI.Text>().text = isLogin ? "Sign in" : isRegister ? "Create account" : isRecovery ? "Reset password" : "Change password";
            codePanel.SetActive(pending);
            if (pending) {
                modal.SetActive(true);
                if (issued.text != Service.PendingRecoveryCode) { issued.text = Service.PendingRecoveryCode; saved.isOn = false; }
                issued.interactable = true; saved.interactable = true; acknowledge.interactable = saved.isOn;
            } else { issued.text = ""; saved.isOn = false; }
            close.interactable = offline.interactable = (!Service.IsBusy || Service.IsRestoring) && !pending;
            if (Service.IsBusy) status.text = Service.IsRestoring ? "Checking your saved login... Local play is available." : "Please wait...";
            else if (Service.Error != null) status.text = ErrorMessage(Service.Error) + (Service.RetryAfterSeconds > 0 ? " Retry after " + Service.RetryAfterSeconds + " seconds." : "");
            else if (pending) status.text = "Shown once. Keep this code somewhere safe, separate from this app.";
            else if (Service.StorageWarning != null) status.text = StorageMessage(Service.StorageWarning);
            else if (isManage) status.text = OnlineAuthenticationService.Instance.IsReady ? "Signed in. Online play is available. Local play is always available." : OnlineMessage(OnlineAuthenticationService.Instance.Error);
            else status.text = isRecovery ? "Use your saved code to set a new password. You will sign in again afterwards." : "Username: 3-32 letters, digits or _. Password: 15-128 characters. Local play needs no account.";
        }
        private async void RetrySavedLogin() { if(await Service.RestoreAsync() && this!=null) Close(); }
        private async void ForgetSavedLogin() { await Service.ForgetSavedSessionAsync(); }
        private static string OnlineMessage(string error)
        {
            if(error=="online_not_configured") return "Signed in. Online play is not set up yet. Local play is available.";
            if(error=="ugs_identity_conflict") return "Signed in. This online identity needs an administrator to resolve a link conflict.";
            return "Signed in. Online connection is unavailable. Retry below or play locally.";
        }
        private static string StorageMessage(string code)
        {
            switch(code) {
                case "saved_login_clear_failed": return "Could not clear the saved login. Retry forgetting it before closing the app.";
                case "session_not_saved": return "Signed in for this run. Your login could not be saved securely; sign in again next time.";
                case "saved_login_invalid": return "The saved login is unavailable. Sign in again.";
                default: return "Could not unlock your saved login. Retry, sign in again, or play locally.";
            }
        }
        private static string ErrorMessage(string code)
        {
            switch (code) {
                case "invalid_credentials": return "Username or password is incorrect.";
                case "invalid_recovery": return "Username or recovery code is incorrect.";
                case "server_changed": return "The account server changed. Sign in again for this server.";
                case "session_expired": return "Your session ended. Sign in again.";
                case "username_unavailable": return "This username is already taken.";
                case "too_many_attempts": return "Too many attempts. Please wait before retrying.";
                case "server_busy": return "The server is busy. Please retry shortly.";
                case "invalid_format": return "Check your username and password format.";
                case "invalid_response": return "The server response was incomplete. Sign in and try again.";
                default: return "Could not reach the account server. Retry or continue with local play.";
            }
        }
        private async void Submit()
        {
            if (Service.IsBusy || Service.PendingRecoveryCode != null) return;
            if (screen != AccountScreen.Manage && !AccountValidation.Username(username.text)) { status.text = "Use a username of 3-32 letters, digits or _."; return; }
            string current = password.text, next = newPassword.text, repeated = confirm.text, code = recovery.text, name = username.text;
            string chosen = screen == AccountScreen.Login || screen == AccountScreen.Register ? current : next;
            if (!AccountValidation.Password(chosen) || (screen == AccountScreen.Manage && !AccountValidation.Password(current))) { status.text = "Passwords must contain 15-128 characters."; return; }
            if (screen != AccountScreen.Login && chosen != repeated) { status.text = "The two passwords do not match."; return; }
            if (screen == AccountScreen.Manage && current == next) { status.text = "Choose a different new password."; return; }
            if (screen == AccountScreen.Recover && !AccountValidation.RecoveryCode(code)) { status.text = "Enter the full saved recovery code."; return; }
            var action = screen; ClearSensitiveFields();
            bool success = action == AccountScreen.Login ? await Service.LoginAsync(name, current) :
                action == AccountScreen.Register ? await Service.RegisterAsync(name, current) :
                action == AccountScreen.Recover ? await Service.RecoverAsync(name, code, next) : await Service.ChangePasswordAsync(current, next);
            if (this == null) return;
            if (success && action == AccountScreen.Login) { nextCheck = Time.realtimeSinceStartupAsDouble + 60; Close(); }
            if (success && action == AccountScreen.Manage) { SelectScreen(AccountScreen.Login); status.text = "Password changed. All devices signed out. Sign in again."; }
        }
        private async void Rotate()
        {
            if (!AccountValidation.Password(password.text)) { status.text = "Enter your current password to issue a new recovery code."; return; }
            var current = password.text; ClearSensitiveFields(); await Service.RotateRecoveryCodeAsync(current);
        }
        private async void Logout(bool all)
        {
            ClearSensitiveFields(); bool success = await Service.LogoutAsync(all);
            if (this == null) return;
            if (!success) status.text = "Signed out on this device. Could not confirm server revocation; other sessions may remain active.";
        }
        private async void RetryOnline() { if (Service.IsSignedIn) await OnlineAuthenticationService.Instance.EnsureSignedInAsync(); }
    }
}
