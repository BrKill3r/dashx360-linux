using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace XboxMetroLauncher.Controls;

public sealed class BladePadGlyph : FrameworkElement
{
    public static readonly StyledProperty<string> LetterProperty = (StyledProperty<string>)DependencyProperty.Register(nameof(Letter),typeof(string),typeof(BladePadGlyph),new FrameworkPropertyMetadata("A",FrameworkPropertyMetadataOptions.AffectsRender));
    public string Letter {get=>(string)GetValue(LetterProperty);set=>SetValue(LetterProperty,value);}
    public override void Render(DrawingContext dc)
    {
        Color tint=Letter switch {"B"=>Color.FromRgb(216,50,26),"X"=>Color.FromRgb(52,130,190),"Y"=>Color.FromRgb(240,215,40),_=>Color.FromRgb(102,181,44)};
        double r=System.Math.Min(Bounds.Width,Bounds.Height)/2;
        if (r <= 1) return;
        var brush=new RadialGradientBrush {Center = new RelativePoint(.4,.25, RelativeUnit.Relative),GradientOrigin = new RelativePoint(.4,.16, RelativeUnit.Relative),RadiusX = new RelativeScalar(.8, RelativeUnit.Relative),RadiusY = new RelativeScalar(.8, RelativeUnit.Relative)};
        brush.GradientStops.Add(new GradientStop(Colors.White,0));brush.GradientStops.Add(new GradientStop(tint,.38));brush.GradientStops.Add(new GradientStop(Color.FromRgb(40,53,30),1));
        dc.DrawEllipse(brush,new Pen(new SolidColorBrush(Color.FromRgb(43,57,36)),1),new Point(r,r),r-1,r-1);
        var text=new FormattedText(Letter,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("avares://DashX360/Fonts#Selawik"),FontStyle.Normal,FontWeights.Bold,FontStretch.Normal),r*1.35,new SolidColorBrush(Color.FromRgb(21,43,26)));
        dc.DrawText(text,new Point(r-text.Width/2,r-text.Height/2-1));
    }
}
