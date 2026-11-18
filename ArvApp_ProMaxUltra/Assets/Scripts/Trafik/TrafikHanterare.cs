using System.Collections.Generic;
using UnityEngine;

// Skapar och styr trafiken runt motorcykeln.
// - Bilarna kör i slumpade körfält med lite olika fart; motorcykeln är 90-110 % snabbare.
// - Bilarna byter körfält då och då (och när de kommer ikapp en långsammare bil),
//   men bara när det finns plats.
// - Bilar som hamnat långt bakom motorcykeln flyttas långt fram igen, så det alltid finns trafik.
// - När motorcykeln flyttas tillbaka en vägperiod (MCTilt.OnVarldFlyttad) flyttas bilarna lika mycket.
public class TrafikHanterare : MonoBehaviour
{
    [Header("Bilar")]
    [Tooltip("De bilmodeller som används. Varje modell används flera gånger.")]
    public GameObject[] bilPrefabs;
    public int antalBilar = 20;

    [Header("Fart")]
    [Tooltip("Hur mycket snabbare motorcykeln är än bilarna (1.9 = 90 % snabbare).")]
    public float minFartskvot = 1.9f;
    public float maxFartskvot = 2.1f;

    [Header("Körfält")]
    public float korfaltsBredd = 3.5f;
    public int korfaltPerSida = 1;
    [Tooltip("Sekunder mellan försök att byta körfält (slumpas mellan min och max).")]
    public Vector2 tidMellanByten = new Vector2(5f, 14f);

    [Header("Område runt motorcykeln (meter)")]
    public float forstaBil = 40f;
    public float bakomGrans = 120f;
    public Vector2 nyBilFramfor = new Vector2(380f, 620f);
    public float minAvstand = 28f;

    public MCTilt motorcykel;
    public int seed = 0;

    readonly List<TrafikBil> bilar = new List<TrafikBil>();
    System.Random rnd;

    void OnEnable() => MCTilt.OnVarldFlyttad += VarldFlyttad;
    void OnDisable() => MCTilt.OnVarldFlyttad -= VarldFlyttad;

    void Start()
    {
        if (motorcykel == null) motorcykel = FindFirstObjectByType<MCTilt>();
        rnd = seed == 0 ? new System.Random() : new System.Random(seed);
        if (bilPrefabs == null || bilPrefabs.Length == 0 || motorcykel == null) { enabled = false; return; }

        // varje modell används ungefär lika många gånger, i blandad ordning
        var ordning = new List<int>();
        for (int i = 0; i < antalBilar; i++) ordning.Add(i % bilPrefabs.Length);
        for (int i = ordning.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); (ordning[i], ordning[j]) = (ordning[j], ordning[i]); }

        float x = motorcykel.transform.position.x + forstaBil;
        foreach (int modell in ordning)
        {
            var go = Instantiate(bilPrefabs[modell], transform);
            go.name = $"{bilPrefabs[modell].name}_{bilar.Count + 1:00}";
            if (!go.TryGetComponent(out TrafikBil bil)) bil = go.AddComponent<TrafikBil>();
            bil.onskadFart = bil.fart = NyFart();
            bil.tidTillByte = Slump(tidMellanByten.x, tidMellanByten.y);
            // placera med lagom mellanrum, i ett körfält där det finns plats
            bool placerad = false;
            for (int forsok = 0; forsok < 20 && !placerad; forsok++)
            {
                int lane = rnd.Next(-korfaltPerSida, korfaltPerSida + 1);
                if (Ledigt(lane, x, null)) { bil.Placera(x, lane, korfaltsBredd); placerad = true; }
                else x += 8f;
            }
            if (!placerad) bil.Placera(x, 0, korfaltsBredd);
            bilar.Add(bil);
            x += Slump(minAvstand * 0.6f, minAvstand * 1.6f);
        }
    }

    float Slump(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
    float NyFart() => motorcykel.hastighet / Slump(minFartskvot, maxFartskvot);
    int MotorcykelnsKorfalt => Mathf.RoundToInt(motorcykel.transform.position.z / korfaltsBredd);

    // ledigt om ingen annan bil är i (eller på väg till) körfältet nära x, och motorcykeln inte är där
    bool Ledigt(int lane, float x, TrafikBil utom, float bakom = 18f, float fram = 26f)
    {
        foreach (var b in bilar)
        {
            if (b == utom) continue;
            if ((b.korfalt == lane || b.malKorfalt == lane) && b.X > x - bakom && b.X < x + fram) return false;
        }
        float mx = motorcykel.transform.position.x;
        if (MotorcykelnsKorfalt == lane && mx > x - 20f && mx < x + 15f) return false;
        return true;
    }

    TrafikBil NarmastFramfor(TrafikBil bil, int lane, out float avstand)
    {
        TrafikBil basta = null; avstand = float.MaxValue;
        foreach (var b in bilar)
        {
            if (b == bil || (b.korfalt != lane && b.malKorfalt != lane)) continue;
            float d = b.X - bil.X;
            if (d > 0f && d < avstand) { avstand = d; basta = b; }
        }
        return basta;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;   // spelet är pausat
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
                int riktning = rnd.Next(2) == 0 ? -1 : 1;
                for (int k = 0; k < 2; k++, riktning = -riktning)
                {
                    int lane = bil.korfalt + riktning;
                    if (lane < -korfaltPerSida || lane > korfaltPerSida) continue;
                    if (Ledigt(lane, bil.X, bil)) { bil.malKorfalt = lane; break; }
                }
            }

            bil.Simulera(dt, korfaltsBredd);

            // bilar långt bakom motorcykeln dyker upp igen långt fram
            if (bil.X < mx - bakomGrans) FlyttaFram(bil, mx);
        }
    }

    void FlyttaFram(TrafikBil bil, float mx)
    {
        for (int forsok = 0; forsok < 30; forsok++)
        {
            float x = mx + Slump(nyBilFramfor.x, nyBilFramfor.y);
            int lane = rnd.Next(-korfaltPerSida, korfaltPerSida + 1);
            if (!Ledigt(lane, x, bil, minAvstand, minAvstand)) continue;
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
