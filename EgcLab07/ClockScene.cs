using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc.Teme;

/// <summary>
/// Tema 7 — Varianta A: Ceas analogic desenat integral de un fragment shader pe un Quad,
/// cu secundar cu salt EaseOut (Tween), tema zi/noapte (Tween) si fereastra pentru faza lunii.
/// </summary>
public sealed class ClockScene : Scene
{
    private const string ToggleThemeAction = "ceas: temă zi / noapte";
    private const string ToggleFastAction = "ceas: timp rapid (×60)";

    // Culori tema de zi (identice cu imaginea de referinta)
    private static readonly Vector3 DayFace = new(0.91f, 0.89f, 0.82f);
    private static readonly Vector3 DayBezel = new(0.66f, 0.54f, 0.33f);
    private static readonly Vector3 DayTick = new(0.10f, 0.10f, 0.12f);
    private static readonly Vector3 DaySecond = new(0.82f, 0.14f, 0.14f);

    // Culori tema de noapte
    private static readonly Vector3 NightFace = new(0.09f, 0.12f, 0.19f);
    private static readonly Vector3 NightBezel = new(0.36f, 0.42f, 0.52f);
    private static readonly Vector3 NightTick = new(0.84f, 0.94f, 0.98f);
    private static readonly Vector3 NightSecond = new(1.00f, 0.42f, 0.18f);

    private Material _clockMaterial = null!;

    // Timpul simulat al ceasului (pornim la 10:08:30, ca in imaginea de prezentare)
    private double _clockSeconds = 10 * 3600 + 8 * 60 + 30;
    private int _lastWholeSecond;
    private bool _fastForward;
    private bool _isNight;

    // Tween pentru saltul secundarului (EaseOut) si pentru tranzitia zi <-> noapte
    private Tween _secondAngleTween = null!;
    private Tween _faceTween = null!;
    private Tween _bezelTween = null!;
    private Tween _tickTween = null!;
    private Tween _secondColorTween = null!;

    public override string Name => "ceas";

    public override IEnumerable<string> ActionNames => new[] { ToggleThemeAction, ToggleFastAction };

    public override string Status
    {
        get
        {
            int total = (int)Math.Floor(_clockSeconds) % 43200;
            if (total < 0) total += 43200;
            int h = total / 3600;
            if (h == 0) h = 12;
            int m = (total % 3600) / 60;
            int s = total % 60;
            string theme = _isNight ? "noapte" : "zi";
            string speed = _fastForward ? " | ×60" : "";
            return $"{h:00}:{m:00}:{s:00} | temă: {theme}{speed}";
        }
    }

    protected override void Load(Input input)
    {
        CameraPosition = new Vector3(0.0f, 2.0f, 4.4f);
        CameraTarget = new Vector3(0.0f, 2.0f, 0.0f);

        input.Bind(ToggleThemeAction, Keys.Space);
        input.Bind(ToggleFastAction, Keys.T);

        Mesh quad = AddMesh(Shapes.Quad(2.0f, 2.0f));

        // Material transparent (alfa 0 in afara discului ceasului) + 3 parametri reglabili
        _clockMaterial = AddMaterial("ceas", "procedural.vert", "clock.frag")
            .Param("uBezelWidth", 0.03f, 0.14f, 0.075f)
            .Param("uTickScale", 0.6f, 1.5f, 1.0f)
            .Param("uMoonPhase", 0.0f, 1.0f, 0.25f);
        _clockMaterial.Transparent = true;

        // Initializare tween-uri pentru tema de zi
        _faceTween = new Tween(DayFace, DayFace, 0.7f, Easing.EaseInOut);
        _bezelTween = new Tween(DayBezel, DayBezel, 0.7f, Easing.EaseInOut);
        _tickTween = new Tween(DayTick, DayTick, 0.7f, Easing.EaseInOut);
        _secondColorTween = new Tween(DaySecond, DaySecond, 0.7f, Easing.EaseInOut);

        _lastWholeSecond = (int)Math.Floor(_clockSeconds);
        float initialSecAngle = _lastWholeSecond * (MathF.Tau / 60.0f);
        _secondAngleTween = new Tween(
            new Vector3(initialSecAngle, 0.0f, 0.0f),
            new Vector3(initialSecAngle, 0.0f, 0.0f),
            0.16f,
            Easing.EaseOut);

        ApplyUniforms(initialSecAngle);

        SceneObject clockObj = AddObject("ceas", quad, _clockMaterial);
        clockObj.Transform.Position = new Vector3(0.0f, 2.0f, 0.0f);
        clockObj.Transform.Scale = new Vector3(1.65f, 1.65f, 1.0f);
    }

