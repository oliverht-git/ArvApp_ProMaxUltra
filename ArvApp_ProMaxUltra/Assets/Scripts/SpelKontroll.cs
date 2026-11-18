using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Håller koll på spelet: om motorcykeln kör in i en bil förlorar man.
// Då stannar trafiken, motorcykeln välter och glider på sidan, och när den har stannat
// visas "Du förlorade" med hur långt man kom.
// Starta om med R, Mellanslag eller Enter.
public class SpelKontroll : MonoBehaviour
{
    public MCTilt motorcykel;
    public TrafikHanterare trafik;

    [Header("Krockyta (meter)")]
    [Tooltip("Motorcykelns längd och bredd som räknas vid krock.")]
    public Vector2 motorcykelStorlek = new Vector2(2.0f, 0.7f);
    [Tooltip("En bils längd och bredd som räknas vid krock.")]
    public Vector2 bilStorlek = new Vector2(4.5f, 1.95f);

    public bool SpeletSlut { get; private set; }
    public float Distans { get; private set; }

    float tidSedanKrasch;
    GUIStyle rubrik, text;

    void Start()
    {
        if (motorcykel == null) motorcykel = FindFirstObjectByType<MCTilt>();
        if (trafik == null) trafik = FindFirstObjectByType<TrafikHanterare>();
    }

    void Update()
    {
        if (motorcykel == null || Time.deltaTime <= 0f) return;

        if (SpeletSlut)
        {
            // texten visas först när motorcykeln har vält och glidit klart
            if (!motorcykel.Stannat) return;
            tidSedanKrasch += Time.deltaTime;
            var tb = Keyboard.current;
            if (tidSedanKrasch > 0.8f && tb != null &&
                (tb.rKey.wasPressedThisFrame || tb.spaceKey.wasPressedThisFrame || tb.enterKey.wasPressedThisFrame))
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }

        Distans += motorcykel.hastighet * Time.deltaTime;

        if (trafik == null) return;
        Vector3 mc = motorcykel.transform.position;
        float maxDx = (motorcykelStorlek.x + bilStorlek.x) * 0.5f;
        float maxDz = (motorcykelStorlek.y + bilStorlek.y) * 0.5f;
        foreach (var bil in trafik.Bilar)
        {
            Vector3 b = bil.transform.position;
            if (Mathf.Abs(b.x - mc.x) < maxDx && Mathf.Abs(b.z - mc.z) < maxDz)
            {
                Forlora();
                break;
            }
        }
    }

    void Forlora()
    {
        SpeletSlut = true;
        tidSedanKrasch = 0f;
        motorcykel.Krascha();
        trafik.pausad = true;
    }

    void OnGUI()
    {
        if (!SpeletSlut || !motorcykel.Stannat) return;
        if (rubrik == null)
        {
            rubrik = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            text = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
        }
        rubrik.fontSize = Mathf.RoundToInt(Screen.height * 0.12f);
        text.fontSize = Mathf.RoundToInt(Screen.height * 0.045f);

        float a = Mathf.Clamp01(tidSedanKrasch / 0.6f);
        GUI.color = new Color(0f, 0f, 0f, 0.55f * a);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);

        float h = Screen.height;
        GUI.color = new Color(1f, 0.25f, 0.2f, a);
        GUI.Label(new Rect(0, h * 0.22f, Screen.width, h * 0.18f), "DU FÖRLORADE", rubrik);
        GUI.color = new Color(1f, 1f, 1f, a);
        GUI.Label(new Rect(0, h * 0.44f, Screen.width, h * 0.08f), $"Du körde {Distans / 1000f:0.00} km", text);
        if (tidSedanKrasch > 0.8f)
            GUI.Label(new Rect(0, h * 0.58f, Screen.width, h * 0.08f), "Tryck R eller Mellanslag för att köra igen", text);
        GUI.color = Color.white;
    }
}
