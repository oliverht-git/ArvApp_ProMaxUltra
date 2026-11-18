using UnityEngine;

// Följer motorcykeln rakt bakifrån, lite ovanifrån och tittar snett nedåt mot den.
public class KameraFoljare : MonoBehaviour
{
    public Transform mal;
    [Tooltip("Avstånd bakom motorcykeln (meter).")]
    public float avstandBakom = 6.5f;
    [Tooltip("Kamerans höjd över vägen (meter).")]
    public float hojd = 2.8f;
    [Tooltip("Punkt framför motorcykeln som kameran tittar mot (meter).")]
    public float tittaFramfor = 4f;
    public float tittaHojd = 0.6f;
    [Tooltip("Hur mycket kameran följer med i sidled vid körfältsbyte (0-1).")]
    [Range(0f, 1f)] public float sidledsFoljning = 0.85f;
    public float sidledsMjukhet = 0.25f;

    float z, zFart;

    void Start()
    {
        if (mal == null)
        {
            var mc = FindFirstObjectByType<MCTilt>();
            if (mc != null) mal = mc.transform;
        }
        if (mal != null) z = mal.position.z * sidledsFoljning;
        LateUpdate();
    }

    void LateUpdate()
    {
        if (mal == null) return;
        if (Time.deltaTime <= 0f && Application.isPlaying) return;   // spelet är pausat
        Vector3 p = mal.position;
        z = Mathf.SmoothDamp(z, p.z * sidledsFoljning, ref zFart, sidledsMjukhet);
        transform.position = new Vector3(p.x - avstandBakom, hojd, z);
        transform.LookAt(new Vector3(p.x + tittaFramfor, tittaHojd, Mathf.Lerp(z, p.z, 0.5f)));
    }
}
