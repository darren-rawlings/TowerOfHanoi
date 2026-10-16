using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Core;

namespace TowerOfHanoi.Core;

/// <summary>
/// The Tower of Hanoi: move every ring from the left peg to the right one, one ring at a time,
/// never putting a ring on a smaller one. Rings are numbered by size, 1 the smallest. The fewest
/// moves possible is 2^n − 1; <see cref="NextBestMove"/> finds the best move from any position,
/// for hints.
/// </summary>
public sealed class HanoiGame
{
	public const int Pegs = 3;
	public const int Target = 2;

	private readonly List<int>[] _pegs = { new(), new(), new() };
	private readonly Stack<(int From, int To)> _history = new();

	public HanoiGame(int rings)
	{
		Rings = rings;
		for (int ring = rings; ring >= 1; ring--)
			_pegs[0].Add(ring);
	}

	public static int RingsFor(Difficulty difficulty) => difficulty switch
	{
		Difficulty.Easy => 4,
		Difficulty.Medium => 6,
		_ => 8,
	};

	public int Rings { get; }

	public int Moves => _history.Count;

	public int Minimum => (1 << Rings) - 1;

	/// <summary>The rings on a peg, bottom first.</summary>
	public IReadOnlyList<int> On(int peg) => _pegs[peg];

	public int? Top(int peg) => _pegs[peg].Count == 0 ? null : _pegs[peg][^1];

	public bool IsSolved => _pegs[Target].Count == Rings;

	/// <summary>3 stars for the fewest moves, 2 within half as many again, otherwise 1.</summary>
	public int Stars => !IsSolved ? 0 : Moves <= Minimum ? 3 : Moves <= Minimum * 3 / 2 ? 2 : 1;

	public bool CanMove(int from, int to) =>
		!IsSolved && from != to && Top(from) is { } ring && (Top(to) is not { } under || under > ring);

	public int Move(int from, int to)
	{
		if (!CanMove(from, to))
			throw new InvalidOperationException($"Can't move from {from} to {to}.");
		int ring = _pegs[from][^1];
		_pegs[from].RemoveAt(_pegs[from].Count - 1);
		_pegs[to].Add(ring);
		_history.Push((from, to));
		return ring;
	}

	public bool Undo()
	{
		if (_history.Count == 0)
			return false;
		var (from, to) = _history.Pop();
		int ring = _pegs[to][^1];
		_pegs[to].RemoveAt(_pegs[to].Count - 1);
		_pegs[from].Add(ring);
		return true;
	}

	private int PegOf(int ring) => Array.FindIndex(_pegs, peg => peg.Contains(ring));

	/// <summary>
	/// The best next move from here, or null when solved. The largest ring not yet on the target
	/// must get there; first everything smaller has to move to the third peg out of its way.
	/// </summary>
	public (int From, int To)? NextBestMove()
	{
		if (IsSolved)
			return null;
		return Next(Rings, Target);
	}

	private (int From, int To)? Next(int ring, int target)
	{
		if (ring == 0)
			return null;
		int at = PegOf(ring);
		if (at == target)
			return Next(ring - 1, target);
		int spare = 3 - at - target;
		// Smaller rings must all be on the spare peg before this one can move.
		var clear = Next(ring - 1, spare);
		return clear ?? (at, target);
	}
}
