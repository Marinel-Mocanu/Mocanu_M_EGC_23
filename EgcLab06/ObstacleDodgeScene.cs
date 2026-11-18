using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// Tema 6 — Varianta B: Evită obstacolele pe un drum cu 3 benzi.
/// </summary>
public sealed class ObstacleDodgeScene : Scene
{
    // --- Constante de geometrie și gameplay -----------------------------------
    private const float LaneWidth = 1.6f;           // distanța dintre centrele a două benzi
    private const float RoadWidth = LaneWidth * 3.0f;
    private const float RoadLength = 120.0f;
    private const float RoadCenterZ = -50.0f;

    private const float DashLength = 1.5f;
    private const float DashCycle = 3.0f;           // 1.5 galben + 1.5 negru
    private const int DashPairsPerLine = 38;

    private const float PlayerSize = 0.9f;
    private const float PlayerZ = 0.0f;
    private const float LaneSwitchDuration = 0.14f; // < 0.2 s (cerință)

    private const float JumpVelocity = 9.5f;
    private const float Gravity = 26.0f;

    private const float InitialSpeed = 14.0f;
    private const float SpeedAcceleration = 0.45f;
    private const float MaxSpeed = 36.0f;

    private const int MaxObstacles = 20;

    // --- Culori (aliniate cu imaginea de referință) --------------------------
    private static readonly Color4 RoadColor = new(0.24f, 0.26f, 0.30f, 1.0f);
    private static readonly Color4 DashLightColor = new(0.86f, 0.83f, 0.58f, 1.0f);
    private static readonly Color4 DashDarkColor = new(0.11f, 0.12f, 0.15f, 1.0f);
    private static readonly Color4 PlayerColor = new(0.22f, 0.72f, 0.85f, 1.0f);
    private static readonly Color4 BeaconColor = new(0.75f, 0.95f, 1.00f, 1.0f);
    private static readonly Color4 TallObstacleColor = new(0.68f, 0.20f, 0.46f, 1.0f);
    private static readonly Color4 LowObstacleColor = new(0.86f, 0.34f, 0.58f, 1.0f);

    // --- Structuri interne -----------------------------------------------------
    private sealed class DashSegment
    {
        public SceneObject Obj = null!;
        public float X;
        public float BaseZ;
    }

    private sealed class ObstacleSlot
    {
        public SceneObject Obj = null!;
        public bool Active;
        public int Lane;        // -1 (stânga), 0 (centru), +1 (dreapta)
        public float Z;
        public bool IsTall;     // true = înalt (se ocolește), false = jos (se sare)
    }

    // --- Obiecte de scenă și animații ------------------------------------------
    private SceneObject _player = null!;
    private SceneObject _beacon = null!;
    private readonly List<DashSegment> _dashes = new();
    private readonly List<ObstacleSlot> _obstaclePool = new();

    private Tween _laneTween = null!;
    private KeyframeTrack _beaconTrack = null!;
    private readonly Random _rng = new();

    // --- Starea jocului --------------------------------------------------------
    private int _currentLane;
    private float _playerY;
    private float _velocityY;
    private float _beaconTime;

    private float _roadScroll;
    private float _spawnTimer;
    private float _worldSpeed;
    private float _survivedTime;
    private float _bestTime;
    private bool _gameOver;

    // --- Proprietăți și metode suprascrise din Scene ---------------------------
    public override string Name => "Evită obstacolele (3 benzi)";

    public override IEnumerable<string> ActionNames => new[]
    {
        "Stânga (A / ←) — schimbă pe banda din stânga",
        "Dreapta (D / →) — schimbă pe banda din dreapta",
        "Săritură (Space) — sare peste obstacolele joase",
        "Joc nou (R) — repornește partida"
    };

    public override string Status => _gameOver
        ? $"GAME OVER | Scor: {_survivedTime:F1}s | Record: {_bestTime:F1}s | Apasă R pentru joc nou"
        : $"Scor: {_survivedTime:F1}s | Record: {_bestTime:F1}s | Viteză: {_worldSpeed:F1} u/s";

    public override IEnumerable<string> Describe()
    {
        string laneText = _currentLane switch
        {
            -1 => "Stânga (-1)",
            0 => "Centru (0)",
            _ => "Dreapta (+1)"
        };

        int activeObstacles = 0;
        foreach (ObstacleSlot slot in _obstaclePool)
        {
            if (slot.Active) activeObstacles++;
        }

        yield return $"Stare          : {(_gameOver ? "Oprit (Coliziune)" : "În desfășurare")}";
        yield return $"Scor / Record  : {_survivedTime:F2} s / {_bestTime:F2} s";
        yield return $"Viteză lume    : {_worldSpeed:F2} unități/s";
        yield return $"Jucător        : banda {laneText}, înălțime = {_playerY:F2} m";
        yield return $"Obstacole      : {activeObstacles} active";
    }

