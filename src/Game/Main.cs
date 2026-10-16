using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Renegade.Ads;
using Renegade.Audio;
using Renegade.Core;
using Renegade.Dev;
using Renegade.Looks;
using Renegade.Play;
using Renegade.Ui;
using TowerOfHanoi.Core;

namespace TowerOfHanoi.Game;

/// <summary>
/// Tower of Hanoi. Tap a peg to lift its top ring, then another peg to drop it there. Solve it and
/// the rings light up one by one from the bottom, each with a rising note, in a fountain of sparks
/// (the signature "finale"); stars reward solving in few moves. A hint (rewarded ad) shows the best
/// next move. Drag sideways to spin the board.
/// </summary>
public partial class Main : Node3D
{
	public const string Title = "Tower of Hanoi";

	/// <summary>Google's test ad units until the real ones exist in AdMob (see the release playbook).</summary>
	private static readonly AdMobConfig AdUnits = AdMobConfig.Test;

	private const float PegSpacing = 2.9f;
	private const float RingHeight = 0.3f;
	private const float PegHeight = 3.0f;
	private const float LiftHeight = PegHeight + 0.6f;
	private const float Thickness = 0.35f;

	/// <summary>Ring colours by size, smallest first: a rainbow so each size is easy to tell apart.</summary>
	private static readonly Color[] RingColours =
	{
		new("ef4444"), new("f97316"), new("facc15"), new("22c55e"), new("14b8a6"), new("3b82f6"), new("8b5cf6"), new("ec4899"),
	};

	private readonly Dictionary<int, Node3D> _rings = new();
	private readonly List<MeshInstance3D> _pegGlows = new();
	private readonly ImpactBank<Style> _impacts = new(Styles.Impact);

	private GameSettings _settings = null!;
	private HanoiGame _game = null!;
	private Node3D _rig = null!;
	private Camera3D _camera = null!;
	private SoundPool _sound = null!;
	private GameHud _hud = null!;
	private IAdService _ads = null!;
	private AdSchedule? _adSchedule;
	private SpinControl _spin = null!;
	private MeshInstance3D _base = null!;
	private readonly List<MeshInstance3D> _pegs = new();
	private int _lifted = -1;
	private bool _busy;
	private int _best;

	private bool _demo;
	private bool _quitAfterDemo;
	private float _initialSpin;
	private float _demoTimer;

	private static float PegX(int peg) => (peg - 1) * PegSpacing;

	private static float RingRadius(int ring, int rings) => 0.38f + 0.85f * (ring - 1) / Mathf.Max(1, rings - 1);

	public override void _Ready()
	{
		DevLog.Tag = "han";
		AudioWatch.Attach(this, Title);
		_settings = GameSettings.Load(board: Style.Wood, pieces: Style.Wood);
		ReadDeveloperArgs();
		AudioServer.SetBusMute(0, _settings.Muted);

		_camera = new Camera3D { Position = new Vector3(0, 9.4f, 8.6f), Fov = 50, KeepAspect = Camera3D.KeepAspectEnum.Width };
		AddChild(_camera);
		_camera.LookAt(new Vector3(0, 0.9f, 0.2f));
		Stage.Build(this, -Thickness, new Color("1a0f09"));
		_rig = new Node3D();
		AddChild(_rig);
		_rig.RotateY(_initialSpin);
		BuildBoard();
		Stage.WarmUp(this, -Thickness, Sparkle.Halo);

		_sound = new SoundPool();
		AddChild(_sound);
		_adSchedule ??= new AdSchedule(_settings.GamesSinceAd);
		var theme = HudTheme.Warm;
		_ads = AdServices.Create(this, AdUnits, _demo, new PlaceholderAds
		{
			TextColor = theme.Text,
			RewardTitle = "Watch for a hint",
			ClaimText = "Show me a move",
			SkipText = "Skip (no hint)",
		});

		_hud = new GameHud(theme);
		AddChild(_hud);
		_hud.Build(new HudOptions(Title, "Tap a peg, then where to put its ring", PiecesLabel: "Rings",
			PieceName: s => s switch { Style.Plastic => "Plastic", Style.Wood => "Wood", Style.Metal => "Metal", Style.Glass => "Glass", _ => "Marble" },
			UndoText: "Hint (ad)", LevelOnly: true),
			_settings.Muted, GameMode.PlayerVsComputer, _settings.Difficulty, _settings.Board, _settings.Pieces);
		_hud.NewGamePressed += OnNewGamePressed;
		_hud.UndoPressed += OnHintPressed;
		_hud.MuteToggled += muted =>
		{
			_settings.Muted = muted;
			AudioServer.SetBusMute(0, muted);
			_settings.Save();
		};
		_hud.ModeChosen += (_, difficulty) =>
		{
			_settings.Difficulty = difficulty;
			_settings.Save();
			StartGame();
		};
		_hud.BoardChosen += style =>
		{
			_settings.Board = style;
			_settings.Save();
			var material = Materials.For(Styles.BoardLook(style));
			_base.MaterialOverride = material;
			foreach (var peg in _pegs)
				peg.MaterialOverride = material;
		};
		_hud.PiecesChosen += style =>
		{
			_settings.Pieces = style;
			_settings.Save();
			PlaceRings();
		};

		_spin = new SpinControl(_rig, () => !_busy && _lifted < 0);
		_spin.Tapped += OnTapped;
		StartGame();
	}

