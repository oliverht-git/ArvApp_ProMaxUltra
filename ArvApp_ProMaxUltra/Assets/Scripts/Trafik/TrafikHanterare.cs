using System.Collections.Generic;
using UnityEngine;

// Skapar och styr trafiken runt motorcykeln, på båda körbanorna.
// - Medtrafik: bilar åt samma håll som motorcykeln, i slumpade körfält med lite olika fart;
//   motorcykeln är 90-110 % snabbare.
// - Mötande trafik: bilar på andra sidan mitträcket som kör mot motorcykeln.
// - Bilarna byter körfält då och då (och när de kommer ikapp en långsammare bil),
//   men bara när det finns plats.
// - Bilar som hamnat långt bakom motorcykeln flyttas långt fram igen, så det alltid finns trafik.
// - När motorcykeln flyttas tillbaka en vägperiod (MCTilt.OnVarldFlyttad) flyttas bilarna lika mycket.
public class TrafikHanterare : MonoBehaviour
{
    [Header("Bilar")]
    [Tooltip("De bilmodeller som används. Varje modell används flera gånger.")]
    public GameObject[] bilPrefabs;
    [Tooltip("Antal bilar åt samma håll som motorcykeln.")]
    public int antalBilar = 20;
    [Tooltip("Antal mötande bilar på andra körbanan.")]
    public int antalMotandeBilar = 14;

    [Header("Fart")]
    [Tooltip("Hur mycket snabbare motorcykeln är än bilarna (1.9 = 90 % snabbare).")]
    public float minFartskvot = 1.9f;
    public float maxFartskvot = 2.1f;

    [Header("Körfält")]
    public float korfaltsBredd = 3.5f;
    public int korfaltPerSida = 1;
    [Tooltip("Sekunder mellan försök att byta körfält (slumpas mellan min och max).")]
    public Vector2 tidMellanByten = new Vector2(5f, 14f);

    [Header("Körbanor (måste stämma med vägen från HighwayBuilder)")]
    [Tooltip("Mittfilens Z för vår körbana och för den mötande körbanan.")]
    public float mittZ = 0f;
    public float motandeMittZ = 16.5f;
    [Tooltip("Vägytans höjd vid respektive mittfil och lutning i sidled.")]
    public float vagHojd = 0f, vagLutning = 0.025f;
    public float motandeVagHojd = 0f, motandeVagLutning = -0.025f;

    [Header("Område runt motorcykeln (meter)")]
    public float forstaBil = 40f;
    public float bakomGrans = 120f;
    public Vector2 nyBilFramfor = new Vector2(380f, 620f);
    public Vector2 nyMotandeBilFramfor = new Vector2(550f, 900f);
    public float minAvstand = 28f;

    public MCTilt motorcykel;
    public int seed = 0;

    readonly List<TrafikBil> bilar = new List<TrafikBil>();
    System.Random rnd;

    /// <summary>Alla bilar i trafiken, båda riktningarna (används t.ex. för krockkontroll).</summary>
    public IReadOnlyList<TrafikBil> Bilar => bilar;

    /// <summary>När sant står trafiken still (t.ex. efter en krock).</summary>
    [HideInInspector] public bool pausad;

    void OnEnable() => MCTilt.OnVarldFlyttad += VarldFlyttad;
    void OnDisable() => MCTilt.OnVarldFlyttad -= VarldFlyttad;

    void Start()
    {
        if (motorcykel == null) motorcykel = FindFirstObjectByType<MCTilt>();
        rnd = seed == 0 ? new System.Random() : new System.Random(seed);
        if (bilPrefabs == null || bilPrefabs.Length == 0 || motorcykel == null) { enabled = false; return; }

        // varje modell används ungefär lika många gånger, i blandad ordning
        int totalt = antalBilar + antalMotandeBilar;
        var ordning = new List<int>();
        for (int i = 0; i < totalt; i++) ordning.Add(i % bilPrefabs.Length);
        for (int i = ordning.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); (ordning[i], ordning[j]) = (ordning[j], ordning[i]); }

