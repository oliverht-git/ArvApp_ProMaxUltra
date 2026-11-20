using System;
using UnityEngine;
using UnityEngine.InputSystem;

// Kör motorcykeln framåt längs motorvägen (+X) och byter körfält med A/D eller med spaken på en Joy-Con.
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

    [Header("Hjul")]
    [Tooltip("Objektet där hjulen (Wheel_Front / Wheel_Rear) letas efter, t.ex. Motorcyklar. Tomt = motorcykeln själv.")]
    public Transform hjulSokning;

    [Header("Joy-Con / handkontroll")]
    [Tooltip("Hur långt spaken måste föras åt sidan (0-1) för att byta körfält.")]
    [Range(0.2f, 0.95f)] public float spakTroskel = 0.5f;
    [Tooltip("Använd spakens upp/ner i stället för vänster/höger (om Joy-Con:en hålls på ett annat sätt).")]
    public bool spakAnvandYAxel = false;
    [Tooltip("Byt håll på spaken om vänster och höger blir fel.")]
    public bool spakInvertera = false;

    int spakRiktning;   // -1 = vänster, 0 = mitten, 1 = höger (förra bildrutan)

    int korfalt;
    float sidled, sidledsFart, nuvarandeLutning;
    float kraschFart, kraschLutning, kraschGir, kraschSidled;
    // Alla fram- och bakhjul, så hjulen snurrar på den motorcykel som syns (även om de andra är avstängda).
    // Varje hjul sparar sin hjulaxel i sitt eget koordinatsystem, så det alltid snurrar exakt runt
    // sin egen axel. (Att snurra runt en axel i världen gör att små fel läggs ihop och hjulen vinglar.)
    struct Hjul { public Transform t; public Vector3 axel; public float radie; }
    readonly System.Collections.Generic.List<Hjul> hjulen = new();
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
        SnurraHjul(kraschFart, dt);

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
        hjulen.Clear();
        var sok = hjulSokning != null ? hjulSokning : transform;
        // Hjulaxeln är motorcykelns sidled (lokala Z). Räknas om till varje hjuls eget koordinatsystem.
        Vector3 sidaIVarlden = sok.TransformDirection(Vector3.forward);
        foreach (var t in sok.GetComponentsInChildren<Transform>(true))
        {
            float radie = t.name == "Wheel_Front" ? HjulRadieFram : t.name == "Wheel_Rear" ? HjulRadieBak : 0f;
            if (radie > 0f)
                hjulen.Add(new Hjul { t = t, axel = t.InverseTransformDirection(sidaIVarlden).normalized, radie = radie });
        }
    }

    // Spakens läge i sidled från -1 (vänster) till 1 (höger), från Joy-Con eller annan handkontroll.
    // En Joy-Con läses av JoyConHanterare (fullt rapportläge). Andra joysticks läses via sin hat/stick.
    float SpakSidled()
    {
        Vector2 v = Vector2.zero;
        var jc = JoyConHanterare.Instans;
        if (jc != null && jc.Ansluten) v = jc.Spak;

        foreach (var js in Joystick.all)
        {
            if (jc != null && jc.ArJoyCon(js)) continue;   // dess vanliga HID-tolkning är fel i fullt läge
            var hat = js.TryGetChildControl<UnityEngine.InputSystem.Controls.DpadControl>("hat");
            Vector2 j = hat != null ? hat.ReadValue() : js.stick.ReadValue();
            if (j.sqrMagnitude > v.sqrMagnitude) v = j;
        }
        foreach (var gp in Gamepad.all)   // t.ex. Pro Controller eller Joy-Con-par
        {
            Vector2 g = gp.leftStick.ReadValue();
            Vector2 d = gp.dpad.ReadValue();
            if (g.sqrMagnitude > v.sqrMagnitude) v = g;
            if (d.sqrMagnitude > v.sqrMagnitude) v = d;
        }

        float x = spakAnvandYAxel ? v.y : v.x;
        return spakInvertera ? -x : x;
    }

    // Hjulen rullar: rotation runt varje hjuls egen axel.
    void SnurraHjul(float fart, float dt)
    {
        foreach (var h in hjulen)
            if (h.t) h.t.Rotate(h.axel, -fart / h.radie * Mathf.Rad2Deg * dt, Space.Self);
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

        // Joy-Con / handkontroll: varje gång spaken förs ut åt sidan byts ett körfält.
        // Spaken måste tillbaka till mitten innan nästa byte, precis som att släppa en tangent.
        float spak = SpakSidled();
        int riktning = spak > spakTroskel ? 1 : (spak < -spakTroskel ? -1 : 0);
        if (riktning != 0 && spakRiktning == 0)
        {
            if (riktning > 0) korfalt = Mathf.Max(korfalt - 1, -korfaltPerSida);   // höger
            else korfalt = Mathf.Min(korfalt + 1, korfaltPerSida);                // vänster
        }
        if (Mathf.Abs(spak) < spakTroskel * 0.6f) spakRiktning = 0;   // tillbaka i mitten (lite marginal så den inte fladdrar)
        else if (riktning != 0) spakRiktning = riktning;

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

        SnurraHjul(hastighet, dt);

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
