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
	private MoveStrip _strip = null!;
	private Button _ringsButton = null!;
	private Button _solveButton = null!;
	private readonly List<Flames> _fires = new();

	/// <summary>True once the computer has finished the puzzle for you (Solve): no stars, no fire.</summary>
	private bool _assisted;

	/// <summary>Animation speed: 1 normally, much quicker while the computer solves a big tower.</summary>
	private float _pace = 1f;

	/// <summary>True while the computer plays out a Solve: taps and menus wait.</summary>
	private bool _solving;

	private bool _demo;
	private bool _quitAfterDemo;
	private string? _clip;
	private string? _openMenu;
	private float _initialSpin;
	private float _demoTimer;

	public const int MinRings = 3;
	public const int MaxRings = 8;

	/// <summary>The ring count, chosen with the Rings button (saved); 4 to start with.</summary>
	private int RingCount
	{
		get => _settings.Extra.TryGetValue("rings", out var saved) && int.TryParse(saved, out int rings)
			? Mathf.Clamp(rings, MinRings, MaxRings) : 4;
		set => _settings.Extra["rings"] = value.ToString();
	}

	/// <summary>Solve costs more ads for bigger towers: 1 up to 4 rings, 2 up to 6, then 3.</summary>
	private static int SolveAds(int rings) => rings <= 4 ? 1 : rings <= 6 ? 2 : 3;

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
			UndoText: "Hint (ad)", Modes: false), _settings);
		_hud.NewGamePressed += OnNewGamePressed;
		_hud.UndoPressed += OnHintPressed;
		_hud.RestartPressed += OnRestartPressed;
		_hud.SetTutorial(Tutorial.Pages);

		// The ring count replaces the usual level button; Solve sits under Hint.
		_ringsButton = _hud.AddButton("", true, Control.LayoutPreset.CenterBottom, -150, 150, -110, -46, 26);
		_ringsButton.Pressed += ChooseRings;
		_solveButton = _hud.AddButton("", false, Control.LayoutPreset.BottomLeft, 20, 180, -110, -46, 22);
		_solveButton.Pressed += OnSolvePressed;

		// The 2ⁿ − 1 strip, under the top buttons, on its own layer so tutorial clips show it too.
		var stripLayer = new CanvasLayer { Layer = 0 };
		AddChild(stripLayer);
		_strip = new MoveStrip(RingColours);
		Widgets.Anchor(_strip, Control.LayoutPreset.TopWide, left: 40, right: -40, top: 214, bottom: 320);
		stripLayer.AddChild(_strip);

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

		_spin = new SpinControl(_rig, () => !_busy && _lifted < 0 && !_settings.FreezeBoard);
		_spin.Tapped += OnTapped;
		if (_clip != null)
		{
			_hud.Visible = false;
			_ = PlayClip(_clip);
			return;
		}
		StartGame();
		if (!_demo)
			_hud.OfferTutorialOnce(_settings);
		_hud.ApplyDevFlags();
		if (_openMenu == "rings")
			ChooseRings();
		else if (_openMenu == "solve")
			OnSolvePressed();
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
		PutOutFire();
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
		if (_busy || _solving || _game.IsSolved)
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
		up.TweenProperty(node, "position:y", LiftHeight, 0.22f * _pace).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
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
		down.TweenProperty(node, "position", Slot(peg, _game.On(peg).Count - 1), 0.22f * _pace).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
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
		over.TweenProperty(node, "position:x", PegX(peg), 0.25f * _pace).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		over.TweenProperty(node, "position", Slot(peg, _game.On(peg).Count - 1), 0.24f * _pace).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
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
	/// step higher, then a fountain of sparks over the top; then, if you solved it yourself, the
	/// whole tower catches fire from the bottom up.
	/// </summary>
	private async Task Finale()
	{
		if (!_assisted)
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
		if (_assisted)
			return;
		for (int level = 0; level < stack.Count; level++)
		{
			var node = _rings[stack[level]];
			float radius = RingRadius(stack[level], _game.Rings);
			_fires.Add(Flames.SetAlight(_rig, node.Position + new Vector3(0, RingHeight * 0.6f, 0), new[] { node.GetChild<MeshInstance3D>(0) },
				radius: radius * 0.8f, size: 0.7f + 0.3f * radius, withLight: level == stack.Count - 1));
			await Wait(0.08f);
		}
	}

	private void PutOutFire()
	{
		foreach (var fire in _fires)
			fire.Extinguish();
		_fires.Clear();
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
		PutOutFire();
		_game = new HanoiGame(RingCount);
		_best = 0;
		_assisted = false;
		_pace = 1f;
		PlaceRings();
		RefreshUi();
	}

	/// <summary>A puzzle against nobody: starting over just asks first, then sets the tower up again.</summary>
	private void OnRestartPressed()
	{
		if (_busy || _solving)
			return;
		if (_game.IsSolved || _game.Moves == 0)
		{
			StartGame();
			return;
		}
		_hud.AskRestart(false, _ => StartGame());
	}

	/// <summary>The Rings button: pick 3 to 8 rings, each shown with its fewest moves.</summary>
	private void ChooseRings()
	{
		if (_busy || _solving)
			return;
		var choices = new List<(string Text, bool Accent, System.Action? Pressed)>();
		for (int rings = MinRings; rings <= MaxRings; rings++)
		{
			int count = rings;
			choices.Add(($"{rings} rings  ·  {(1 << rings) - 1} moves", rings == RingCount, () =>
			{
				RingCount = count;
				_settings.Save();
				StartGame();
			}));
		}
		choices.Add(("Cancel", false, null));
		_hud.Dialog("How many rings?", "Each ring more doubles the fewest moves, plus one.", choices.ToArray());
	}

	private bool CanSolve => !_busy && !_solving && !_game.IsSolved && !_demo;

	/// <summary>Solve (ads): after the ads, the computer finishes the puzzle from wherever you are.</summary>
	private void OnSolvePressed()
	{
		if (!CanSolve)
			return;
		int ads = SolveAds(_game.Rings);
		string watch = ads == 1 ? "Watch a short ad" : $"Watch {ads} short ads";
		_hud.Dialog("Solve it for you?", $"{watch} and the computer finishes the puzzle from here. Bigger towers take more ads.",
			(ads == 1 ? "Watch the ad" : $"Watch {ads} ads", true, () =>
			{
				DevLog.Write($"ad: rewarded series of {ads} (solve)");
				_solveButton.Visible = false;
				_hud.SetUndoVisible(false);
				_ads.ShowRewardedSeries(ads, rewarded: () => _ = AutoSolve(), closed: RefreshUi);
			}),
			("Not now", false, null));
	}

	/// <summary>Plays the perfect solution from here, briskly enough that even 8 rings take seconds.</summary>
	private async Task AutoSolve()
	{
		if (_busy || _game.IsSolved)
			return;
		_assisted = true;
		_solving = true;
		if (_lifted >= 0)
			await Lower();
		int left = _game.MovesLeft;
		_pace = Mathf.Clamp(14f / Mathf.Max(1, left), 0.12f, 1f);
		while (_game.NextBestMove() is { } move)
		{
			await Lift(move.From);
			await MoveTo(move.To);
		}
		_pace = 1f;
		_solving = false;
		RefreshUi();
	}

	private bool CanHint => !_busy && !_solving && !_game.IsSolved;

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

	// ---------------------------------------------------------------- Tutorial clips

	/// <summary>
	/// Plays one scripted scene for the tutorial (with --clip=name; Record-Clips.ps1 records it),
	/// then quits. The HUD is hidden; the move strip stays, so the clips show the count filling up.
	/// </summary>
	private async Task PlayClip(string clip)
	{
		// The clip is square: the strip goes to the very top, clear of the pegs.
		Widgets.Anchor(_strip, Control.LayoutPreset.TopWide, left: 40, right: -40, top: 16, bottom: 120);
		_settings.Muted = false;
		RingCount = clip switch { "move" => 4, "solve" => 3, "auto8" => 8, _ => 6 };
		StartGame();
		await Wait(0.8f);
		switch (clip)
		{
			case "move":
				await Lift(0);
				await Wait(0.3f);
				await MoveTo(1);
				await Wait(0.5f);
				await Lift(0);
				await Wait(0.3f);
				await MoveTo(1); // Not allowed: ring 2 on ring 1 shakes and stays up.
				await Wait(0.7f);
				await MoveTo(2);
				await Wait(0.5f);
				await Lift(1);
				await Wait(0.3f);
				await MoveTo(2);
				await Wait(1.2f);
				break;
			case "solve":
				while (_game.NextBestMove() is { } move)
				{
					await Lift(move.From);
					await MoveTo(move.To);
					await Wait(0.25f);
				}
				await Wait(2.4f);
				break;
			case "auto8":
				// Not a tutorial clip: times the Solve button on the biggest tower.
				await AutoSolve();
				break;
			default:
				// Six rings at speed: 63 moves, the biggest ring once, in the middle.
				_pace = 0.2f;
				while (_game.NextBestMove() is { } move)
				{
					await Lift(move.From);
					await MoveTo(move.To);
				}
				_pace = 1f;
				await Wait(2.4f);
				break;
		}
		GetTree().Quit();
	}

	// ---------------------------------------------------------------- UI

	private void RefreshUi()
	{
		_hud.SetScore($"{_game.Rings} rings   ·   moves {_game.Moves}");
		_strip.Set(_game.Rings, _game.Moves);
		_hud.SetNewGameVisible(_game.IsSolved && !_busy);
		_hud.SetUndoVisible(CanHint);
		_hud.SetRestartVisible(!_game.IsSolved);
		_ringsButton.Text = $"Rings: {RingCount}";
		_ringsButton.Disabled = _solving;
		int ads = SolveAds(_game.Rings);
		_solveButton.Text = ads == 1 ? "Solve (ad)" : $"Solve ({ads} ads)";
		_solveButton.Visible = CanSolve;
		if (_game.IsSolved)
		{
			if (_busy)
				return;
			string stars = new string('★', _game.Stars) + new string('☆', 3 - _game.Stars);
			if (_assisted)
				_hud.SetStatus("Solved for you. Try it yourself next!", new Color("ffcf7a"));
			else
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
	/// `--rings=6`, `--spin=30`, `--clip=solve`, `--menu=rings` or `--menu=solve`. Style flags aren't saved.
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
				case "--spin":
					_initialSpin = Mathf.DegToRad(float.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
					break;
				case "--clip":
					_clip = value;
					break;
				case "--menu":
					_openMenu = value;
					break;
				case "--rings" when int.TryParse(value, out int rings):
					RingCount = rings;
					break;
			}
		}
	}
}