        float mx = motorcykel.transform.position.x;
        float xMed = mx + forstaBil;                 // medtrafik: från strax framför motorcykeln och framåt
        float xMot = mx - 60f;                       // mötande: utspridda både bakom och långt framför
        for (int n = 0; n < totalt; n++)
        {
            bool mot = n >= antalBilar;
            var bil = SkapaBil(ordning[n], mot);
            float x = mot ? xMot : xMed;
            bool placerad = false;
            for (int forsok = 0; forsok < 20 && !placerad; forsok++)
            {
                int lane = rnd.Next(-korfaltPerSida, korfaltPerSida + 1);
                if (Ledigt(lane, x, bil.riktning, null)) { bil.Placera(x, lane, korfaltsBredd); placerad = true; }
                else x += 8f;
            }
            if (!placerad) bil.Placera(x, 0, korfaltsBredd);
            bilar.Add(bil);
            if (mot) xMot = x + Slump(minAvstand * 1.2f, minAvstand * 2.6f);
            else xMed = x + Slump(minAvstand * 0.6f, minAvstand * 1.6f);
        }
    }

    TrafikBil SkapaBil(int modell, bool motande)
    {
        var go = Instantiate(bilPrefabs[modell], transform);
        go.name = $"{bilPrefabs[modell].name}_{(motande ? "Mot" : "Med")}_{bilar.Count + 1:00}";
        if (!go.TryGetComponent(out TrafikBil bil)) bil = go.AddComponent<TrafikBil>();
        bil.riktning = motande ? -1 : 1;
        bil.mittZ = motande ? motandeMittZ : mittZ;
        bil.vagHojd = motande ? motandeVagHojd : vagHojd;
        bil.vagLutning = motande ? motandeVagLutning : vagLutning;
        bil.onskadFart = bil.fart = NyFart();
        bil.tidTillByte = Slump(tidMellanByten.x, tidMellanByten.y);
        return bil;
    }

    float Slump(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
    float NyFart() => motorcykel.hastighet / Slump(minFartskvot, maxFartskvot);
    int MotorcykelnsKorfalt => Mathf.RoundToInt((motorcykel.transform.position.z - mittZ) / korfaltsBredd);

    // Ledigt om ingen annan bil på samma körbana är i (eller på väg till) körfältet nära x.
    // "bakom"/"fram" räknas i bilens färdriktning. På vår körbana räknas även motorcykeln.
    bool Ledigt(int lane, float x, int riktning, TrafikBil utom, float bakom = 18f, float fram = 26f)
    {
        foreach (var b in bilar)
        {
            if (b == utom || b.riktning != riktning) continue;
            float d = (b.X - x) * riktning;
            if ((b.korfalt == lane || b.malKorfalt == lane) && d > -bakom && d < fram) return false;
        }
        if (riktning > 0)
        {
            float mx = motorcykel.transform.position.x;
            if (MotorcykelnsKorfalt == lane && mx > x - 20f && mx < x + 15f) return false;
        }
        return true;
    }

    TrafikBil NarmastFramfor(TrafikBil bil, int lane, out float avstand)
    {
        TrafikBil basta = null; avstand = float.MaxValue;
        foreach (var b in bilar)
        {
            if (b == bil || b.riktning != bil.riktning || (b.korfalt != lane && b.malKorfalt != lane)) continue;
            float d = (b.X - bil.X) * bil.riktning;
            if (d > 0f && d < avstand) { avstand = d; basta = b; }
        }
        return basta;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || pausad) return;   // spelet är pausat
        float mx = motorcykel.transform.position.x;

        foreach (var bil in bilar)
        {
            // håll avstånd till bilen framför i samma körfält
            float malFart = bil.onskadFart;
            var fram = NarmastFramfor(bil, bil.malKorfalt, out float avstand);
            bool blockerad = false;
            if (fram != null && avstand < 45f && fram.fart < bil.onskadFart)
            {
                blockerad = true;
                if (avstand < 30f) malFart = Mathf.Min(malFart, fram.fart * (avstand < 14f ? 0.92f : 1f));
            }
            bil.fart = Mathf.MoveTowards(bil.fart, malFart, 2.5f * dt);

            // byt körfält då och då, eller tidigare om bilen är blockerad
            bil.tidTillByte -= dt * (blockerad ? 3f : 1f);
            if (bil.tidTillByte <= 0f && !bil.ByterKorfalt)
            {
                bil.tidTillByte = Slump(tidMellanByten.x, tidMellanByten.y);
                int steg = rnd.Next(2) == 0 ? -1 : 1;
                for (int k = 0; k < 2; k++, steg = -steg)
                {
                    int lane = bil.korfalt + steg;
                    if (lane < -korfaltPerSida || lane > korfaltPerSida) continue;
                    if (Ledigt(lane, bil.X, bil.riktning, bil)) { bil.malKorfalt = lane; break; }
                }
            }

            bil.Simulera(dt, korfaltsBredd);

            // bilar långt bakom motorcykeln dyker upp igen långt fram
            if (bil.X < mx - bakomGrans) FlyttaFram(bil, mx);
        }
    }

    void FlyttaFram(TrafikBil bil, float mx)
    {
        var omrade = bil.riktning > 0 ? nyBilFramfor : nyMotandeBilFramfor;
        for (int forsok = 0; forsok < 30; forsok++)
        {
            float x = mx + Slump(omrade.x, omrade.y);
            int lane = rnd.Next(-korfaltPerSida, korfaltPerSida + 1);
            if (!Ledigt(lane, x, bil.riktning, bil, minAvstand, minAvstand)) continue;
            bil.Placera(x, lane, korfaltsBredd);
            bil.onskadFart = bil.fart = NyFart();
            bil.tidTillByte = Slump(tidMellanByten.x, tidMellanByten.y);
            return;
        }
    }

    void VarldFlyttad(float dx)
    {
        foreach (var bil in bilar) bil.FlyttaX(dx);
    }
}
