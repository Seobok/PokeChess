using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using PokeChess.Client.UI;

namespace PokeChess.Core.Tests
{
    public sealed class UiFontLayoutTests
    {
        [TestCase("RoundBadge/RoundBadgeText", "1-1")]
        [TestCase("RoundBadge/RoundBadgeText", "10-6")]
        [TestCase("BoardCapacity/CapacityCount", "0 / 1")]
        [TestCase("BoardCapacity/CapacityCount", "9 / 10")]
        public void MatchHudNumbersProduceVisibleGeometryWithBundledFont(string path, string value)
        {
            var factory = typeof(PlacementSandboxView).Assembly.GetType("PokeChess.Client.UI.UiPrefab");
            var root = (RectTransform)factory.GetMethod("Instantiate", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { "MatchHud", null, null });
            try
            {
                var label = root.Find(path).GetComponent<UnityEngine.UI.Text>();
                label.text = value;
                using (var generator = new TextGenerator())
                {
                    var settings = label.GetGenerationSettings(label.rectTransform.rect.size);
                    Assert.That(generator.Populate(value, settings), Is.True);
                    Assert.That(generator.characterCountVisible, Is.EqualTo(value.Length),
                        $"{path} must render the full value, including both counts/stage numbers");
                    Assert.That(label.font.name, Is.EqualTo("NotoSansKR-Regular"));
                }
            }
            finally { Object.DestroyImmediate(root.gameObject); }
        }
    }
}
