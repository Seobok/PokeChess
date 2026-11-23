using System;
using UnityEngine;
using UnityEngine.Events;

namespace PokeChess.Client.UI
{
    internal static class UiPrefab
    {
        public static RectTransform Instantiate(string name, Transform parent, Font fallbackFont = null)
        {
            var prefab = Resources.Load<RectTransform>("UI/" + name);
            if (prefab == null)
                throw new InvalidOperationException($"Missing UI prefab: {name}");

            var instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            instance.name = prefab.name;
            var uiFont = fallbackFont != null ? fallbackFont : UiFont.Default;
            foreach (var label in instance.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                label.font = uiFont;
            return instance;
        }

        public static T Get<T>(Transform root, string path) where T : Component
        {
            var child = root.Find(path);
            if (child != null && child.TryGetComponent<T>(out var component))
                return component;

            throw new InvalidOperationException($"Missing {typeof(T).Name} at {root.name}/{path}");
        }

        public static UnityEngine.UI.Button Bind(Transform root, string path, UnityAction action)
        {
            var button = Get<UnityEngine.UI.Button>(root, path);
            button.onClick.AddListener(action);
            return button;
        }
    }
}
