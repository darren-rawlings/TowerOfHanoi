using System.Collections.Generic;
using Renegade.Ui;

namespace TowerOfHanoi.Game;

/// <summary>The guided tour. Clips are recorded from the game with --clip (see Main.PlayClip).</summary>
public static class Tutorial
{
	public static readonly IReadOnlyList<TutorialPage> Pages = new[]
	{
		new TutorialPage("Move the tower",
			"Move the whole tower from the left peg to the right peg. Choose how many rings with the Rings button: 3 to 8.",
			"res://tutorial/solve.ogv"),
		new TutorialPage("One ring at a time",
			"Tap a peg to lift its top ring, then tap the peg to put it on. A ring can never sit on a smaller one: try, and it shakes and stays up.",
			"res://tutorial/move.ogv"),
		new TutorialPage("The fewest moves: 2ⁿ − 1",
			"To move n rings, move the n − 1 above the biggest out of the way, move the biggest once, then bring the others back on top. Each ring more doubles the work, plus one: 3 rings take 7 moves, 4 take 15, 8 take 255.",
			"res://tutorial/double.ogv"),
		new TutorialPage("The strip",
			"The bars at the top are the perfect solution, one bar per move, as tall as the ring that moves. The biggest ring's single move is the tall bar in the middle. They light up as you go.",
			"res://tutorial/double.ogv"),
		new TutorialPage("Hints and Solve",
			"Hint (ad) shows the best next move. Solve (ads) finishes the puzzle for you: 1 ad up to 4 rings, 2 up to 6, 3 for more. Solve it yourself in the fewest moves for three stars, and the tower catches fire.",
			"res://tutorial/solve.ogv"),
	};
}
