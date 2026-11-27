using System;
using System.Collections.Generic;
using UnityEngine;

namespace PokeChess.Client.UI
{
    internal sealed class PassengerBattleVisuals
    {
        private sealed class Transition
        {
            public RectTransform Root;
            public UnityEngine.UI.Image Effect;
            public PokemonSpriteView Passenger;
            public CanvasGroup Fade;
            public Vector2 From, To;
            public long Tick;
            public bool Boarding;
        }
        private readonly Dictionary<string, Transition> transitions = new Dictionary<string, Transition>();

        public void Signal(Transform parent, string detail, string target, string species, Vector2 from, Vector2 to, long tick)
        {
            if (detail != "BOARD" && detail != "DISEMBARK") return;
            string key = detail + ":" + target + ":" + tick;
            if (transitions.ContainsKey(key)) return;
            var root = new GameObject("Passenger_" + detail, typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(parent, false);
            var transition = new Transition { Root = root, Effect = GuardianVisuals.Create(root, "boarding"),
                From = from, To = to, Tick = tick, Boarding = detail == "BOARD" };
            if (transition.Boarding && !string.IsNullOrEmpty(species))
            {
                var ghost = new GameObject("BoardingPassenger", typeof(RectTransform), typeof(CanvasGroup));
                ghost.transform.SetParent(root, false);
                transition.Fade = ghost.GetComponent<CanvasGroup>();
                transition.Fade.blocksRaycasts = false;
                transition.Passenger = ghost.AddComponent<PokemonSpriteView>();
                transition.Passenger.Initialize(species);
            }
            transitions.Add(key, transition);
        }

        public void Render(double tick, int rate, float scale)
        {
            foreach (var transition in transitions.Values)
            {
                double age = (tick - transition.Tick) / Math.Max(1, rate);
                transition.Root.gameObject.SetActive(age >= 0 && age < .6);
                if (age < 0 || age >= .6) continue;
                float progress = Mathf.Clamp01((float)age / .6f);
                transition.Root.anchoredPosition = Vector2.Lerp(transition.From, transition.To, progress);
                GuardianVisuals.Draw(transition.Effect, "barrier-create", age, false);
                transition.Effect.color = new Color(.55f, .9f, 1, 1 - progress);
                transition.Effect.rectTransform.sizeDelta = Vector2.one * 60 * scale *
                    (transition.Boarding ? 1 - .5f * progress : .5f + progress);
                transition.Effect.rectTransform.anchoredPosition = Vector2.up * 16 * scale;
                if (transition.Passenger != null)
                {
                    transition.Passenger.ShowIdle(age);
                    transition.Fade.alpha = 1 - progress;
                    transition.Passenger.transform.localScale = Vector3.one * (1 - .75f * progress);
                }
                transition.Root.SetAsLastSibling();
            }
        }

        public void Clear()
        {
            foreach (var transition in transitions.Values) EarthVisuals.Release(transition.Root.gameObject);
            transitions.Clear();
        }
    }
}
