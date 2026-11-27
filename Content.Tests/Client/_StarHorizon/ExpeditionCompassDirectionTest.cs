using System.Numerics;
using Content.Client._StarHorizon.Expedition;
using NUnit.Framework;
using Robust.Shared.Graphics;
using Robust.Shared.Maths;

namespace Content.Tests.Client._StarHorizon;

/// <summary>
/// Компас интерфейса экспедиции поворачивается вместе с камерой: направление на экране совпадает с тем, как матрица
/// вида камеры движка переводит то же направление из мира на экран.
/// </summary>
[TestFixture]
public sealed class ExpeditionCompassDirectionTest
{
    [TestCase(0)]
    [TestCase(90)]
    [TestCase(180)]
    [TestCase(-90)]
    [TestCase(37)]
    public void CompassFollowsCameraRotation(double degrees)
    {
        var eye = new Eye { Rotation = Angle.FromDegrees(degrees) };
        eye.GetViewMatrix(out var view, Vector2.One);

        foreach (var dir in new[] { new Vector2(0, 1), new Vector2(1, 0), Vector2.Normalize(new Vector2(-2, 1)) })
        {
            // Вид камеры: Y вверх, на экране Y вниз.
            var viewDir = Vector2.Normalize(Vector2.TransformNormal(dir, view));
            var expected = new Vector2(viewDir.X, -viewDir.Y);
            var actual = ExpeditionHudOverlay.WorldToScreenDirection(eye.Rotation, dir);

            Assert.That((actual - expected).Length(), Is.LessThan(0.001f), $"камера {degrees}°, направление {dir}: {actual} против {expected}");
        }
    }

    [Test]
    public void NorthIsUpWithoutRotation()
    {
        var north = ExpeditionHudOverlay.WorldToScreenDirection(Angle.Zero, new Vector2(0, 1));
        Assert.That((north - new Vector2(0, -1)).Length(), Is.LessThan(0.001f), "север вверху экрана");
    }
}
