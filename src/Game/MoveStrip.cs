using Godot;

namespace TowerOfHanoi.Game;

/// <summary>
/// The fewest moves, drawn: one bar for each move of the perfect solution, 2ⁿ − 1 of them, as tall
/// as the ring that moves. The biggest ring moves once, in the middle; either side of it is the
/// whole solution for one ring fewer, and so on down, so the doubling is there to see. Bars light
/// up as you make moves.
/// </summary>
public partial class MoveStrip : Control
{
	private const int TextSize = 26;
	private const int PowerSize = 17;
	private const float BarsTop = 44;

	private readonly Color[] _colours;
	private int _rings;
	private int _moves;

	public MoveStrip(Color[] colours)
	{
		_colours = colours;
		MouseFilter = MouseFilterEnum.Ignore;
	}

	public void Set(int rings, int moves)
	{
		_rings = rings;
		_moves = moves;
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_rings <= 0)
			return;
		var font = GetThemeDefaultFont();
		int total = (1 << _rings) - 1;
		var text = new Color("fff3e0");
		var shadow = new Color(0, 0, 0, 0.55f);

		// "Fewest moves: 2ⁿ − 1 = 15", with the power drawn small and raised.
		string before = "Fewest moves: 2", power = _rings.ToString(), after = $" − 1 = {total}";
		float wBefore = font.GetStringSize(before, HorizontalAlignment.Left, -1, TextSize).X;
		float wPower = font.GetStringSize(power, HorizontalAlignment.Left, -1, PowerSize).X;
		float wAfter = font.GetStringSize(after, HorizontalAlignment.Left, -1, TextSize).X;
		float x = (Size.X - wBefore - wPower - wAfter) / 2;
		const float baseline = 28;
		foreach (var (offset, colour) in new[] { (new Vector2(2, 2), shadow), (Vector2.Zero, text) })
		{
			DrawString(font, new Vector2(x, baseline) + offset, before, HorizontalAlignment.Left, -1, TextSize, colour);
			DrawString(font, new Vector2(x + wBefore + 1, baseline - 11) + offset, power, HorizontalAlignment.Left, -1, PowerSize, colour);
			DrawString(font, new Vector2(x + wBefore + wPower + 2, baseline) + offset, after, HorizontalAlignment.Left, -1, TextSize, colour);
		}

		float height = Size.Y - BarsTop;
		float width = Size.X / total;
		float gap = width > 5 ? 1.5f : 0;
		for (int move = 1; move <= total; move++)
		{
			// Move k of the perfect solution moves ring (trailing zeros of k) + 1.
			int ring = System.Numerics.BitOperations.TrailingZeroCount(move) + 1;
			float h = height * (0.25f + 0.75f * ring / _rings);
			var colour = _colours[(ring - 1) % _colours.Length] with { A = move <= _moves ? 1f : 0.32f };
			DrawRect(new Rect2((move - 1) * width + gap / 2, BarsTop + height - h, width - gap, h), colour);
		}
	}
}
