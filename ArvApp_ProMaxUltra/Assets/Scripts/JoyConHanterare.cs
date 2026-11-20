using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.HID;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

// Läser en Nintendo Joy-Con (vänster eller höger) som är ansluten via Bluetooth.
//
// En Joy-Con startar i ett enkelt läge där spaken bara skickas som 8 riktningar och inget gyro.
// Det här skriptet skickar två kommandon till den (slå på gyrot, byt till fullt rapportläge 0x30)
// och läser sedan rådata direkt ur rapporterna: knappar, spak (12 bitar) och gyro.
//
// Gyrot används som laserpekare: rikta kontrollen mot skärmen så flyttar sig en röd prick,
// och tryck A/ZR (höger) eller vänster pil/ZL (vänster) för att klicka på knappen under pricken.
// Tryck + / − eller spaken för att centrera pricken igen (gyrot glider lite med tiden).
[DefaultExecutionOrder(-100)]   // före andra skript, så de ser KlickTryckt och Spak samma bildruta
public class JoyConHanterare : MonoBehaviour
{
    public static JoyConHanterare Instans { get; private set; }

    [Header("Laserpekare")]
    [Tooltip("Den röda pricken (en UI-bild i en Canvas).")]
    public RectTransform prick;
    [Tooltip("Pricken visas bara när det här objektet är aktivt, t.ex. VehiclePicker. Tomt = alltid.")]
    public GameObject visaBaraNar;
    [Tooltip("Hur många grader man behöver vrida kontrollen för att gå över hela skärmens bredd.")]
    public float graderOverSkarmen = 40f;
    [Tooltip("Byt håll i sidled om pricken går åt fel håll.")]
    public bool inverteraSidled = false;
    [Tooltip("Byt håll upp/ner om pricken går åt fel håll.")]
    public bool inverteraHojd = false;
    [Tooltip("Gyro-rörelser långsammare än så här (grader/sekund) räknas som stillastående, så pricken inte glider.")]
    public float gyroDodzon = 1.5f;

    [Header("Spak")]
    [Tooltip("Hur långt från mitten spaken går (rådata, mitten är ungefär 2048).")]
    public float spakOmfang = 1300f;

    /// <summary>Sant när Joy-Con:en är ansluten och skickar fullständiga rapporter.</summary>
    public bool Ansluten => device != null && Time.unscaledTime - senasteRapport < 0.5f;
    /// <summary>Spaken, -1..1 i båda led (x = höger, y = upp).</summary>
    public Vector2 Spak { get; private set; }
    /// <summary>Sant den bildruta klick-knappen (A/ZR eller vänster pil/ZL) trycks ner.</summary>
    public bool KlickTryckt { get; private set; }
    /// <summary>Prickens position i skärmpixlar.</summary>
    public Vector2 PekarPosition => new Vector2(Screen.width * (0.5f + girVinkel / graderOverSkarmen),
                                                Screen.height * 0.5f + Screen.width * (lutVinkel / graderOverSkarmen));

    const int Nintendo = 0x057E, VansterJoyCon = 0x2006, HogerJoyCon = 0x2007;
    const float GyroGraderPerEnhet = 0.06103f;   // ±2000 °/s
    const float TidPerGyroprov = 0.005f;         // 3 prov per rapport, 5 ms mellan

    InputDevice device;
    bool hoger;
    byte paketRaknare;
    float senasteKommando, senasteRapport = -10f;
    int kommandoSteg;

    // senaste lästa data (skrivs i onEvent, läses i Update)
    uint knappar, forraKnappar;
    int spakX = 2048, spakY = 2048;
    float girVinkel, lutVinkel;   // grader från mitten

    void Awake()
    {
        Instans = this;
        if (prick) prick.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        InputSystem.onEvent += OnEvent;
        InputSystem.onDeviceChange += OnDeviceChange;
        HittaJoyCon();
    }

    void OnDisable()
    {
        InputSystem.onEvent -= OnEvent;
        InputSystem.onDeviceChange -= OnDeviceChange;
        if (Instans == this) Instans = null;
    }

