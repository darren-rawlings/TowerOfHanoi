using Godot;

namespace TowerOfHanoi.Game;

/// <summary>
/// The fewest moves, as the formula: "Fewest moves: 2ⁿ − 1 = 15", with the power drawn small and
/// raised. (It once drew a bar for each move of the perfect solution, but those gave the
/// solution away.)
/// </summary>
public partial class MoveStrip : Control
{
	private const int TextSize = 26;
	private const int PowerSize = 17;

	private int _rings;

	public MoveStrip()
	{
		MouseFilter = MouseFilterEnum.Ignore;
	}

	public void Set(int rings)
	{
		_rings = rings;
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_rings <= 0)
			return;
		var font = GetThemeDefaultFont();
		int total = (1 << _rings) - 1;
		string before = "Fewest moves: 2", power = _rings.ToString(), after = $" − 1 = {total}";
		float wBefore = font.GetStringSize(before, HorizontalAlignment.Left, -1, TextSize).X;
		float wPower = font.GetStringSize(power, HorizontalAlignment.Left, -1, PowerSize).X;
		float wAfter = font.GetStringSize(after, HorizontalAlignment.Left, -1, TextSize).X;
		float x = (Size.X - wBefore - wPower - wAfter) / 2;
		const float baseline = 28;
		foreach (var (offset, colour) in new[] { (new Vector2(2, 2), new Color(0, 0, 0, 0.55f)), (Vector2.Zero, new Color("fff3e0")) })
		{
			DrawString(font, new Vector2(x, baseline) + offset, before, HorizontalAlignment.Left, -1, TextSize, colour);
			DrawString(font, new Vector2(x + wBefore + 1, baseline - 11) + offset, power, HorizontalAlignment.Left, -1, PowerSize, colour);
			DrawString(font, new Vector2(x + wBefore + wPower + 2, baseline) + offset, after, HorizontalAlignment.Left, -1, TextSize, colour);
		}
	}
}
