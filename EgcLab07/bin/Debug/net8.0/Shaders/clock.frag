#version 330 core

// Varianta A: ceas analogic desenat integral in fragment shader, cu rama,
// 60 de gradatii, orar, minutar, secundar, tema zi/noapte si fereastra pentru faza lunii.

in vec3 vColor;
in vec2 vUV;

uniform bool uShowUV;
uniform float uTime;

// Uniforme regulate cu sagetile (Param)
uniform float uBezelWidth;   // grosimea ramei exterioare
uniform float uTickScale;    // scara lungimii/grosimii gradatiilor
uniform float uMoonPhase;    // faza lunii in [0, 1] (0 = luna noua, 0.5 = luna plina)

// Unghiurile acelor (radiani, masurate de la ora 12 in sens orar), calculate in C#
uniform float uHourAngle;
uniform float uMinuteAngle;
uniform float uSecondAngle;

// Culorile temei (interpolate lin cu Tween intre zi si noapte)
uniform vec3 uFaceColor;
uniform vec3 uBezelColor;
uniform vec3 uTickColor;
uniform vec3 uSecondColor;

out vec4 FragColor;

const float PI = 3.14159265359;
const float TWO_PI = 6.28318530718;

// Distanta 2D de la punctul p la segmentul [a, b] (4 randuri, pentru ace)
float sdSegment(vec2 p, vec2 a, vec2 b)
{
    vec2 pa = p - a, ba = b - a;
    float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
    return length(pa - ba * h);
}

