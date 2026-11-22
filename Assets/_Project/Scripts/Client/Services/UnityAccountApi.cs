using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace PokeChess.Client.Services
{
    public sealed class UnityAccountApi : IAccountApi
    {
        public bool UsesCookies => Application.platform == RuntimePlatform.WebGLPlayer;
        public string SessionScope
        {
            get {
                var settings=Resources.Load<AccountApiSettings>("AccountApiSettings");
                var configured=overrideUrl??(settings==null?"":settings.BaseUrl);
                if(!TryBaseUrl(configured,Application.absoluteURL,UsesCookies,out var root,allowEditorLoopback || (settings!=null && settings.AllowLoopbackHttpInEditor))) return null;
                return Application.identifier+"|"+(Application.isEditor?"editor":"player")+"|"+root.AbsoluteUri;
            }
        }
        private readonly string overrideUrl;
        private readonly bool allowEditorLoopback;
        public UnityAccountApi(string baseUrl = null, bool allowEditorLoopback = false) { overrideUrl = baseUrl; this.allowEditorLoopback = allowEditorLoopback; }
        public static bool TryBaseUrl(string configured, string pageUrl, bool web, out Uri uri, bool allowEditorLoopback = false)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(configured) && web && Uri.TryCreate(pageUrl, UriKind.Absolute, out var page))
                configured = page.GetLeftPart(UriPartial.Authority);
            if (!Uri.TryCreate(configured, UriKind.Absolute, out var candidate) || !(candidate.Scheme == Uri.UriSchemeHttps || EditorLoopback(candidate, allowEditorLoopback)) ||
                candidate.UserInfo.Length != 0 || candidate.Query.Length != 0 || candidate.Fragment.Length != 0) return false;
            if (web && (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var origin) || candidate.GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority))) return false;
            uri = new Uri(candidate.AbsoluteUri.TrimEnd('/') + "/"); return true;
        }
        private static bool EditorLoopback(Uri uri, bool allowed)
        {
#if UNITY_EDITOR
            return allowed && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
#else
            return false;
#endif
        }
        [Serializable] private sealed class Response { public string accountId, token, recoveryCode,playerId,accessToken,projectId,environment,environmentId,admissionTicket,error; public long expiresAt; public bool authorized; }
        public async Task<AccountReply> SendAsync(string method, string path, AccountPayload payload, string token)
        {
            var settings = Resources.Load<AccountApiSettings>("AccountApiSettings");
            var configured = overrideUrl ?? (settings == null ? "" : settings.BaseUrl);
            if (!TryBaseUrl(configured, Application.absoluteURL, UsesCookies, out var root, allowEditorLoopback || (settings != null && settings.AllowLoopbackHttpInEditor))) throw new InvalidOperationException("Account API requires HTTPS configuration.");
            using (var request = new UnityWebRequest(new Uri(root, path.TrimStart('/')), method))
            {
                request.timeout = 20; request.redirectLimit = 0;
                request.downloadHandler = new DownloadHandlerBuffer();
                if (payload != null) {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload)));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.SetRequestHeader("X-PokeChess-Request", "1");
                if (!UsesCookies && token != null) request.SetRequestHeader("Authorization", "Bearer " + token);
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();
                var reply = new AccountReply { Status = request.responseCode };
                if (int.TryParse(request.GetResponseHeader("Retry-After"), out var seconds)) reply.RetryAfterSeconds = Math.Max(0, seconds);
                if (request.responseCode > 0 && !string.IsNullOrWhiteSpace(request.downloadHandler.text)) {
                    Response data; try { data=JsonUtility.FromJson<Response>(request.downloadHandler.text); } catch { return reply; } if(data==null) return reply;
                    reply.AccountId = data.accountId; reply.Token = data.token; reply.RecoveryCode = data.recoveryCode;
                    reply.PlayerId=data.playerId; reply.AccessToken=data.accessToken; reply.ProjectId=data.projectId; reply.Environment=data.environment; reply.EnvironmentId=data.environmentId;
                    reply.ExpiresAt=data.expiresAt; reply.AdmissionTicket=data.admissionTicket; reply.Authorized=data.authorized;
                    // Only known safe public error codes, never a server-provided arbitrary message.
                    if(data.error=="online_not_configured" || data.error=="ugs_identity_conflict" || data.error=="ugs_unavailable" || data.error=="ugs_busy" || data.error=="ugs_rate_limited" || data.error=="ugs_invalid_response") reply.ErrorCode=data.error;
                }
                // Never expose request bodies, server error bodies or credentials in logs/UI.
                return reply;
            }
        }
    }
}
