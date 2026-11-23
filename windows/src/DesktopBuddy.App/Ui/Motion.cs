using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace DesktopBuddy.App.Ui;

/// Desktop Buddy motion (windows/DESIGN.md §Motion). Only opacity and scale ever move — never
/// position (house rule: no slide-in or slide-out). Enter decelerates, exit accelerates.
/// When Windows "Animation effects" is off, everything becomes an instant change.
internal static class Motion
{
    public const double FastMs = 83, NormalMs = 167, SlowMs = 250;

    private static readonly Windows.UI.ViewManagement.UISettings Settings = new();

    /// Windows Settings → Accessibility → Visual effects → Animation effects.
    public static bool Enabled => Settings.AnimationsEnabled;

    /// The signature "settle": a new token or Peek fades in while growing from 96% to 100%.
    public static void Settle(UIElement element) => Enter(element, 0.96f, SlowMs);

    /// A plain fade in (lists, the Board opening).
    public static void FadeIn(UIElement element) => Enter(element, 1f, NormalMs);

    /// Fades out (and shrinks a little), then runs `done`. Instant when animations are off.
    public static void FadeOut(UIElement element, Action? done = null, float toScale = 0.98f)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        if (!Enabled)
        {
            visual.Opacity = 0;
            done?.Invoke();
            return;
        }
        var c = visual.Compositor;
        CenterOn(element, visual);
        var accelerate = c.CreateCubicBezierEasingFunction(new Vector2(1, 0), new Vector2(1, 1));
        var batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation("Opacity", Scalar(c, 1, 0, FastMs * 2, accelerate));
        if (toScale < 1) visual.StartAnimation("Scale", Vector(c, 1, toScale, FastMs * 2, accelerate));
        batch.End();
        batch.Completed += (_, _) => done?.Invoke();
    }

    private static void Enter(UIElement element, float fromScale, double ms)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        if (!Enabled)
        {
            visual.Opacity = 1;
            visual.Scale = Vector3.One;
            return;
        }
        var c = visual.Compositor;
        CenterOn(element, visual);
        var decelerate = c.CreateCubicBezierEasingFunction(new Vector2(0, 0), new Vector2(0, 1));
        visual.StartAnimation("Opacity", Scalar(c, 0, 1, ms, decelerate));
        if (fromScale < 1) visual.StartAnimation("Scale", Vector(c, fromScale, 1, ms, decelerate));
    }

    private static void CenterOn(UIElement element, Visual visual)
    {
        var size = element.ActualSize;
        visual.CenterPoint = new Vector3(size.X / 2, size.Y / 2, 0);
    }

    private static ScalarKeyFrameAnimation Scalar(Compositor c, float from, float to, double ms, CompositionEasingFunction ease)
    {
        var a = c.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(0, from);
        a.InsertKeyFrame(1, to, ease);
        a.Duration = TimeSpan.FromMilliseconds(ms);
        return a;
    }

    private static Vector3KeyFrameAnimation Vector(Compositor c, float from, float to, double ms, CompositionEasingFunction ease)
    {
        var a = c.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(0, new Vector3(from, from, 1));
        a.InsertKeyFrame(1, new Vector3(to, to, 1), ease);
        a.Duration = TimeSpan.FromMilliseconds(ms);
        return a;
    }
}
