namespace GrayMatch;

public class MatchResult
{
    public int Index { get; set; }
    public double Score { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double Angle { get; set; }
    public int TemplateWidth { get; set; }
    public int TemplateHeight { get; set; }
    public int LeftTopX { get; set; }
    public int LeftTopY { get; set; }
    public int Level { get; set; }

    public override string ToString()
        => $"#{Index} Score={Score:F4} Center=({CenterX:F2},{CenterY:F2}) Angle={Angle:F1} Size={TemplateWidth}x{TemplateHeight}";
}