	// ---------------------------------------------------------------- World

	private void BuildBoard()
	{
		var material = Materials.For(Styles.BoardLook(_settings.Board));
		_base = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(PegSpacing * 3 + 0.6f, Thickness, 3.2f) }, Position = new Vector3(0, -Thickness / 2, 0), MaterialOverride = material };
		_rig.AddChild(_base);
		var glow = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.78f, 0.35f, 0.4f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		for (int peg = 0; peg < HanoiGame.Pegs; peg++)
		{
			var post = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.15f, Height = PegHeight, RadialSegments = 20 }, Position = new Vector3(PegX(peg), PegHeight / 2, 0), MaterialOverride = material };
			_rig.AddChild(post);
			_pegs.Add(post);
			var ring = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1.35f, BottomRadius = 1.35f, Height = 0.01f, RadialSegments = 40 }, MaterialOverride = glow, Position = new Vector3(PegX(peg), 0.006f, 0), Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			_rig.AddChild(ring);
			_pegGlows.Add(ring);
		}
	}

	private Look RingLook(int ring)
	{
		var c = RingColours[(ring - 1) % RingColours.Length];
		return _settings.Pieces switch
		{
			Style.Glass => new Look(Look.GlassPattern, c, c.Lightened(0.4f), 0.04f, Opacity: 0.72f),
			Style.Metal => new Look(Look.MetalPattern, c, c.Darkened(0.35f), 0.25f, Metallic: 1f),
			Style.Marble => new Look(Look.MarblePattern, c, c.Darkened(0.4f), 0.15f, Scale: 1.5f),
			Style.Wood => new Look(Look.WoodPattern, c, c.Darkened(0.4f), 0.5f, Scale: 2f),
			_ => new Look(Look.PlasticPattern, c, c.Darkened(0.2f), 0.25f),
		};
	}

	/// <summary>A thick ring: a short tube, so it reads as a stacking ring and sits flat.</summary>
	private Node3D MakeRing(int ring)
	{
		float radius = RingRadius(ring, _game.Rings);
		var node = new Node3D();
		node.AddChild(new MeshInstance3D
		{
			Mesh = new TorusMesh { InnerRadius = 0.17f, OuterRadius = radius, Rings = 48, RingSegments = 16 },
			Scale = new Vector3(1, RingHeight / ((radius - 0.17f) * 1.0f), 1),
			Position = new Vector3(0, RingHeight / 2, 0),
			MaterialOverride = Materials.For(RingLook(ring)),
		});
		_rig.AddChild(node);
		return node;
	}

	private static Vector3 Slot(int peg, int level) => new(PegX(peg), level * RingHeight, 0);

	/// <summary>Puts every ring where the rules have it (start of a game, or a finish change).</summary>
	private void PlaceRings()
	{
		foreach (var ring in _rings.Values)
			ring.QueueFree();
		_rings.Clear();
		for (int peg = 0; peg < HanoiGame.Pegs; peg++)
		{
			var stack = _game.On(peg);
			for (int level = 0; level < stack.Count; level++)
			{
				var node = MakeRing(stack[level]);
				node.Position = Slot(peg, level);
				_rings[stack[level]] = node;
			}
		}
		_lifted = -1;
	}

	// ---------------------------------------------------------------- Input

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!_hud.MenuOpen)
			_spin.HandleInput(@event);
	}

	private void OnTapped(Vector2 screen)
	{
		if (_busy || _game.IsSolved)
			return;
		int peg = PegAt(screen);
		if (peg < 0)
			return;
		_hud.HideHint();
		if (_lifted < 0)
			_ = Lift(peg);
		else if (peg == _lifted)
			_ = Lower();
		else
			_ = MoveTo(peg);
	}

	/// <summary>The peg nearest a tap, judged on the board plane and at ring height (tall pegs are easy to hit).</summary>
	private int PegAt(Vector2 screen)
	{
		var from = _camera.ProjectRayOrigin(screen);
		var direction = _camera.ProjectRayNormal(screen);
		int best = -1;
		float bestDistance = 1.5f;
		foreach (float height in new[] { 0f, 0.8f, 1.6f, 2.4f })
		{
			if (Mathf.Abs(direction.Y) < 0.001f)
				continue;
			var local = _rig.ToLocal(from + direction * ((height - from.Y) / direction.Y));
			for (int peg = 0; peg < HanoiGame.Pegs; peg++)
			{
				float distance = new Vector2(local.X - PegX(peg), local.Z).Length();
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = peg;
				}
			}
		}
		return best;
	}

	// ---------------------------------------------------------------- Moves

	private async Task Lift(int peg)
	{
		if (_game.Top(peg) is not { } ring)
			return;
		_busy = true;
		_lifted = peg;
		var node = _rings[ring];
		var up = CreateTween();
		up.TweenProperty(node, "position:y", LiftHeight, 0.22f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		await ToSignal(up, Tween.SignalName.Finished);
		_busy = false;
		RefreshUi();
	}

	private async Task Lower()
	{
		int peg = _lifted;
		var node = _rings[_game.Top(peg)!.Value];
		_busy = true;
		var down = CreateTween();
		down.TweenProperty(node, "position", Slot(peg, _game.On(peg).Count - 1), 0.22f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		await ToSignal(down, Tween.SignalName.Finished);
		Clack(node.GlobalPosition, 0.5f);
		_lifted = -1;
		_busy = false;
		RefreshUi();
	}

	private async Task MoveTo(int peg)
	{
		int from = _lifted;
		var node = _rings[_game.Top(from)!.Value];
		_busy = true;
		if (!_game.CanMove(from, peg))
		{
			// Not allowed: a quick shake over the peg it's on, and it stays lifted.
			var shake = CreateTween();
			float x = node.Position.X;
			foreach (float dx in new[] { 0.15f, -0.15f, 0.1f, 0f })
				shake.TweenProperty(node, "position:x", x + dx, 0.05f);
			await ToSignal(shake, Tween.SignalName.Finished);
			_hud.SetStatus("A ring can't go on a smaller one", Colors.White);
			_busy = false;
			return;
		}
		_game.Move(from, peg);
		DevLog.Write($"move {from}->{peg}, {_game.Moves} moves");
		var over = CreateTween();
		over.TweenProperty(node, "position:x", PegX(peg), 0.25f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		over.TweenProperty(node, "position", Slot(peg, _game.On(peg).Count - 1), 0.24f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		await ToSignal(over, Tween.SignalName.Finished);
		Clack(node.GlobalPosition, 0.8f);
		_lifted = -1;
		if (_game.IsSolved)
			await Finale();
		_busy = false;
		RefreshUi();
	}

	/// <summary>
	/// The signature: the finished tower lights up ring by ring from the bottom, each with a note a
	/// step higher, then a fountain of sparks over the top.
	/// </summary>
	private async Task Finale()
	{
		_best = _best == 0 ? _game.Moves : Mathf.Min(_best, _game.Moves);
		var stack = _game.On(HanoiGame.Target);
		for (int level = 0; level < stack.Count; level++)
		{
			var node = _rings[stack[level]];
			var mesh = node.GetChild<MeshInstance3D>(0);
			mesh.MaterialOverlay = Sparkle.Halo;
			var pop = CreateTween();
			pop.TweenProperty(node, "scale", Vector3.One * 1.12f, 0.08f);
			pop.TweenProperty(node, "scale", Vector3.One, 0.12f);
			_sound.Play(_impacts.Random(Style.Glass), node.GlobalPosition, 0f, 0.8f + level * 0.1f);
			await Wait(0.12f);
		}
		Sparkle.Burst(_rig, Slot(HanoiGame.Target, stack.Count) + new Vector3(0, 0.3f, 0));
		Sparkle.Burst(_rig, Slot(HanoiGame.Target, stack.Count) + new Vector3(0, 0.6f, 0));
		await Wait(0.5f);
		foreach (var node in _rings.Values)
			node.GetChild<MeshInstance3D>(0).MaterialOverlay = null;
	}

	private void Clack(Vector3 at, float strength)
	{
		_sound.PlayImpact(_impacts.Random(_settings.Pieces), at, strength, Styles.GainDb(_settings.Pieces));
		_sound.PlayImpact(_impacts.Random(_settings.Board), at, strength * 0.5f, Styles.GainDb(_settings.Board));
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_spin.Process(dt);
		if (!_demo || _busy || _game.IsSolved)
			return;
		// Demo: follow the hints, quickly.
		_demoTimer += dt;
		if (_demoTimer < 0.1f)
			return;
		_demoTimer = 0f;
		if (_game.NextBestMove() is not { } move)
			return;
		if (_lifted < 0)
			_ = Lift(move.From);
		else
			_ = MoveTo(move.To);
	}

	// ---------------------------------------------------------------- Games, hints, ads

	private void OnNewGamePressed()
	{
		if (_busy)
			return;
		bool adDue = _adSchedule!.GameFinished();
		_settings.GamesSinceAd = _adSchedule.GamesSinceAd;
		_settings.Save();
		if (adDue)
		{
			DevLog.Write("ad: between-games interstitial");
			_ads.ShowInterstitial(StartGame);
		}
		else
		{
			StartGame();
		}
	}

	private void StartGame()
	{
		_game = new HanoiGame(HanoiGame.RingsFor(_settings.Difficulty));
		_best = 0;
		PlaceRings();
		RefreshUi();
	}

	private bool CanHint => !_busy && !_game.IsSolved;

	private void OnHintPressed()
	{
		if (!CanHint)
			return;
		DevLog.Write("ad: rewarded (hint)");
		_ads.ShowRewarded(rewarded: ShowHint, closed: RefreshUi);
	}

	/// <summary>Lights the best next move: the peg to take from, then the peg to put it on.</summary>
	private async void ShowHint()
	{
		if (_game.NextBestMove() is not { } move)
			return;
		if (_lifted >= 0 && _lifted != move.From)
			await Lower();
		foreach (var (peg, delay) in new[] { (move.From, 0.0), (move.To, 0.6) })
		{
			GetTree().CreateTimer(delay).Timeout += () =>
			{
				_pegGlows[peg].Visible = true;
				GetTree().CreateTimer(1.2).Timeout += () => _pegGlows[peg].Visible = false;
			};
		}
		_hud.SetStatus($"Move the top ring from the {PegName(move.From)} peg to the {PegName(move.To)}", new Color("ffcf7a"));
	}

	private static string PegName(int peg) => peg switch { 0 => "left", 1 => "middle", _ => "right" };

	// ---------------------------------------------------------------- UI

	private void RefreshUi()
	{
		_hud.SetScore($"{_game.Rings} rings   ·   moves {_game.Moves}   ·   best possible {_game.Minimum}");
		_hud.SetNewGameVisible(_game.IsSolved && !_busy);
		_hud.SetUndoVisible(CanHint);
		if (_game.IsSolved)
		{
			if (_busy)
				return;
			string stars = new string('★', _game.Stars) + new string('☆', 3 - _game.Stars);
			_hud.SetStatus($"Solved in {_game.Moves}!  {stars}", new Color("f5c542"));
			if (_demo && _quitAfterDemo)
				GetTree().CreateTimer(1.5).Timeout += () => GetTree().Quit();
			return;
		}
		_hud.SetStatus(_lifted >= 0 ? "Now tap where to put it" : "Move the tower to the right peg", new Color("fff3e0"));
	}

	private SignalAwaiter Wait(float seconds) => ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

	// ---------------------------------------------------------------- Developer flags

	/// <summary>
	/// Developer flags after `--` (or in user://dev_args.txt on a phone): `--log`, `--demo` (solves
	/// on its own), `--quit-after-demo`, `--games-since-ad=4`, `--board=Wood`, `--pieces=Wood`,
	/// `--difficulty=Hard`, `--spin=30`. Style flags aren't saved.
	/// </summary>
	private void ReadDeveloperArgs()
	{
		foreach (var (name, value) in DevArgs.Read())
		{
			switch (name)
			{
				case "--log":
					DevLog.Enabled = true;
					break;
				case "--demo":
					_demo = true;
					_settings.Muted = false;
					break;
				case "--quit-after-demo":
					_quitAfterDemo = true;
					break;
				case "--games-since-ad":
					_adSchedule = new AdSchedule(int.Parse(value));
					break;
				case "--board" when System.Enum.TryParse(value, out Style board):
					_settings.Board = board;
					break;
				case "--pieces" when System.Enum.TryParse(value, out Style pieces):
					_settings.Pieces = pieces;
					break;
				case "--difficulty" when System.Enum.TryParse(value, out Difficulty difficulty):
					_settings.Difficulty = difficulty;
					break;
				case "--spin":
					_initialSpin = Mathf.DegToRad(float.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
					break;
			}
		}
	}
}
