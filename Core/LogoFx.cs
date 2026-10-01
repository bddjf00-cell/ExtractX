using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ExtractX.Core;

/// <summary>El logo X gira mientras se extrae o comprime.</summary>
public static class LogoFx
{
    public static void Spin(FrameworkElement el)
    {
        try
        {
            var rt = new RotateTransform(0);
            el.RenderTransform = rt;
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            var anim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            rt.BeginAnimation(RotateTransform.AngleProperty, anim);
        }
        catch { }
    }

    public static void Stop(FrameworkElement el)
    {
        try
        {
            if (el.RenderTransform is RotateTransform rt)
                rt.BeginAnimation(RotateTransform.AngleProperty, null);
            el.RenderTransform = new RotateTransform(0);
        }
        catch { }
    }
}
