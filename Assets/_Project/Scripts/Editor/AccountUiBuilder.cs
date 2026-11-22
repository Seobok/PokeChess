using UnityEditor;
using UnityEngine;

namespace PokeChess.Editor
{
    public static class AccountUiBuilder
    {
        static Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        static RectTransform Rect(string name, Transform parent) { var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false); return r; }
        static void Stretch(RectTransform r, float inset = 0) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.one * inset; r.offsetMax = -Vector2.one * inset; }
        static void Height(GameObject go, float height) { var layout = go.AddComponent<UnityEngine.UI.LayoutElement>(); layout.minHeight = layout.preferredHeight = height; layout.flexibleHeight = 0; }
        static UnityEngine.UI.Text Label(string name, Transform parent, string value, int size, float height)
        {
            var r = Rect(name, parent); var text = r.gameObject.AddComponent<UnityEngine.UI.Text>();
            text.font = Font; text.fontSize = size; text.color = new Color(.9f,.94f,1); text.text = value;
            text.supportRichText = false; text.raycastTarget = false; text.alignment = TextAnchor.MiddleLeft;
            if (height > 0) Height(r.gameObject, height); return text;
        }
        static RectTransform Vertical(string name, Transform parent)
        {
            var r = Rect(name, parent); var group = r.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            group.spacing = 8; group.childControlHeight = group.childControlWidth = true; group.childForceExpandHeight = false;
            return r;
        }
        static RectTransform Row(string name, Transform parent, float height)
        {
            var r = Rect(name, parent); Height(r.gameObject,height); var group = r.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            group.spacing = 8; group.childControlWidth = group.childControlHeight = true; group.childForceExpandWidth = true; group.childForceExpandHeight = false; return r;
        }
        static UnityEngine.UI.Button Button(string name, Transform parent, string label, float height = 38)
        {
            var r = Rect(name,parent); Height(r.gameObject,height);
            var image = r.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = new Color(.15f,.29f,.43f);
            var button = r.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            var text = Label("Label",r,label,16,0); text.alignment = TextAnchor.MiddleCenter; Stretch(text.rectTransform,3); return button;
        }
        static UnityEngine.UI.InputField Field(string name, Transform parent, string label, bool secret, int limit = 128)
        {
            var group = Vertical(name,parent); Height(group.gameObject,60); group.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().spacing = 2;
            Label("Label", group, label,14,18);
            var r = Rect("Input",group); Height(r.gameObject,40); var image = r.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = new Color(.035f,.065f,.10f);
            var text = Label("Text",r,"",17,0); Stretch(text.rectTransform,9); text.raycastTarget = false;
            var input = r.gameObject.AddComponent<UnityEngine.UI.InputField>(); input.targetGraphic = image; input.textComponent = text;
            input.contentType = secret ? UnityEngine.UI.InputField.ContentType.Password : UnityEngine.UI.InputField.ContentType.Standard;
            input.characterLimit = limit; input.keyboardType = secret ? TouchScreenKeyboardType.ASCIICapable : TouchScreenKeyboardType.Default;
            return input;
        }
        public static string AddSessionActions()
        {
            const string path="Assets/_Project/Resources/UI/AccountCanvas.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try {
                var body=root.transform.Find("Modal/Card/Body");
                if(body==null || body.Find("Form")==null) throw new System.InvalidOperationException("Account form missing");
                if(body.Find("SessionActions")!=null) return "Session actions already exist";
                var row=Row("SessionActions",body,38);
                row.SetSiblingIndex(body.Find("Form").GetSiblingIndex());
                Button("Restore",row,"Retry saved login"); Button("Forget",row,"Forget saved login");
                row.gameObject.SetActive(false); PrefabUtility.SaveAsPrefabAsset(root,path);
                return "Added saved-login retry and forget actions";
            } finally { PrefabUtility.UnloadPrefabContents(root); }
        }        public static string Build()
        {
            const string path = "Assets/_Project/Resources/UI/AccountCanvas.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return "AccountCanvas already exists; preserved.";
            var root = Rect("AccountCanvas",null);
            try {
                var canvas = root.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 150;
                var scaler = root.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280,800); scaler.matchWidthOrHeight = 1;
                root.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                var account = Button("AccountButton",root,"Account / offline").GetComponent<RectTransform>();
                account.anchorMin = account.anchorMax = account.pivot = Vector2.one; account.anchoredPosition = new Vector2(-16,-16); account.sizeDelta = new Vector2(220,40);
                var modal = Rect("Modal",root); Stretch(modal); var dim = modal.gameObject.AddComponent<UnityEngine.UI.Image>(); dim.color = new Color(0,.015f,.03f,.92f);
                var card = Rect("Card",modal); card.sizeDelta = new Vector2(620,720); var background = card.gameObject.AddComponent<UnityEngine.UI.Image>(); background.color = new Color(.065f,.105f,.16f);
                var body = Vertical("Body",card); Stretch(body,20);
                Label("Title",body,"WELCOME BACK",24,34);
                Label("Status",body,"Local play is always available.",16,64);
                var tabs = Row("Tabs",body,36); Button("Login",tabs,"Sign in"); Button("Register",tabs,"Sign up"); Button("Recover",tabs,"Recovery");
                var sessions = Row("SessionActions",body,38); Button("Restore",sessions,"Retry saved login"); Button("Forget",sessions,"Forget saved login");
                var form = Vertical("Form",body);
                Field("Username",form,"Username",false,32); Field("Password",form,"Password / current password",true);
                Field("Recovery",form,"Saved recovery code",false,71); Field("NewPassword",form,"New password",true); Field("Confirm",form,"Confirm password",true);
                Button("Submit",body,"Sign in",42);
                var actions = Vertical("ManageActions",body);
                Button("Rotate",actions,"Issue a new recovery code");
                var logout = Row("LogoutRow",actions,38); Button("Logout",logout,"Sign out"); Button("LogoutAll",logout,"Sign out all devices");
                Button("OnlineRetry",actions,"Retry online connection");
                var recovery = Vertical("RecoveryCode",body);
                Label("Instructions",recovery,"Keep this code in a password manager or another safe place. Anyone with this code and your username can reset your password. It cannot be shown again.",16,94);
                var issued = Field("Issued",recovery,"Your new recovery code (select to copy)",false,71);
                issued.readOnly = true; issued.lineType = UnityEngine.UI.InputField.LineType.MultiLineNewline;
                issued.textComponent.fontSize = 16; issued.textComponent.alignment = TextAnchor.UpperLeft;
                issued.transform.parent.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 104;
                issued.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 84;
                var toggleRoot = Rect("Saved",recovery); Height(toggleRoot.gameObject,40);
                var toggle = toggleRoot.gameObject.AddComponent<UnityEngine.UI.Toggle>();
                var box = Rect("Box",toggleRoot); box.anchorMin = box.anchorMax = new Vector2(0,.5f); box.pivot = new Vector2(0,.5f); box.sizeDelta = new Vector2(26,26);
                var boxImage = box.gameObject.AddComponent<UnityEngine.UI.Image>(); boxImage.color = new Color(.2f,.32f,.45f); toggle.targetGraphic = boxImage;
                var check = Rect("Check",box); Stretch(check,5); var checkImage = check.gameObject.AddComponent<UnityEngine.UI.Image>(); checkImage.color = new Color(.4f,.9f,.7f); toggle.graphic = checkImage;
                var savedText = Label("Label",toggleRoot,"I saved this recovery code.",16,0); Stretch(savedText.rectTransform); savedText.rectTransform.offsetMin = new Vector2(38,0);
                toggle.isOn = false;
                Button("Acknowledge",recovery,"Continue after saving");
                var spacer = Rect("Spacer",body); spacer.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleHeight = 1;
                var footer = Row("Footer",body,40); Button("Offline",footer,"Play locally"); Button("Close",footer,"Close");
                PrefabUtility.SaveAsPrefabAsset(root.gameObject,path);
                if (Resources.Load<PokeChess.Client.Services.AccountApiSettings>("AccountApiSettings") == null) {
                    var settings = ScriptableObject.CreateInstance<PokeChess.Client.Services.AccountApiSettings>();
                    settings.BaseUrl = ""; AssetDatabase.CreateAsset(settings,"Assets/_Project/Resources/AccountApiSettings.asset");
                }
                AssetDatabase.SaveAssets(); return path;
            } finally { Object.DestroyImmediate(root.gameObject); }
        }
    }
}
