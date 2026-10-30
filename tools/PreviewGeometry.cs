#:project ../spikes/Common/Common.csproj
#:property TargetFramework=net10.0-windows
#:property EnableWindowsTargeting=true
// Genera SVG de la geometría COMPARTIDA (reposo / hover / oculto) a partir de DockScene. NO es una captura de ningún prototipo:
// es la intención geométrica que los tres prototipos deben dibujar. Uso: dotnet run tools/PreviewGeometry.cs -- docs/img
using System.Globalization;
using System.Text;
using WinMac.Spikes.Common;

string dir = args.Length > 0 ? args[0] : "docs/img";
Directory.CreateDirectory(dir);
var ci = CultureInfo.InvariantCulture;
string F(double v) => v.ToString("0.##", ci);

string Svg(double? cursor, double hideT)
{
    var f = DockScene.Layout(cursor);
    double W = DockScene.WindowWidth, H = DockScene.WindowHeight;
    double off = (W - f.BarWidth) / 2, barBottom = H - DockScene.ShadowMargin, barTop = barBottom - DockScene.BarHeight;
    var sb = new StringBuilder();
    sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {F(W)} {F(H)}' width='{F(W)}' height='{F(H)}'>");
    sb.Append("<defs><linearGradient id='bar' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='#fff' stop-opacity='.47'/><stop offset='1' stop-color='#fff' stop-opacity='.22'/></linearGradient>");
    sb.Append("<linearGradient id='bd' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='#fff' stop-opacity='.75'/><stop offset='1' stop-color='#fff' stop-opacity='.15'/></linearGradient>");
    sb.Append("<filter id='sh' x='-20%' y='-60%' width='140%' height='260%'><feGaussianBlur stdDeviation='12'/></filter>");
    for (int i = 0; i < DockScene.Items.Length; i++)
    {
        var it = DockScene.Items[i];
        sb.Append($"<linearGradient id='g{i}' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='rgb({Math.Min(255, it.R + 70)},{Math.Min(255, it.G + 70)},{Math.Min(255, it.B + 70)})'/><stop offset='1' stop-color='rgb({it.R},{it.G},{it.B})'/></linearGradient>");
    }
    sb.Append("</defs>");
    sb.Append($"<rect width='{F(W)}' height='{F(H)}' fill='#5b7fa6'/>"); // fondo neutro de referencia
    sb.Append($"<g transform='translate(0 {F(hideT * DockScene.HideDistance)})'>");
    sb.Append($"<rect x='{F(off)}' y='{F(barTop + 10)}' width='{F(f.BarWidth)}' height='{F(DockScene.BarHeight)}' rx='{F(DockScene.BarRadius)}' fill='#000' opacity='.28' filter='url(#sh)'/>");
    sb.Append($"<rect x='{F(off)}' y='{F(barTop)}' width='{F(f.BarWidth)}' height='{F(DockScene.BarHeight)}' rx='{F(DockScene.BarRadius)}' fill='url(#bar)' stroke='url(#bd)'/>");
    sb.Append($"<line x1='{F(off + f.SeparatorX)}' x2='{F(off + f.SeparatorX)}' y1='{F(barTop + DockScene.BarHeight * .2)}' y2='{F(barTop + DockScene.BarHeight * .8)}' stroke='#fff' stroke-opacity='.35'/>");
    var pts = Squircle.Unit();
    for (int i = 0; i < DockScene.Items.Length; i++)
    {
        double s = f.Size[i], x = off + f.X[i], y = barBottom - DockScene.PadBottom - s;
        string d = "M" + string.Join("L", pts.Select(p => $"{F(x + p.X * s)},{F(y + p.Y * s)}")) + "Z";
        sb.Append($"<path d='{d}' fill='url(#g{i})' stroke='#fff' stroke-opacity='.35'/>");
        sb.Append($"<text x='{F(x + s / 2)}' y='{F(y + s * .63)}' font-family='Segoe UI,sans-serif' font-weight='600' font-size='{F(s * .42)}' fill='#fff' text-anchor='middle'>{DockScene.Items[i].Glyph}</text>");
        if (DockScene.Items[i].IsOpen) sb.Append($"<circle cx='{F(x + s / 2)}' cy='{F(barBottom - DockScene.PadBottom + 6)}' r='{F(DockScene.DotSize / 2)}' fill='#fff' fill-opacity='.82'/>");
    }
    sb.Append("</g></svg>");
    return sb.ToString();
}
File.WriteAllText(Path.Combine(dir, "geometria-reposo.svg"), Svg(null, 0));
File.WriteAllText(Path.Combine(dir, "geometria-hover.svg"), Svg(DockScene.PadX + 5 * (DockScene.BaseSize + DockScene.Gap) + DockScene.BaseSize / 2, 0));
File.WriteAllText(Path.Combine(dir, "geometria-oculto.svg"), Svg(null, 1));
Console.WriteLine($"OK -> {dir} (ventana {DockScene.WindowWidth:0}x{DockScene.WindowHeight:0} DIP)");
