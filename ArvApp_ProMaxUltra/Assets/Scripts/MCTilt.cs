using System;
using UnityEngine;
using UnityEngine.InputSystem;

// Kör motorcykeln framåt längs motorvägen (+X) och byter körfält med A/D.
// Motorcykeln lutar åt det håll den byter körfält.
//
// Motorvägen upprepar sig var 600:e meter. När motorcykeln har kört en hel period
// flyttas den tillbaka exakt en period, så vägen blir oändlig utan synliga hopp.
// Andra fordon (som läggs till senare) ska prenumerera på OnVarldFlyttad och flytta
// sig själva lika mycket, så de stannar på rätt plats i förhållande till motorcykeln.
public class MCTilt : MonoBehaviour
{
    [Header("Fart")]
    [Tooltip("Hastighet framåt i meter per sekund (45 m/s ≈ 160 km/h).")]
    public float hastighet = 45f;

    [Header("Körfält")]
    public float korfaltsBredd = 3.5f;
    [Tooltip("Antal körfält på varje sida om mittenfilen.")]
    public int korfaltPerSida = 1;
    [Tooltip("Ungefär hur många sekunder ett körfältsbyte tar.")]
    public float bytesTid = 0.55f;

    [Header("Lutning")]
    public float maxLutning = 25f;
    public float lutningsHastighet = 8f;
    [Tooltip("Grader lutning per meter/sekund i sidled.")]
    public float lutningPerSidledsfart = 4.5f;
    [Tooltip("Hur mycket motorcykeln vrids i färdriktningen vid körfältsbyte (0 = inte alls).")]
    [Range(0f, 1f)] public float styrvinkelFaktor = 0.5f;

    [Header("Oändlig väg")]
    [Tooltip("Måste vara samma som vägens period (HighwayBuilder.Period).")]
    public float vagPeriod = 600f;
    [Tooltip("Vägens lutning i sidled (höjd per meter i Z), 2,5 % enligt HighwayBuilder.")]
    public float vagLutning = 0.025f;

    /// <summary>Anropas med förflyttningen i X (negativ) när världen flyttas tillbaka en period.</summary>
    public static event Action<float> OnVarldFlyttad;

    /// <summary>Nuvarande körfält: -1 = höger, 0 = mitten, 1 = vänster.</summary>
    public int Korfalt => korfalt;

    /// <summary>Sant efter en krock: motorcykeln välter, glider och stannar, och styrningen slutar fungera.</summary>
    public bool Kraschad { get; private set; }

    /// <summary>Sant när motorcykeln har vält och glidit klart efter en krock.</summary>
    public bool Stannat => Kraschad && kraschFart <= 0f && Mathf.Approximately(nuvarandeLutning, kraschLutning);

    [Header("Krasch")]
    [Tooltip("Hur snabbt motorcykeln bromsas när den glider på sidan (m/s²).")]
    public float glidBromsning = 26f;
    [Tooltip("Hur snabbt den välter (grader per sekund).")]
    public float valtHastighet = 260f;

    int korfalt;
    float sidled, sidledsFart, nuvarandeLutning;
    float kraschFart, kraschLutning, kraschGir, kraschSidled;
    Transform framhjul, bakhjul;
    const float HjulRadieFram = 0.30f, HjulRadieBak = 0.33f;

    /// <summary>Anropas när motorcykeln kör in i en bil.</summary>
    public void Krascha()
    {
        if (Kraschad) return;
        Kraschad = true;
        kraschFart = hastighet;
        // välter åt det håll den redan lutar (eller åt ett slumpat håll om den står rakt)
        float sida = Mathf.Abs(nuvarandeLutning) > 2f ? Mathf.Sign(nuvarandeLutning) : (UnityEngine.Random.value < 0.5f ? -1f : 1f);
        kraschLutning = 84f * sida;
        kraschSidled = sidledsFart * 0.6f;
        kraschGir = (UnityEngine.Random.value - 0.5f) * 50f;
    }