    void OnDeviceChange(InputDevice d, InputDeviceChange change)
    {
        if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected) HittaJoyCon();
        if ((change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected) && d == device) device = null;
    }

    void HittaJoyCon()
    {
        if (device != null) return;
        foreach (var d in InputSystem.devices)
        {
            if (d.description.interfaceName != "HID") continue;
            var hid = HID.HIDDeviceDescriptor.FromJson(d.description.capabilities);
            if (hid.vendorId != Nintendo || (hid.productId != VansterJoyCon && hid.productId != HogerJoyCon)) continue;
            device = d;
            hoger = hid.productId == HogerJoyCon;
            kommandoSteg = 0;
            senasteKommando = -10f;
            Debug.Log($"Joy-Con hittad: {(hoger ? "höger" : "vänster")} ({d.displayName})");
            return;
        }
    }

    /// <summary>Sant om den här enheten är Joy-Con:en (dess vanliga HID-tolkning är fel i fullt läge).</summary>
    public bool ArJoyCon(InputDevice d) => d != null && d == device;

    void Update()
    {
        if (device == null) { if (prick) prick.gameObject.SetActive(false); return; }

        // Skicka uppstartskommandona tills fullständiga rapporter kommer (och igen om de slutar komma).
        if (!Ansluten && Time.unscaledTime - senasteKommando > 0.3f)
        {
            senasteKommando = Time.unscaledTime;
            if (kommandoSteg % 2 == 0) SkickaUnderkommando(0x40, 0x01);   // slå på gyro/accelerometer
            else SkickaUnderkommando(0x03, 0x30);                         // fullt rapportläge, 60 Hz
            kommandoSteg++;
        }

        // Knappar
        uint klickMask = hoger ? (0x08u | 0x80u) : ((0x08u | 0x80u) << 16);   // A+ZR / vänster pil+ZL
        uint centreraMask = (0x01u | 0x02u | 0x04u | 0x08u) << 8;               // − + och spaktryck
        KlickTryckt = (knappar & klickMask) != 0 && (forraKnappar & klickMask) == 0;
        if ((knappar & centreraMask) != 0 && (forraKnappar & centreraMask) == 0) Centrera();
        forraKnappar = knappar;

        // Spak (rådata 0..4095 med mitten runt 2048)
        var s = new Vector2((spakX - 2048) / spakOmfang, (spakY - 2048) / spakOmfang);
        if (s.magnitude < 0.15f) s = Vector2.zero;
        Spak = Vector2.ClampMagnitude(s, 1f);

        // Laserpekare
        float maxGir = graderOverSkarmen * 0.5f;
        float maxLut = maxGir * Screen.height / Mathf.Max(1, Screen.width);
        girVinkel = Mathf.Clamp(girVinkel, -maxGir, maxGir);
        lutVinkel = Mathf.Clamp(lutVinkel, -maxLut, maxLut);

        bool visa = Ansluten && (visaBaraNar == null || visaBaraNar.activeInHierarchy);
        if (prick)
        {
            prick.gameObject.SetActive(visa);
            if (visa) { prick.position = PekarPosition; prick.SetAsLastSibling(); }
        }
        if (visa && KlickTryckt) KlickaPa(PekarPosition);
    }

    /// <summary>Flyttar pricken till mitten av skärmen.</summary>
    public void Centrera() { girVinkel = 0f; lutVinkel = 0f; }

    static void KlickaPa(Vector2 skarmPos)
    {
        var es = EventSystem.current;
        if (es == null) return;
        var data = new PointerEventData(es) { position = skarmPos, button = PointerEventData.InputButton.Left };
        var traffar = new List<RaycastResult>();
        es.RaycastAll(data, traffar);
        foreach (var t in traffar)
        {
            var mal = ExecuteEvents.GetEventHandler<IPointerClickHandler>(t.gameObject);
            if (mal == null) continue;
            data.pointerCurrentRaycast = t;
            data.pointerPress = mal;
            ExecuteEvents.Execute(mal, data, ExecuteEvents.pointerClickHandler);
            return;
        }
    }

    // ---------- Rådata från Joy-Con:en ----------

    unsafe void OnEvent(InputEventPtr ev, InputDevice d)
    {
        if (d == null || d != device || !ev.IsA<StateEvent>()) return;
        var state = StateEvent.From(ev);
        int storlek = (int)state->stateSizeInBytes;
        byte* r = (byte*)state->state;
        if (storlek < 49 || r[0] != 0x30) return;   // bara fullständiga rapporter (0x30)
        senasteRapport = Time.unscaledTime;

        knappar = (uint)(r[3] | (r[4] << 8) | (r[5] << 16));
        byte* st = hoger ? r + 9 : r + 6;
        spakX = st[0] | ((st[1] & 0x0F) << 8);
        spakY = (st[1] >> 4) | (st[2] << 4);

        // 3 gyroprov à 12 byte (accel x,y,z, gyro x,y,z) med start på byte 13
        for (int i = 0; i < 3; i++)
        {
            byte* p = r + 13 + i * 12;
            float gy = (short)(p[8] | (p[9] << 8)) * GyroGraderPerEnhet;    // lutning upp/ner
            float gz = (short)(p[10] | (p[11] << 8)) * GyroGraderPerEnhet;  // vridning åt sidan
            if (Mathf.Abs(gz) > gyroDodzon) girVinkel += (inverteraSidled ? -gz : gz) * TidPerGyroprov;
            if (Mathf.Abs(gy) > gyroDodzon) lutVinkel += (inverteraHojd ? -gy : gy) * TidPerGyroprov;
        }
    }

    void SkickaUnderkommando(byte kommando, byte argument)
    {
        if (device == null) return;
        var c = JoyConUtRapport.Skapa();
        unsafe
        {
            c.data[0] = 0x01;                          // rapport-ID: underkommando
            c.data[1] = (byte)(paketRaknare++ & 0x0F);
            byte[] vila = { 0x00, 0x01, 0x40, 0x40, 0x00, 0x01, 0x40, 0x40 };   // ingen vibration
            for (int i = 0; i < 8; i++) c.data[2 + i] = vila[i];
            c.data[10] = kommando;
            c.data[11] = argument;
        }
        device.ExecuteCommand(ref c);
    }

    [StructLayout(LayoutKind.Explicit, Size = Storlek)]
    unsafe struct JoyConUtRapport : IInputDeviceCommandInfo
    {
        const int RapportStorlek = 49;
        const int Storlek = InputDeviceCommand.BaseCommandSize + RapportStorlek;
        public static FourCC Typ => new FourCC('H', 'I', 'D', 'O');
        public FourCC typeStatic => Typ;

        [FieldOffset(0)] public InputDeviceCommand baseCommand;
        [FieldOffset(InputDeviceCommand.BaseCommandSize)] public fixed byte data[RapportStorlek];

        public static JoyConUtRapport Skapa() => new JoyConUtRapport
        {
            baseCommand = new InputDeviceCommand(Typ, Storlek)
        };
    }
}
