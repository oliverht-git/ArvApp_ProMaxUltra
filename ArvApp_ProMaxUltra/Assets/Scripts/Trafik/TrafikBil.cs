using UnityEngine;

// En bil i trafiken. Styrs av TrafikHanterare, som anropar Simulera() varje bildruta.
public class TrafikBil : MonoBehaviour
{
    [Tooltip("Farten bilen vill hålla (m/s).")]
    public float onskadFart = 22f;
    [Tooltip("Nuvarande fart (m/s).")]
    public float fart = 22f;
    [Tooltip("Körfältet bilen är i: -1 = höger, 0 = mitten, 1 = vänster.")]
    public int korfalt;
    [Tooltip("Körfältet bilen är på väg till (samma som korfalt när den inte byter).")]
    public int malKorfalt;
    [Tooltip("Ungefär hur lång tid ett körfältsbyte tar (sekunder).")]
    public float bytesTid = 2.6f;

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

    public void Placera(float x, int lane, float korfaltsBredd)
    {
        korfalt = malKorfalt = lane;
        z = lane * korfaltsBredd; zFart = 0f;
        transform.SetPositionAndRotation(new Vector3(x, 0f, z), Quaternion.identity);
    }

    public void Simulera(float dt, float korfaltsBredd)
    {
        float malZ = malKorfalt * korfaltsBredd;
        z = Mathf.SmoothDamp(z, malZ, ref zFart, bytesTid * 0.35f);
        if (ByterKorfalt && Mathf.Abs(z - malZ) < 0.25f) korfalt = malKorfalt;

        var pos = transform.position;
        pos.x += fart * dt;
        pos.y = 0f;
        pos.z = z;
        float gir = -Mathf.Atan2(zFart, Mathf.Max(fart, 0.1f)) * Mathf.Rad2Deg;
        transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, gir, 0f));

        // hjulen rullar runt bilens tväraxel
        Vector3 axel = transform.rotation * Vector3.forward;
        for (int i = 0; i < hjul.Length; i++)
            hjul[i].Rotate(axel, -fart / hjulRadie[i] * Mathf.Rad2Deg * dt, Space.World);
    }

    public void FlyttaX(float dx) => transform.position += new Vector3(dx, 0f, 0f);
}