    void KraschUppdatering(float dt)
    {
        // 1) välter snabbt över på sidan, 2) glider på sidan och bromsas av friktionen tills den stannar
        nuvarandeLutning = Mathf.MoveTowards(nuvarandeLutning, kraschLutning, valtHastighet * dt);
        float liggande = Mathf.Abs(nuvarandeLutning) / 84f;                       // 0 = upprätt, 1 = på sidan
        float broms = Mathf.Lerp(glidBromsning * 0.35f, glidBromsning, liggande);   // mer friktion när den ligger ner
        kraschFart = Mathf.MoveTowards(kraschFart, 0f, broms * dt);
        kraschSidled = Mathf.MoveTowards(kraschSidled, 0f, 6f * dt);

        float farAndel = hastighet > 0f ? kraschFart / hastighet : 0f;
        float girNu = Mathf.MoveTowardsAngle(transform.eulerAngles.y, kraschGir, 70f * farAndel * dt);

        var pos = transform.position;
        pos.x += kraschFart * dt;
        pos.z += kraschSidled * dt;
        pos.y = vagLutning * pos.z + 0.18f * liggande;                               // styret/kåpan bär upp den lite från marken
        transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, girNu, 0f) * Quaternion.AngleAxis(nuvarandeLutning, Vector3.right));

        // hjulen snurrar av sig
        Vector3 axel = transform.rotation * Vector3.forward;
        if (framhjul) framhjul.Rotate(axel, -kraschFart / HjulRadieFram * Mathf.Rad2Deg * dt, Space.World);
        if (bakhjul) bakhjul.Rotate(axel, -kraschFart / HjulRadieBak * Mathf.Rad2Deg * dt, Space.World);

        if (pos.x >= 0f)
        {
            transform.position = pos + new Vector3(-vagPeriod, 0f, 0f);
            OnVarldFlyttad?.Invoke(-vagPeriod);
        }
    }

    void Start()
    {
        transform.position = new Vector3(-7.7f, 0f, 0f);   // mittenfilen
        transform.rotation = Quaternion.identity;
        korfalt = 0;
        sidled = 0f;
        foreach (var t in GetComponentsInChildren<Transform>())
        {
            if (t.name == "Wheel_Front") framhjul = t;
            else if (t.name == "Wheel_Rear") bakhjul = t;
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;   // spelet är pausat
        if (Kraschad) { KraschUppdatering(dt); return; }

        // Körfältsbyte: varje tryck flyttar ett körfält. Höger är -Z i världen.
        var tangentbord = Keyboard.current;
        if (tangentbord != null)
        {
            if (tangentbord.dKey.wasPressedThisFrame || tangentbord.rightArrowKey.wasPressedThisFrame)
                korfalt = Mathf.Max(korfalt - 1, -korfaltPerSida);
            if (tangentbord.aKey.wasPressedThisFrame || tangentbord.leftArrowKey.wasPressedThisFrame)
                korfalt = Mathf.Min(korfalt + 1, korfaltPerSida);
        }

        float malZ = korfalt * korfaltsBredd;
        sidled = Mathf.SmoothDamp(sidled, malZ, ref sidledsFart, bytesTid * 0.4f);

        var pos = transform.position;
        pos.x += hastighet * dt;
        pos.z = sidled;
        pos.y = vagLutning * pos.z;   // vägen lutar i sidled

        // Lutning: proportionell mot farten i sidled (negativ sidledsfart = mot höger = lutar höger).
        float malLutning = Mathf.Clamp(sidledsFart * lutningPerSidledsfart, -maxLutning, maxLutning);
        nuvarandeLutning = Mathf.Lerp(nuvarandeLutning, malLutning, 1f - Mathf.Exp(-lutningsHastighet * dt));
        // Liten styrvinkel åt samma håll som körfältsbytet.
        float gir = -Mathf.Atan2(sidledsFart, Mathf.Max(hastighet, 0.1f)) * Mathf.Rad2Deg * styrvinkelFaktor;
        transform.rotation = Quaternion.Euler(0f, gir, 0f) * Quaternion.AngleAxis(nuvarandeLutning, Vector3.right);

        // Hjulen rullar: rotation runt axeln (motorcykelns lokala Z).
        Vector3 axel = transform.rotation * Vector3.forward;
        if (framhjul) framhjul.Rotate(axel, -hastighet / HjulRadieFram * Mathf.Rad2Deg * dt, Space.World);
        if (bakhjul) bakhjul.Rotate(axel, -hastighet / HjulRadieBak * Mathf.Rad2Deg * dt, Space.World);

        // Oändlig väg: hoppa tillbaka en period (vägen ser exakt likadan ut där).
        if (pos.x >= 0f)
        {
            pos.x -= vagPeriod;
            transform.position = pos;
            OnVarldFlyttad?.Invoke(-vagPeriod);
        }
        else
        {
            transform.position = pos;
        }
    }
}
