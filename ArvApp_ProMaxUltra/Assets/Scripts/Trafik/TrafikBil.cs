using UnityEngine;

// En bil i trafiken. Styrs av TrafikHanterare, som anropar Simulera() varje bildruta.
public class TrafikBil : MonoBehaviour
{
    [Tooltip("Farten bilen vill hålla (m/s).")]
    public float onskadFart = 22f;
    [Tooltip("Nuvarande fart (m/s).")]
    public float fart = 22f;
    [Tooltip("Körfältet bilen är i: -1, 0 eller 1 räknat från körbanans mitt.")]
    public int korfalt;
    [Tooltip("Körfältet bilen är på väg till (samma som korfalt när den inte byter).")]
    public int malKorfalt;
    [Tooltip("Ungefär hur lång tid ett körfältsbyte tar (sekunder).")]
    public float bytesTid = 2.6f;

    [Header("Körbana")]
    [Tooltip("+1 = kör åt samma håll som motorcykeln (+X), -1 = mötande trafik (-X).")]
    public int riktning = 1;
    [Tooltip("Z-läget för körbanans mittfil.")]
    public float mittZ;
    [Tooltip("Vägytans höjd vid mittfilen och lutning i sidled (vägen lutar 2,5 %).")]
    public float vagHojd, vagLutning;

    [HideInInspector] public float tidTillByte;

    public bool ByterKorfalt => korfalt != malKorfalt;
    public float X => transform.position.x;

    float z, zFart;
    Transform[] hjul;
    float[] hjulRadie;

    void Awake()
    {
        var lista = new System.Collections.Generic.List<Transform>();
        foreach (var t in GetComponentsInChildren<Transform>())
            if (t.name.StartsWith("Wheel_")) lista.Add(t);
        hjul = lista.ToArray();
        hjulRadie = new float[hjul.Length];
        for (int i = 0; i < hjul.Length; i++) hjulRadie[i] = hjul[i].name.Contains("_R") ? 0.346f : 0.327f;
    }

    float Y(float zz) => vagHojd + vagLutning * (zz - mittZ);

    public void Placera(float x, int lane, float korfaltsBredd)
    {
        korfalt = malKorfalt = lane;
        z = mittZ + lane * korfaltsBredd; zFart = 0f;
        transform.SetPositionAndRotation(new Vector3(x, Y(z), z), Quaternion.Euler(0f, riktning > 0 ? 0f : 180f, 0f));
    }

    public void Simulera(float dt, float korfaltsBredd)
    {
        float malZ = mittZ + malKorfalt * korfaltsBredd;
        z = Mathf.SmoothDamp(z, malZ, ref zFart, bytesTid * 0.35f);
        if (ByterKorfalt && Mathf.Abs(z - malZ) < 0.25f) korfalt = malKorfalt;

        var pos = transform.position;
        pos.x += riktning * fart * dt;
        pos.y = Y(z);
        pos.z = z;
        // bilen pekar i färdriktningen (inklusive den lilla sidledsrörelsen vid filbyte)
        float gir = Mathf.Atan2(-zFart, riktning * Mathf.Max(fart, 0.1f)) * Mathf.Rad2Deg;
        transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, gir, 0f));

        // hjulen rullar runt bilens tväraxel
        Vector3 axel = transform.rotation * Vector3.forward;
        for (int i = 0; i < hjul.Length; i++)
            hjul[i].Rotate(axel, -fart / hjulRadie[i] * Mathf.Rad2Deg * dt, Space.World);
    }

    public void FlyttaX(float dx) => transform.position += new Vector3(dx, 0f, 0f);
}
