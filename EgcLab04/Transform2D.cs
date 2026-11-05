using OpenTK.Mathematics;

namespace Egc;

/// <summary>
/// Poziția, rotația și scalarea unui obiect din scenă. Din ele se construiește matricea model,
/// care duce vârfurile obiectului din object space în world space (secțiunea 5).
/// </summary>
public class Transform2D
{
    public Vector2 Position = Vector2.Zero;   // translația, în unități de lume
    public float Rotation = 0.0f;             // unghiul, în GRADE, pozitiv = sens trigonometric
    public Vector2 Scale = Vector2.One;       // factorii de scalare pe x și pe y
    public float Depth = 0.0f;                // coordonata z; contează doar în perspectivă (secțiunea 7.2)

    public Matrix4 ModelMatrix =>
        // TODO (T1): scalare, apoi rotație, apoi translație
        //scalare
        Matrix4.CreateScale(Scale.X, Scale.Y, 1.0f)*
        //rotatie
        Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(Rotation)) *
        //translatie
        Matrix4.CreateTranslation(Position.X, Position.Y, Depth);
    //daca as pune transaltia inainte rotatia, atunci obiectul s-ar roti in jurul originii, nu in jurul centrului lui




    /// <summary>O copie independentă (folosită pentru readucerea scenei în starea inițială).</summary>
    public Transform2D Clone() => (Transform2D)MemberwiseClone();
}