void main()
{
    if (uShowUV)
    {
        FragColor = vec4(vUV, 0.0, 1.0);
        return;
    }

    // Coordonate centrate in [-0.5, 0.5]
    vec2 p = vUV - vec2(0.5);
    float r = length(p);
    float aa = max(fwidth(r), 0.0012);

    // Razele structurii ceasului
    float rOuter = 0.475;
    float rOuterRim = rOuter - 0.008;
    float rBezelInner = rOuterRim - uBezelWidth;
    float rFace = rBezelInner - 0.011;

    // Decupare circulara: alfa 0 in afara ceasului (margini netede)
    float clockAlpha = 1.0 - smoothstep(rOuter - aa, rOuter + aa, r);
    if (clockAlpha <= 0.001)
    {
        FragColor = vec4(0.0);
        return;
    }

    // 1. Cadranul (cu o umbrire radiala discreta spre margine)
    float faceShade = 1.0 - 0.08 * smoothstep(rFace * 0.55, rFace, r);
    vec3 col = uFaceColor * faceShade;

    // 2. Bonus: fereastra cu faza lunii in partea de jos a cadranului
    vec2 moonCenter = vec2(0.0, -0.145);
    float moonWinR = 0.056;
    float dMoonWin = length(p - moonCenter);
    if (dMoonWin < moonWinR + aa * 2.0)
    {
        // Cerul noptii din fereastra + o usoara pulsatie din uTime
        vec3 skyCol = vec3(0.06, 0.09, 0.17) * (0.95 + 0.05 * sin(uTime * 1.5));

        // Discul lunii iluminat conform uMoonPhase
        float moonDiskR = 0.041;
        vec2 mp = (p - moonCenter) / moonDiskR;
        float mr2 = dot(mp, mp);
        vec3 winCol = skyCol;

        if (mr2 < 1.0)
        {
            float mz = sqrt(max(0.0, 1.0 - mr2));
            vec3 normal = vec3(mp.x, mp.y, mz);
            float phaseAng = uMoonPhase * TWO_PI;
            vec3 sunDir = normalize(vec3(sin(phaseAng), 0.12, -cos(phaseAng)));
            float lit = smoothstep(-0.08, 0.12, dot(normal, sunDir));
            vec3 moonLight = vec3(0.96, 0.92, 0.76);
            vec3 moonDark = vec3(0.14, 0.17, 0.25);
            vec3 moonCol = mix(moonDark, moonLight, lit);
            float diskMask = 1.0 - smoothstep(0.92, 1.0, sqrt(mr2));
            winCol = mix(skyCol, moonCol, diskMask);
        }

        // Contur fin auriu al ferestrei lunii
        float winMask = 1.0 - smoothstep(moonWinR - aa, moonWinR + aa, dMoonWin);
        float winBorder = smoothstep(moonWinR - 0.005 - aa, moonWinR - 0.005 + aa, dMoonWin) * winMask;
        col = mix(col, winCol, winMask);
        col = mix(col, uBezelColor * 0.85, winBorder);
    }

    // 3. Cele 60 de gradatii (unghi masurat de la ora 12, in sens orar)
    float angle = mod(atan(p.x, p.y), TWO_PI);
    float step60 = TWO_PI / 60.0;
    float tickIdx = floor(angle / step60 + 0.5);
    float dAngle = abs(angle - tickIdx * step60);
    float perpDist = dAngle * r;

    bool isHourTick = mod(tickIdx, 5.0) < 0.5;
    float tickOuter = rFace - 0.016;
    float tickLen = (isHourTick ? 0.048 : 0.023) * uTickScale;
    float tickInner = tickOuter - tickLen;
    float tickHalfW = (isHourTick ? 0.0052 : 0.0023) * uTickScale;

    float inRadial = smoothstep(tickInner - aa, tickInner + aa, r)
                   * (1.0 - smoothstep(tickOuter - aa, tickOuter + aa, r));
    float inAngular = 1.0 - smoothstep(tickHalfW - aa, tickHalfW + aa, perpDist);
    float tickMask = inRadial * inAngular;
    col = mix(col, uTickColor, tickMask);

    // 4. Rama ceasului (inelul exterior inchis, rama metalica si bordura interioara)
    float innerBorderMask = smoothstep(rFace - aa, rFace + aa, r)
                          * (1.0 - smoothstep(rBezelInner - aa, rBezelInner + aa, r));
    col = mix(col, uTickColor * 0.85, innerBorderMask);

    float bezelMask = smoothstep(rBezelInner - aa, rBezelInner + aa, r)
                    * (1.0 - smoothstep(rOuterRim - aa, rOuterRim + aa, r));
    float bezelT = clamp((r - rBezelInner) / max(uBezelWidth, 0.001), 0.0, 1.0);
    float bevel = 0.88 + 0.22 * sin(bezelT * PI);
    col = mix(col, uBezelColor * bevel, bezelMask);

    float outerRimMask = smoothstep(rOuterRim - aa, rOuterRim + aa, r);
    col = mix(col, vec3(0.08, 0.08, 0.10), outerRimMask);

    // 5. Acele ceasului (orarul, minutarul, secundarul + axul central)
    vec2 hourDir = vec2(sin(uHourAngle), cos(uHourAngle));
    float dHour = sdSegment(p, -0.032 * hourDir, 0.205 * hourDir);
    float hourMask = 1.0 - smoothstep(0.0095 - aa, 0.0095 + aa, dHour);
    col = mix(col, uTickColor, hourMask);

    vec2 minDir = vec2(sin(uMinuteAngle), cos(uMinuteAngle));
    float dMin = sdSegment(p, -0.042 * minDir, 0.305 * minDir);
    float minMask = 1.0 - smoothstep(0.0062 - aa, 0.0062 + aa, dMin);
    col = mix(col, uTickColor, minMask);

    vec2 secDir = vec2(sin(uSecondAngle), cos(uSecondAngle));
    float dSec = sdSegment(p, -0.082 * secDir, 0.330 * secDir);
    float secMask = 1.0 - smoothstep(0.0026 - aa, 0.0026 + aa, dSec);
    col = mix(col, uSecondColor, secMask);

    // Capacul central peste axul acelor
    float capMask = 1.0 - smoothstep(0.014 - aa, 0.014 + aa, r);
    col = mix(col, uSecondColor, capMask);

    FragColor = vec4(col * vColor, clockAlpha);
}