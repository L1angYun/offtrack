using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Offtrack.Runtime.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace Offtrack.Tests.PlayMode
{
    public sealed class PresentationShapeTests
    {
        private readonly List<GameObject> created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in created) Object.DestroyImmediate(item);
            created.Clear();
        }

        [TestCase(TextAnchor.MiddleCenter, -10f, 0f, 10f)]
        [TestCase(TextAnchor.MiddleLeft, 0f, 10f, 20f)]
        [TestCase(TextAnchor.MiddleRight, -20f, -10f, 0f)]
        public void LetterSpacingSpreadsGlyphQuadsAndKeepsTheAlignmentAnchor(TextAnchor alignment, float first, float second, float third)
        {
            var text = NewObject("Copy").AddComponent<Text>();
            text.font = FontProvider.Fallback;
            text.alignment = alignment;
            var spacing = text.gameObject.AddComponent<LetterSpacing>();
            spacing.Spacing = 10;
            var vh = new VertexHelper();
            for (int quad = 0; quad < 3; quad++) AddQuad(vh, quad * 20f);

            spacing.ModifyMesh(vh);

            var expected = new[] { first, second, third };
            var vertex = new UIVertex();
            for (int quad = 0; quad < 3; quad++)
                for (int corner = 0; corner < 4; corner++)
                {
                    vh.PopulateUIVertex(ref vertex, quad * 4 + corner);
                    float original = quad * 20f + (corner == 0 || corner == 1 ? 0 : 10);
                    Assert.That(vertex.position.x, Is.EqualTo(original + expected[quad]).Within(1e-4f), $"quad {quad} corner {corner}");
                }
        }

        [Test]
        public void ArcDrawsOnlyTheRequestedSweepAtTheRequestedStroke()
        {
            var shape = NewObject("Arc").AddComponent<FlatShape>();
            shape.Kind = ShapeKind.Arc;
            shape.Segments = 192;
            shape.StrokeWidth = 3;
            shape.ArcStart = 45;
            shape.ArcSweep = 90;
            shape.rectTransform.sizeDelta = new Vector2(20, 20);
            var vh = new VertexHelper();
            typeof(FlatShape).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(VertexHelper) }, null).Invoke(shape, new object[] { vh });

            // 192 segments per full turn -> 48 for a quarter turn; two vertices per step.
            Assert.That(vh.currentVertCount, Is.EqualTo((48 + 1) * 2));
            Assert.That(vh.currentIndexCount, Is.EqualTo(48 * 6));
            var vertex = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                var point = (Vector2)vertex.position;
                float angle = Mathf.Atan2(point.y, point.x) * Mathf.Rad2Deg;
                Assert.That(angle, Is.InRange(45f - 1e-3f, 135f + 1e-3f));
                Assert.That(point.magnitude, Is.EqualTo(i % 2 == 0 ? 10f : 7f).Within(1e-4f));
            }
        }

        private GameObject NewObject(string name)
        {
            var item = new GameObject(name, typeof(RectTransform));
            created.Add(item);
            return item;
        }

        private static void AddQuad(VertexHelper vh, float x)
        {
            int start = vh.currentVertCount;
            vh.AddVert(new Vector3(x, 0), Color.white, Vector2.zero);
            vh.AddVert(new Vector3(x, 10), Color.white, Vector2.zero);
            vh.AddVert(new Vector3(x + 10, 10), Color.white, Vector2.zero);
            vh.AddVert(new Vector3(x + 10, 0), Color.white, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