    public override void Load(Input input)
    {
        // 1. Camera fixă în spatele jucătorului, privind în lungul drumului (-Z)
        CameraPosition = new Vector3(0.0f, 3.2f, 5.5f);
        CameraTarget = new Vector3(0.0f, 0.8f, -18.0f);

        // 2. Legarea acțiunilor de input (fără citire directă din KeyboardState)
        input.Bind("Stânga", Keys.Left, Keys.A);
        input.Bind("Dreapta", Keys.Right, Keys.D);
        input.Bind("Săritură", Keys.Space);
        input.Bind("Joc nou", Keys.R);

        // 3. Forma de bază (înregistrată prin AddMesh pentru eliberare automată)
        Mesh cubeMesh = AddMesh(Shapes.Cube());

        // 4. Drumul cu 3 benzi
        SceneObject road = AddObject("Drum", cubeMesh);
        road.Transform.Position = new Vector3(0.0f, -0.05f, RoadCenterZ);
        road.Transform.Scale = new Vector3(RoadWidth, 0.1f, RoadLength);
        road.Tint = RoadColor;

        // 5. Marcajele rutiere dintre benzi (galben-crem alternat cu negru)
        float[] dividerX = { -LaneWidth * 0.5f, LaneWidth * 0.5f };
        for (int i = 0; i < DashPairsPerLine; i++)
        {
            float baseZ = -100.0f + i * DashCycle;
            foreach (float x in dividerX)
            {
                SceneObject lightDash = AddObject("Marcaj deschis", cubeMesh);
                lightDash.Transform.Position = new Vector3(x, 0.005f, baseZ);
                lightDash.Transform.Scale = new Vector3(0.11f, 0.02f, DashLength);
                lightDash.Tint = DashLightColor;
                _dashes.Add(new DashSegment { Obj = lightDash, X = x, BaseZ = baseZ });

                SceneObject darkDash = AddObject("Marcaj închis", cubeMesh);
                darkDash.Transform.Position = new Vector3(x, 0.005f, baseZ + DashLength);
                darkDash.Transform.Scale = new Vector3(0.11f, 0.02f, DashLength);
                darkDash.Tint = DashDarkColor;
                _dashes.Add(new DashSegment { Obj = darkDash, X = x, BaseZ = baseZ + DashLength });
            }
        }

        // 6. Ierarhia părinte-copil: Jucătorul (părinte) + baliza de pe acoperiș (copil)
        _player = AddObject("Jucător", cubeMesh);
        _player.Transform.Scale = new Vector3(PlayerSize, PlayerSize, PlayerSize);
        _player.Tint = PlayerColor;

        _beacon = AddObject("Baliză jucător", cubeMesh, _player);
        _beacon.Transform.Scale = new Vector3(0.26f, 0.14f, 0.26f);
        _beacon.Tint = BeaconColor;

        _beaconTrack = new KeyframeTrack(0.35f) { Ease = Easing.EaseInOut }
            .Add(new Vector3(-0.24f, 0.57f, 0.0f))
            .Add(new Vector3(0.24f, 0.57f, 0.0f));

        // 7. Pool de obiecte pentru obstacole (refolosite fără alocări în timpul jocului)
        for (int i = 0; i < MaxObstacles; i++)
        {
            SceneObject obsObj = AddObject($"Obstacol_{i}", cubeMesh);
            obsObj.Visible = false;
            _obstaclePool.Add(new ObstacleSlot { Obj = obsObj });
        }

        ResetGame();
    }

    private void ResetGame()
    {
        _currentLane = 0;
        _laneTween = new Tween(Vector3.Zero, Vector3.Zero, LaneSwitchDuration, Easing.EaseOut);
        _playerY = 0.0f;
        _velocityY = 0.0f;
        _beaconTime = 0.0f;

        _worldSpeed = InitialSpeed;
        _survivedTime = 0.0f;
        _spawnTimer = 0.55f;
        _roadScroll = 0.0f;
        _gameOver = false;

        foreach (ObstacleSlot slot in _obstaclePool)
        {
            slot.Active = false;
            slot.Obj.Visible = false;
        }

        UpdatePlayerTransform();

        // Câteva obstacole inițiale în depărtare, ca în imaginea de referință
        SpawnWave(-32.0f);
        SpawnWave(-54.0f);
        SpawnWave(-76.0f);
    }