    public override void Update(float dt, Input input)
    {
        base.Update(dt, input);

        // Comutare tema zi / noapte cu Tween (Space)
        if (input.WasPressed(ToggleThemeAction))
        {
            _isNight = !_isNight;
            _faceTween.GoTo(_isNight ? NightFace : DayFace, 0.7f);
            _bezelTween.GoTo(_isNight ? NightBezel : DayBezel, 0.7f);
            _tickTween.GoTo(_isNight ? NightTick : DayTick, 0.7f);
            _secondColorTween.GoTo(_isNight ? NightSecond : DaySecond, 0.7f);
        }

        // Comutare mod rapid x60 (tasta T) pentru observarea minutarului si orarului
        if (input.WasPressed(ToggleFastAction))
        {
            _fastForward = !_fastForward;
        }

        _faceTween.Update(dt);
        _bezelTween.Update(dt);
        _tickTween.Update(dt);
        _secondColorTween.Update(dt);

        // Avansarea timpului ceasului folosind exclusiv dt (timpul de joc)
        float speedMult = _fastForward ? 60.0f : 1.0f;
        _clockSeconds += dt * speedMult;

        int currentWholeSecond = (int)Math.Floor(_clockSeconds);
        float secondAngle;

        if (!_fastForward)
        {
            // Secundarul "sare" o data pe secunda cu o miscare scurta EaseOut
            if (currentWholeSecond != _lastWholeSecond)
            {
                float prevAngle = _lastWholeSecond * (MathF.Tau / 60.0f);
                float nextAngle = currentWholeSecond * (MathF.Tau / 60.0f);
                _secondAngleTween = new Tween(
                    new Vector3(prevAngle, 0.0f, 0.0f),
                    new Vector3(nextAngle, 0.0f, 0.0f),
                    0.16f,
                    Easing.EaseOut);
                _lastWholeSecond = currentWholeSecond;
            }
            _secondAngleTween.Update(dt);
            secondAngle = _secondAngleTween.Value.X;
        }
        else
        {
            _lastWholeSecond = currentWholeSecond;
            secondAngle = (float)(_clockSeconds % 60.0) * (MathF.Tau / 60.0f);
        }

        ApplyUniforms(secondAngle);
    }

    private void ApplyUniforms(float secondAngle)
    {
        float minuteAngle = (float)((_clockSeconds / 60.0) % 60.0) * (MathF.Tau / 60.0f);
        float hourAngle = (float)((_clockSeconds / 3600.0) % 12.0) * (MathF.Tau / 12.0f);

        _clockMaterial.Uniforms.Set("uHourAngle", hourAngle);
        _clockMaterial.Uniforms.Set("uMinuteAngle", minuteAngle);
        _clockMaterial.Uniforms.Set("uSecondAngle", secondAngle);

        _clockMaterial.Uniforms.Set("uFaceColor", _faceTween.Value);
        _clockMaterial.Uniforms.Set("uBezelColor", _bezelTween.Value);
        _clockMaterial.Uniforms.Set("uTickColor", _tickTween.Value);
        _clockMaterial.Uniforms.Set("uSecondColor", _secondColorTween.Value);
    }

    public override IEnumerable<string> Describe()
    {
        foreach (string line in base.Describe()) yield return line;
        yield return $"Ora afișată    : {Status}";
        yield return $"Faza lunii     : {_clockMaterial.Uniforms.Get<float>("uMoonPhase"):0.##}";
    }
}