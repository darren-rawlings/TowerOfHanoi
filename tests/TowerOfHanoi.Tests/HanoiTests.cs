using TowerOfHanoi.Core;
using Xunit;

namespace TowerOfHanoi.Tests;

public class HanoiTests
{
	[Fact]
	public void StartsWithEveryRingOnTheLeftPeg()
	{
		var game = new HanoiGame(4);
		Assert.Equal(new[] { 4, 3, 2, 1 }, game.On(0));
		Assert.Equal(15, game.Minimum);
	}

	[Fact]
	public void ABiggerRingCantGoOnASmallerOne()
	{
		var game = new HanoiGame(3);
		game.Move(0, 1);
		Assert.False(game.CanMove(0, 1));
		Assert.True(game.CanMove(0, 2));
	}

	[Theory]
	[InlineData(3)]
	[InlineData(6)]
	[InlineData(8)]
	public void FollowingTheHintsSolvesInTheFewestMoves(int rings)
	{
		var game = new HanoiGame(rings);
		while (game.NextBestMove() is { } move)
			game.Move(move.From, move.To);
		Assert.True(game.IsSolved);
		Assert.Equal(game.Minimum, game.Moves);
		Assert.Equal(3, game.Stars);
	}

	[Fact]
	public void HintsRecoverFromAMessyPosition()
	{
		var game = new HanoiGame(5);
		game.Move(0, 2); game.Move(0, 1); game.Move(2, 1); game.Move(0, 2); game.Move(1, 0);
		int guard = 0;
		while (game.NextBestMove() is { } move && guard++ < 200)
			game.Move(move.From, move.To);
		Assert.True(game.IsSolved);
	}

	[Fact]
	public void MovesLeftCountsThePerfectFinishFromAnywhere()
	{
		var game = new HanoiGame(5);
		Assert.Equal(31, game.MovesLeft);
		game.Move(0, 2); game.Move(0, 1); game.Move(2, 1); game.Move(0, 2); game.Move(1, 0);
		int expected = game.MovesLeft;
		int made = 0;
		while (game.NextBestMove() is { } move)
		{
			Assert.Equal(expected - made, game.MovesLeft);
			game.Move(move.From, move.To);
			made++;
		}
		Assert.Equal(expected, made);
		Assert.Equal(0, game.MovesLeft);
	}

	[Fact]
	public void UndoPutsTheRingBack()
	{
		var game = new HanoiGame(3);
		game.Move(0, 2);
		Assert.True(game.Undo());
		Assert.Equal(new[] { 3, 2, 1 }, game.On(0));
		Assert.Equal(0, game.Moves);
	}
}