    public override void Update(float dt, Input input)
    {
        if (input.WasPressed("Joc nou"))
        {
            ResetGame();
            return;
        }

        if (_gameOver)
        {
            return;
        }

        // 1. Progresia scorului și a vitezei lumii
        _survivedTime += dt;
        _worldSpeed = MathF.Min(MaxSpeed, InitialSpeed + _survivedTime * SpeedAcceleration);

        // 2. Schimbarea benzii cu Tween + EaseOut (< 0.2 s)
        if (input.WasPressed("Stânga") && _currentLane > -1)
        {
            ChangeLane(_currentLane - 1);
        }
        else if (input.WasPressed("Dreapta") && _currentLane < 1)
        {
            ChangeLane(_currentLane + 1);
        }

        _laneTween.Update(dt);

        // 3. Salt cu gravitație
        if (input.WasPressed("Săritură") && _playerY <= 0.0001f)
        {
            _velocityY = JumpVelocity;
        }

        if (_playerY > 0.0f || _velocityY > 0.0f)
        {
            _playerY += _velocityY * dt;
            _velocityY -= Gravity * dt;

            if (_playerY <= 0.0f)
            {
                _playerY = 0.0f;
                _velocityY = 0.0f;
            }
        }

        // Actualizarea poziției jucătorului (părinte) și a balizei (copil)
        _beaconTime += dt;
        UpdatePlayerTransform();

        // 4. Deplasarea marcajelor rutiere odată cu lumea
        _roadScroll = (_roadScroll + _worldSpeed * dt) % DashCycle;
        foreach (DashSegment dash in _dashes)
        {
            dash.Obj.Transform.Position = new Vector3(dash.X, 0.005f, dash.BaseZ + _roadScroll);
        }

        // 5. Generarea și deplasarea obstacolelor + coliziuni AABB
        _spawnTimer -= dt;
        if (_spawnTimer <= 0.0f)
        {
            SpawnWave(-95.0f);
            _spawnTimer = MathF.Max(0.48f, 1.25f - _survivedTime * 0.02f);
        }

        Vector3 playerHalf = new(PlayerSize * 0.44f, PlayerSize * 0.44f, PlayerSize * 0.44f);

        foreach (ObstacleSlot slot in _obstaclePool)
        {
            if (!slot.Active) continue;

            slot.Z += _worldSpeed * dt;
            Vector3 obsSize = slot.Obj.Transform.Scale;
            slot.Obj.Transform.Position = new Vector3(slot.Lane * LaneWidth, obsSize.Y * 0.5f, slot.Z);

            Vector3 obsHalf = obsSize * 0.46f;
            if (CheckAabbOverlap(_player.Transform.Position, playerHalf, slot.Obj.Transform.Position, obsHalf))
            {
                _gameOver = true;
                if (_survivedTime > _bestTime)
                {
                    _bestTime = _survivedTime;
                }
                return;
            }

            if (slot.Z > 10.0f)
            {
                slot.Active = false;
                slot.Obj.Visible = false;
            }
        }

        base.Update(dt, input);
    }

    private void UpdatePlayerTransform()
    {
        _player.Transform.Position = new Vector3(_laneTween.Value.X, PlayerSize * 0.5f + _playerY, PlayerZ);
        _beacon.Transform.Position = _beaconTrack.Sample(_beaconTime);
    }

    private void ChangeLane(int newLane)
    {
        float currentX = _laneTween.Value.X;
        _currentLane = newLane;
        float targetX = _currentLane * LaneWidth;
        _laneTween = new Tween(
            new Vector3(currentX, 0.0f, 0.0f),
            new Vector3(targetX, 0.0f, 0.0f),
            LaneSwitchDuration,
            Easing.EaseOut);
    }

    private void SpawnWave(float zPos)
    {
        int firstLane = _rng.Next(-1, 2);
        ActivateObstacle(firstLane, zPos, _rng.NextDouble() < 0.65);

        // La viteze mai mari pot apărea 2 obstacole pe rând, lăsând mereu o bandă liberă
        if (_survivedTime > 10.0f && _rng.NextDouble() < 0.45)
        {
            int secondLane = firstLane == 0 ? (_rng.Next(2) == 0 ? -1 : 1) : 0;
            ActivateObstacle(secondLane, zPos, _rng.NextDouble() < 0.55);
        }
    }

    private void ActivateObstacle(int lane, float zPos, bool isTall)
    {
        foreach (ObstacleSlot slot in _obstaclePool)
        {
            if (slot.Active) continue;

            slot.Active = true;
            slot.Lane = lane;
            slot.Z = zPos;
            slot.IsTall = isTall;

            Vector3 size = isTall
                ? new Vector3(0.95f, 2.2f, 0.9f)
                : new Vector3(1.15f, 0.60f, 0.80f);

            slot.Obj.Transform.Scale = size;
            slot.Obj.Transform.Position = new Vector3(lane * LaneWidth, size.Y * 0.5f, zPos);
            slot.Obj.Tint = isTall ? TallObstacleColor : LowObstacleColor;
            slot.Obj.Visible = true;
            return;
        }
    }

    /// <summary>Coliziune AABB: două cutii se suprapun dacă se suprapun pe X, Y și Z.</summary>
    private static bool CheckAabbOverlap(Vector3 cA, Vector3 hA, Vector3 cB, Vector3 hB)
    {
        return MathF.Abs(cA.X - cB.X) < (hA.X + hB.X)
            && MathF.Abs(cA.Y - cB.Y) < (hA.Y + hB.Y)
            && MathF.Abs(cA.Z - cB.Z) < (hA.Z + hB.Z);
    }
}