// Builds a realistic 3-lane motorway (both carriageways) around the scene origin.
// Menu: Tools > Highway > Build Highway.  Re-running replaces the previous build.
//
// Layout (metres, Unity axes): traffic on "our" carriageway drives along +X,
// the middle lane is centred on z = 0, the hard shoulder is on the right (-Z),
// the median is on the left (+Z) and the opposite carriageway lies beyond it.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Random = System.Random;

public static class HighwayBuilder
{
    const string Dir = "Assets/Environment/Highway";
    const float HalfLength = 1200f;    // road runs from x = -1200 to +1200
    // Everything along X repeats every Period metres, so the motorcycle can be moved back
    // by exactly one period (see MCTilt) without any visible jump -> endless motorway.
    public const float Period = 600f;
    const float ForestHalfLength = 1000f;
    const float CrossFall = 0.025f;    // 2.5 % crossfall towards the hard shoulder
    const float LaneWidth = 3.5f;

    // --- cross-section --------------------------------------------------------
    // Carriageway A (ours): hard shoulder -8.0..-5.25, lanes -5.25..5.25, inner shoulder 5.25..6.25
    static float YA(float z) => CrossFall * z;
    static float YMedianEdge => YA(6.25f);
    // Carriageway B (opposite): inner shoulder 10.25..11.25, lanes 11.25..21.75, hard shoulder 21.75..24.5
    static float YB(float z) => YMedianEdge - CrossFall * (z - 10.25f);
    const float VergeY = -0.23f;       // level where the outer gravel meets the grass

    [MenuItem("Tools/Highway/Build Highway")]
    public static void BuildMenu() => Debug.Log(Build(true));

    public static string Build(bool regenerateTextures)
    {
        var log = new System.Text.StringBuilder();
        Directory.CreateDirectory(Dir + "/Textures");
        Directory.CreateDirectory(Dir + "/Materials");
        Directory.CreateDirectory(Dir + "/Meshes");
        AssetDatabase.Refresh();
        try
        {
            if (regenerateTextures || !File.Exists(Dir + "/Textures/Asphalt_Albedo.png"))
            {
                Progress("Generating asphalt textures", 0.05f); MakeAsphalt(2048);
                Progress("Generating grass textures", 0.30f); MakeGrass(1024);
                Progress("Generating gravel textures", 0.45f); MakeGravel(1024);
                Progress("Generating paint textures", 0.55f); MakePaint(512);
                Progress("Generating steel textures", 0.60f); MakeGalvanized(512);
                Progress("Generating tree textures", 0.65f); MakeNeedles(512); MakeBark(256);
                AssetDatabase.Refresh();
                ConfigureTextures();
            }
            Progress("Creating materials", 0.70f);
            var mAsphalt = LitMat("Asphalt", "Asphalt", 1.0f, 1.0f, 0f);
            var mPaint = LitMat("RoadPaint", "Paint", 0.7f, 1.0f, 0f);
            var mGravel = LitMat("Gravel", "Gravel", 1.2f, 1.0f, 0f);
            var mGrass = LitMat("Grass", "Grass", 1.0f, 1.0f, 0f);
            var mSteel = LitMat("GalvanizedSteel", "Galvanized", 0.6f, 1.0f, 1f);
            var mNeedles = LitMat("SpruceNeedles", "Needles", 1.0f, 1.0f, 0f);
            var mBark = LitMat("Bark", "Bark", 1.0f, 1.0f, 0f);

            Progress("Building road geometry", 0.75f);
            var old = GameObject.Find("Highway");
            if (old) UnityEngine.Object.DestroyImmediate(old);
            var showroom = GameObject.Find("Showroom_Floor");      // test floor from the vehicle import
            if (showroom) UnityEngine.Object.DestroyImmediate(showroom);
            var root = new GameObject("Highway");

            AddObj(root, "Asphalt", BuildAsphalt(), mAsphalt, true);
            AddObj(root, "RoadMarkings", BuildMarkings(), mPaint, false);
            AddObj(root, "GravelVerges", BuildGravel(), mGravel, false);
            AddObj(root, "Ground", BuildGround(), mGrass, true);
            AddObj(root, "Guardrails", BuildGuardrails(), mSteel, false);

            Progress("Planting forest", 0.85f);
            var forest = new GameObject("Forest"); forest.transform.SetParent(root.transform, false);
            int trees = BuildForest(forest, mNeedles, mBark);
            log.AppendLine($"Forest: {trees} trees");

            Progress("Lighting and post-processing", 0.93f);
            SetupLighting(log);
            PlaceBikeAndCamera(log);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            log.Insert(0, "Highway built.\n");
        }
        finally { EditorUtility.ClearProgressBar(); }
        return log.ToString();
    }

    static void Progress(string s, float p) => EditorUtility.DisplayProgressBar("Highway", s, p);

    // ===========================================================================
    // Procedural, seamlessly tiling textures
    // ===========================================================================
    static uint Hash(int x, int y, int s)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)s * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
    }
    static float H01(int x, int y, int s) => (Hash(x, y, s) & 0xFFFFFF) / 16777215f;
    static int Wrap(int a, int p) { int r = a % p; return r < 0 ? r + p : r; }
    static float Sm(float t) => t * t * (3f - 2f * t);
    static float C01(float t) => Mathf.Clamp01(t);

    // periodic value noise; x,y in lattice units, periods px/py
    static float VNoise(float x, float y, int px, int py, int s)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = Sm(x - x0), fy = Sm(y - y0);
        int xa = Wrap(x0, px), xb = Wrap(x0 + 1, px), ya = Wrap(y0, py), yb = Wrap(y0 + 1, py);
        float a = H01(xa, ya, s), b = H01(xb, ya, s), c = H01(xa, yb, s), d = H01(xb, yb, s);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }
    // periodic fBm on the unit tile (u,v in [0,1))
    static float Fbm(float u, float v, int period, int octaves, int s)
    {
        float sum = 0, amp = 0.5f, norm = 0; int p = period;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * VNoise(u * p, v * p, p, p, s + i * 31);
            norm += amp; amp *= 0.5f; p *= 2;
        }
        return sum / norm;
    }
    // periodic Worley (cellular) noise with n cells across the tile
    static void Worley(float u, float v, int n, int s, out float f1, out float f2, out int id)
    {
        float x = u * n, y = v * n; int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
        f1 = f2 = 9f; id = 0;
        for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int gx = cx + i, gy = cy + j, wx = Wrap(gx, n), wy = Wrap(gy, n);
                float px = gx + 0.1f + 0.8f * H01(wx, wy, s), py = gy + 0.1f + 0.8f * H01(wx, wy, s + 1);
                float dx = px - x, dy = py - y, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d < f1) { f2 = f1; f1 = d; id = wx * 7919 + wy; }
                else if (d < f2) f2 = d;
            }
    }

    class TexSet
    {
        public int n; public Color[] col; public float[] h, smooth, ao;
        public TexSet(int size) { n = size; col = new Color[n * n]; h = new float[n * n]; smooth = new float[n * n]; ao = new float[n * n]; }
    }

    // writes <name>_Albedo.png (sRGB), _Normal.png, _Mask.png (G = AO, A = smoothness)
    static void Save(string name, TexSet t, float normalStrength)
    {
        int n = t.n;
        var alb = new Color32[n * n]; var nrm = new Color32[n * n]; var msk = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x; var c = t.col[i];
                alb[i] = new Color32(G(c.r), G(c.g), G(c.b), 255);
                float dx = t.h[y * n + Wrap(x + 1, n)] - t.h[y * n + Wrap(x - 1, n)];
                float dy = t.h[Wrap(y + 1, n) * n + x] - t.h[Wrap(y - 1, n) * n + x];
                var nv = new Vector3(-dx * normalStrength, -dy * normalStrength, 1f).normalized;
                nrm[i] = new Color32(B(nv.x * 0.5f + 0.5f), B(nv.y * 0.5f + 0.5f), B(nv.z * 0.5f + 0.5f), 255);
                msk[i] = new Color32(0, B(t.ao[i]), 0, B(t.smooth[i]));
            }
        Write(name + "_Albedo", alb, n); Write(name + "_Normal", nrm, n); Write(name + "_Mask", msk, n);
    }
    static byte B(float f) => (byte)Mathf.RoundToInt(C01(f) * 255f);
    static byte G(float linear) => B(Mathf.LinearToGammaSpace(C01(linear)));
    static void Write(string file, Color32[] px, int n)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false, true);
        tex.SetPixels32(px); tex.Apply();
        File.WriteAllBytes($"{Dir}/Textures/{file}.png", tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
    }

    // Asphalt concrete (AC 16-ish): 2 m tile, aggregate in a dark bitumen binder
    static void MakeAsphalt(int n)
    {
        var t = new TexSet(n);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n; int i = y * n + x;
                Worley(u, v, 150, 11, out float f1, out float f2, out int id);          // coarse aggregate ~13 mm
                float edge = f2 - f1;
                float has = H01(id, 3, 5) < 0.88f ? 1f : 0f;
                float stone = Sm(C01((edge - 0.05f) / 0.25f)) * has;
                Worley(u, v, 480, 23, out float g1, out float g2, out int gid);         // fines / sand
                float fine = Sm(C01((g2 - g1 - 0.04f) / 0.3f));
                float tone = H01(id, 1, 9);
                var stoneC = new Color(0.075f + 0.20f * tone, 0.075f + 0.20f * tone, 0.078f + 0.21f * tone);
                if (H01(id, 2, 9) < 0.2f) stoneC = new Color(stoneC.r * 1.18f, stoneC.g * 1.02f, stoneC.b * 0.9f);  // granite/feldspar
                float ft = H01(gid, 4, 3);
                var fineC = new Color(0.035f + 0.06f * ft, 0.035f + 0.06f * ft, 0.037f + 0.06f * ft);
                var binder = new Color(0.018f, 0.018f, 0.019f);
                var c = Color.Lerp(Color.Lerp(binder, fineC, fine * 0.75f), stoneC, stone);
                float macro = 0.82f + 0.36f * Fbm(u, v, 3, 4, 101);                         // patchy weathering
                float oil = Sm(C01((Fbm(u, v, 4, 4, 141) - 0.62f) * 5f)) * 0.35f;          // drips / stains
                float grain = (VNoise(u * 1024, v * 1024, 1024, 1024, 77) - 0.5f) * 0.012f;
                c = c * macro * (1f - oil);
                c.r += grain; c.g += grain; c.b += grain;
                t.col[i] = c;
                t.h[i] = stone * (0.55f + 0.45f * Mathf.Sqrt(C01(edge * 2f))) + fine * 0.22f;
                t.smooth[i] = Mathf.Lerp(0.08f + 0.06f * oil, 0.20f + 0.18f * H01(id, 6, 9), stone);  // polished aggregate tops
                t.ao[i] = Mathf.Lerp(0.5f, 1f, Mathf.Max(stone, fine * 0.55f));
            }
        Save("Asphalt", t, 6f);
    }

    // Grass verge, 4 m tile
    static void MakeGrass(int n)
    {
        var t = new TexSet(n);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n; int i = y * n + x;
                float patch = Fbm(u, v, 4, 5, 201);
                float dry = Sm(C01((Fbm(u, v, 6, 4, 301) - 0.45f) * 3f));
                float b1 = VNoise(u * 384, v * 48, 384, 48, 211);
                float b2 = VNoise(u * 56, v * 352, 56, 352, 223);
                float b3 = VNoise((u + v) * 300, (u - v + 1f) * 40, 300, 40, 227);
                float blades = Mathf.Max(b1, Mathf.Max(b2, b3));
                blades = Mathf.Pow(blades, 2.2f);
                var green = Color.Lerp(new Color(0.030f, 0.060f, 0.014f), new Color(0.085f, 0.125f, 0.030f), patch);
                var dryC = new Color(0.16f, 0.13f, 0.055f);
                var c = Color.Lerp(green, dryC, dry * 0.55f) * (0.45f + 1.0f * blades);
                float dirt = Sm(C01((Fbm(u, v, 3, 4, 401) - 0.64f) * 6f));
                c = Color.Lerp(c, new Color(0.075f, 0.058f, 0.038f) * (0.8f + 0.4f * b1), dirt * 0.8f);
                t.col[i] = c;
                t.h[i] = blades * 0.8f + patch * 0.2f;
                t.smooth[i] = 0.10f + 0.08f * blades;
                t.ao[i] = 0.55f + 0.45f * blades;
            }
        Save("Grass", t, 4f);
    }

    // Crushed-stone verge, 2 m tile
    static void MakeGravel(int n)
    {
        var t = new TexSet(n);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n; int i = y * n + x;
                Worley(u, v, 64, 31, out float f1, out float f2, out int id);
                float edge = f2 - f1, stone = Sm(C01((edge - 0.03f) / 0.3f));
                float tone = H01(id, 1, 31);
                var sc = new Color(0.11f + 0.25f * tone, 0.105f + 0.24f * tone, 0.10f + 0.22f * tone);
                var dirt = new Color(0.07f, 0.06f, 0.045f) * (0.8f + 0.4f * Fbm(u, v, 16, 3, 41));
                t.col[i] = Color.Lerp(dirt, sc, stone);
                t.h[i] = stone * (0.4f + 0.6f * Mathf.Sqrt(C01(edge * 2f)));
                t.smooth[i] = Mathf.Lerp(0.08f, 0.22f, stone);
                t.ao[i] = Mathf.Lerp(0.45f, 1f, stone);
            }
        Save("Gravel", t, 5f);
    }

    // Thermoplastic road paint with glass beads and wear, 2 m tile
    static void MakePaint(int n)
    {
        var t = new TexSet(n);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n; int i = y * n + x;
                float wear = Sm(C01((Fbm(u, v, 6, 5, 501) - 0.66f) * 5f));
                float bead = H01(x, y, 503) > 0.965f ? 1f : 0f;
                float tone = 0.62f + 0.12f * Fbm(u, v, 24, 3, 507);
                var c = Color.Lerp(new Color(tone, tone, tone * 0.98f), new Color(0.05f, 0.05f, 0.052f), wear);
                t.col[i] = c;
                t.h[i] = (1f - wear) * 0.5f + bead * 0.2f;
                t.smooth[i] = Mathf.Lerp(0.30f + 0.4f * bead, 0.12f, wear);
                t.ao[i] = 1f - wear * 0.3f;
            }
        Save("Paint", t, 3f);
    }

    // Hot-dip galvanized steel with spangle, 2 m tile
    static void MakeGalvanized(int n)
    {
        var t = new TexSet(n);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n; int i = y * n + x;
                Worley(u, v, 40, 61, out float f1, out float f2, out int id);
                float tone = 0.50f + 0.14f * H01(id, 1, 61);
                float grime = Sm(C01((Fbm(u, v, 4, 4, 67) - 0.55f) * 3f)) * 0.35f;
                t.col[i] = new Color(tone, tone * 1.01f, tone * 1.03f) * (1f - grime);
                t.h[i] = H01(id, 2, 61) * 0.3f;
                t.smooth[i] = Mathf.Lerp(0.62f, 0.35f, grime) * (0.9f + 0.1f * H01(id, 3, 61));
                t.ao[i] = 1f;
            }
        Save("Galvanized", t, 1.5f);
    }

    static void MakeNeedles(int n)
    {
        var t = new TexSet(n);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n; int i = y * n + x;
                float s1 = VNoise(u * 160, v * 20, 160, 20, 701), s2 = VNoise(u * 90, v * 60, 90, 60, 709);
                float k = Mathf.Pow(Mathf.Max(s1, s2), 1.8f);
                float tip = Fbm(u, v, 8, 3, 713);
                var c = Color.Lerp(new Color(0.010f, 0.025f, 0.012f), new Color(0.035f, 0.065f, 0.025f), k);
                c = Color.Lerp(c, new Color(0.05f, 0.075f, 0.03f), tip * 0.3f);
                t.col[i] = c; t.h[i] = k; t.smooth[i] = 0.12f; t.ao[i] = 0.5f + 0.5f * k;
            }
        Save("Needles", t, 6f);
    }

    static void MakeBark(int n)
    {
        var t = new TexSet(n);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n; int i = y * n + x;
                float k = Fbm(u * 1f, v, 6, 4, 801) * 0.4f + VNoise(u * 48, v * 6, 48, 6, 809) * 0.6f;
                t.col[i] = new Color(0.05f, 0.036f, 0.026f) * (0.6f + 0.8f * k);
                t.h[i] = k; t.smooth[i] = 0.08f; t.ao[i] = 0.6f + 0.4f * k;
            }
        Save("Bark", t, 8f);
    }

    static void ConfigureTextures()
    {
        foreach (var path in Directory.GetFiles(Dir + "/Textures", "*.png"))
        {
            var p = path.Replace('\\', '/');
            var imp = (TextureImporter)AssetImporter.GetAtPath(p);
            if (imp == null) continue;
            bool normal = p.EndsWith("_Normal.png"), mask = p.EndsWith("_Mask.png");
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !normal && !mask;
            imp.alphaSource = mask ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.anisoLevel = 8;
            imp.mipmapEnabled = true;
            imp.filterMode = FilterMode.Trilinear;
            imp.maxTextureSize = 2048;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
        }
    }

    static Material LitMat(string matName, string tex, float bumpScale, float smoothScale, float metallic)
    {
        string path = $"{Dir}/Materials/{matName}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        var alb = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/Textures/{tex}_Albedo.png");
        var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/Textures/{tex}_Normal.png");
        var msk = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/Textures/{tex}_Mask.png");
        m.SetTexture("_BaseMap", alb); m.SetColor("_BaseColor", Color.white);
        m.SetTexture("_BumpMap", nrm); m.SetFloat("_BumpScale", bumpScale); m.EnableKeyword("_NORMALMAP");
        m.SetTexture("_MetallicGlossMap", msk); m.EnableKeyword("_METALLICSPECGLOSSMAP");
        m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smoothScale);
        m.SetFloat("_SmoothnessTextureChannel", 0);
        m.SetTexture("_OcclusionMap", msk); m.SetFloat("_OcclusionStrength", 1f); m.EnableKeyword("_OCCLUSIONMAP");
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ===========================================================================
    // Geometry
    // ===========================================================================
    class MB
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> t = new List<int>();
        public int V(Vector3 p, Vector2 u) { v.Add(p); uv.Add(u); return v.Count - 1; }
        // adds quad a-b-c-d, flipping it if needed so its front face points along 'facing'
        public void Q(int a, int b, int c, int d, Vector3 facing)
        {
            var n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(n, facing) < 0) { int tmp = b; b = d; d = tmp; }
            t.Add(a); t.Add(b); t.Add(c); t.Add(a); t.Add(c); t.Add(d);
        }
        public void Tri(int a, int b, int c, Vector3 facing)
        {
            var n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(n, facing) < 0) { int tmp = b; b = c; c = tmp; }
            t.Add(a); t.Add(b); t.Add(c);
        }
        public Mesh Build(string name)
        {
            var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }
    }

    static void AddObj(GameObject parent, string name, Mesh mesh, Material mat, bool collider)
    {
        string path = $"{Dir}/Meshes/{name}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI);
    }

    // strip across the given z stations, running the full road length
    static void Strip(MB mb, float[] zs, Func<float, float> y, float yOff, float uvTile, float dx = 25f)
    {
        int cols = zs.Length, start = mb.v.Count, rows = 0;
        for (float x = -HalfLength; x <= HalfLength + 0.01f; x += dx, rows++)
            foreach (var z in zs) mb.V(new Vector3(x, y(z) + yOff, z), new Vector2(x / uvTile, z / uvTile));
        for (int r = 0; r < rows - 1; r++)
            for (int c = 0; c < cols - 1; c++)
            {
                int a = start + r * cols + c;
                mb.Q(a, a + 1, a + cols + 1, a + cols, Vector3.up);
            }
    }

    static Mesh BuildAsphalt()
    {
        var mb = new MB();
        Strip(mb, new[] { -8.0f, -5.55f, -5.25f, -1.75f, 1.75f, 5.25f, 5.45f, 6.25f }, YA, 0f, 2f);
        Strip(mb, new[] { 10.25f, 11.05f, 11.25f, 14.75f, 18.25f, 21.75f, 22.05f, 24.5f }, YB, 0f, 2f);
        return mb.Build("Asphalt");
    }

    static void Line(MB mb, float x0, float x1, float z0, float z1, Func<float, float> y)
    {
        const float off = 0.006f;
        int a = mb.V(new Vector3(x0, y(z0) + off, z0), new Vector2(x0 / 2f, z0 / 2f));
        int b = mb.V(new Vector3(x0, y(z1) + off, z1), new Vector2(x0 / 2f, z1 / 2f));
        int c = mb.V(new Vector3(x1, y(z1) + off, z1), new Vector2(x1 / 2f, z1 / 2f));
        int d = mb.V(new Vector3(x1, y(z0) + off, z0), new Vector2(x1 / 2f, z0 / 2f));
        mb.Q(a, b, c, d, Vector3.up);
    }

    static Mesh BuildMarkings()
    {
        var mb = new MB();
        // continuous edge lines (long lines split every 50 m to keep vertices close to the camera)
        for (float x = -HalfLength; x < HalfLength; x += 50f)
        {
            Line(mb, x, x + 50f, -5.55f, -5.25f, YA);   // A right edge line, 30 cm
            Line(mb, x, x + 50f, 5.25f, 5.45f, YA);     // A left edge line, 20 cm
            Line(mb, x, x + 50f, 11.05f, 11.25f, YB);   // B left edge line
            Line(mb, x, x + 50f, 21.75f, 22.05f, YB);   // B right edge line
        }
        // lane dividers: 3 m line, 9 m gap, 15 cm wide
        for (float x = -HalfLength; x < HalfLength; x += 12f)
        {
            foreach (var zc in new[] { -LaneWidth / 2f, LaneWidth / 2f }) Line(mb, x, x + 3f, zc - 0.075f, zc + 0.075f, YA);
            foreach (var zc in new[] { 14.75f, 18.25f }) Line(mb, x + 6f, x + 9f, zc - 0.075f, zc + 0.075f, YB);
        }
        return mb.Build("RoadMarkings");
    }

    static Mesh BuildGravel()
    {
        var mb = new MB();
        Strip(mb, new[] { -8.8f, -8.0f }, z => z < -8.4f ? VergeY + 0.01f : YA(-8f) - 0.02f, 0f, 2f);
        Strip(mb, new[] { 6.25f, 6.75f }, z => z < 6.5f ? YA(6.25f) - 0.02f : YMedianEdge - 0.03f, 0f, 2f);
        Strip(mb, new[] { 9.75f, 10.25f }, z => z < 10f ? YMedianEdge - 0.03f : YB(10.25f) - 0.02f, 0f, 2f);
        Strip(mb, new[] { 24.5f, 25.3f }, z => z < 24.9f ? YB(24.5f) - 0.02f : VergeY + 0.01f, 0f, 2f);
        return mb.Build("GravelVerges");
    }

    // terrain height: ditches next to the road, cut slopes, then rolling hills
    static float Ground(float x, float z)
    {
        const float aOut = -8.8f, bOut = 25.3f;
        if (z >= 6.75f && z <= 9.75f) return YMedianEdge - 0.03f - 0.22f * Mathf.Sin(Mathf.PI * (z - 6.75f) / 3f);
        if (z > aOut && z < bOut) return -0.6f;   // hidden under the pavement
        float d = z <= aOut ? aOut - z : z - bOut;
        if (d < 6f) return VergeY - 0.75f * Mathf.Sin(Mathf.PI * d / 6f);
        // lattice periods along X are chosen so the terrain repeats every 600 m
        float hills = (VNoise(x / 150f, z / 180f + 300f, (int)(Period / 150f), 4096, 9) - 0.35f) * 45f
                    + (VNoise(x / 60f, z / 60f + 300f, (int)(Period / 60f), 4096, 13) - 0.5f) * 8f;
        float bumps = (VNoise(x / 10f, z / 9f + 300f, (int)(Period / 10f), 4096, 17) - 0.5f) * 0.6f;
        return VergeY + 3.2f * Sm(C01((d - 6f) / 22f)) + hills * Sm(C01((d - 30f) / 90f)) + bumps * Sm(C01((d - 6f) / 10f));
    }

    static Mesh BuildGround()
    {
        var zs = new List<float>();
        for (float z = -560f; z < -60f; z += 20f) zs.Add(z);
        for (float z = -60f; z < -8.8f; z += 1.5f) zs.Add(z);
        zs.Add(-8.8f); zs.Add(-8.75f); zs.Add(6.7f);
        for (float z = 6.75f; z <= 9.76f; z += 0.5f) zs.Add(z);
        zs.Add(9.8f); zs.Add(25.25f); zs.Add(25.3f);
        for (float z = 26.8f; z < 80f; z += 1.5f) zs.Add(z);
        for (float z = 80f; z <= 600f; z += 20f) zs.Add(z);
        var mb = new MB();
        int cols = zs.Count, rows = 0;
        for (float x = -1800f; x <= 1800.1f; x += 10f, rows++)
            foreach (var z in zs) mb.V(new Vector3(x, Ground(x, z), z), new Vector2(x / 4f, z / 4f));
        for (int r = 0; r < rows - 1; r++)
            for (int c = 0; c < cols - 1; c++)
            {
                int a = r * cols + c;
                mb.Q(a, a + 1, a + cols + 1, a + cols, Vector3.up);
            }
        return mb.Build("Ground");
    }

    static void Box(MB mb, Vector3 c, Vector3 s)
    {
        Vector3 h = s * 0.5f;
        Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        foreach (var n in dirs)
        {
            Vector3 a = Vector3.Cross(n, Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up);
            Vector3 b = Vector3.Cross(n, a);
            Vector3 fc = c + Vector3.Scale(n, h);
            Vector3 ea = Vector3.Scale(a, h), eb = Vector3.Scale(b, h);
            int i0 = mb.V(fc - ea - eb, new Vector2(0, 0)), i1 = mb.V(fc + ea - eb, new Vector2(1, 0));
            int i2 = mb.V(fc + ea + eb, new Vector2(1, 1)), i3 = mb.V(fc - ea - eb + 2 * eb, new Vector2(0, 1));
            mb.Q(i0, i1, i2, i3, n);
        }
    }

    // W-beam guardrail: profile faces 'dir' (+1 = +Z), valleys at zFace, base at ground level yBase
    static void Rail(MB mb, float zFace, float dir, float yBase, bool posts)
    {
        const int P = 16; const float H = 0.31f, Bottom = 0.44f, Depth = 0.048f;
        var prof = new Vector2[P + 1];
        for (int k = 0; k <= P; k++)
        {
            float t = k / (float)P;
            prof[k] = new Vector2(zFace + dir * Depth * 0.5f * (1f - Mathf.Cos(4f * Mathf.PI * t)), yBase + Bottom + H * t);
        }
        for (float x = -HalfLength; x < HalfLength; x += 50f)
            for (int side = 0; side < 2; side++)
            {
                float zo = side == 0 ? 0f : -dir * 0.003f;
                var facing = new Vector3(0, 0, side == 0 ? dir : -dir);
                for (int k = 0; k < P; k++)
                {
                    var p0 = prof[k]; var p1 = prof[k + 1];
                    int a = mb.V(new Vector3(x, p0.y, p0.x + zo), new Vector2(x / 2f, (p0.y - yBase) / 2f));
                    int b = mb.V(new Vector3(x, p1.y, p1.x + zo), new Vector2(x / 2f, (p1.y - yBase) / 2f));
                    int c = mb.V(new Vector3(x + 50f, p1.y, p1.x + zo), new Vector2((x + 50f) / 2f, (p1.y - yBase) / 2f));
                    int d = mb.V(new Vector3(x + 50f, p0.y, p0.x + zo), new Vector2((x + 50f) / 2f, (p0.y - yBase) / 2f));
                    // facing for the profile segment: perpendicular to it within the YZ plane
                    var seg = new Vector3(0, p1.y - p0.y, p1.x - p0.x);
                    var nrm = Vector3.Cross(seg, Vector3.right).normalized;
                    if (Vector3.Dot(nrm, facing) < 0) nrm = -nrm;
                    mb.Q(a, b, c, d, nrm);
                }
            }
        if (!posts) return;
        float spacerZ = zFace - dir * (0.003f + 0.075f);
        float postZ = zFace - dir * (0.003f + 0.15f + 0.05f);
        for (float x = -HalfLength + 2f; x < HalfLength; x += 4f)
        {
            Box(mb, new Vector3(x, yBase + 0.33f, postZ), new Vector3(0.055f, 0.96f, 0.1f));        // sigma post, 0.3 m buried
            Box(mb, new Vector3(x, yBase + 0.6f, spacerZ), new Vector3(0.05f, 0.2f, 0.15f));         // spacer block
        }
    }

    static Mesh BuildGuardrails()
    {
        var mb = new MB();
        Rail(mb, -8.45f, +1f, VergeY + 0.01f, true);                 // our hard shoulder
        float medBase = Ground(0, 8.25f);
        Rail(mb, 8.0f, -1f, medBase, true);                           // median, facing our carriageway
        Rail(mb, 8.5f, +1f, medBase, false);                          // median, facing the opposite carriageway (shares posts)
        Rail(mb, 25.05f, -1f, VergeY + 0.01f, true);                  // opposite hard shoulder
        return mb.Build("Guardrails");
    }

    // ---------------------------------------------------------------------------
    // Spruce forest in chunks of 100 m along the road
    struct TreeSpec { public float x, z, h; public int seed; }

    static int BuildForest(GameObject parent, Material needles, Material bark)
    {
        // one 600 m tile of trees, repeated along the road so the forest is periodic too
        var rnd = new Random(1234);
        var tile = new List<TreeSpec>();
        for (float x = 0f; x < Period; x += 8f)
            foreach (var side in new[] { -1, 1 })
            {
                float zStart = side < 0 ? -34f : 50f, zEnd = side < 0 ? -290f : 320f;
                for (float z = zStart; side < 0 ? z > zEnd : z < zEnd; z += side * 8f)
                {
                    float px = x + (float)rnd.NextDouble() * 6.5f, pz = z + side * (float)rnd.NextDouble() * 6.5f;
                    float d = side < 0 ? -8.8f - pz : pz - 25.3f;
                    float density = VNoise(px / 75f, pz / 70f + 500f, (int)(Period / 75f), 4096, 21);
                    float need = d < 40f ? 0.62f : 0.42f;                      // sparser close to the road
                    if (density < need || rnd.NextDouble() > 0.75) continue;
                    float h = 11f + 13f * (float)rnd.NextDouble() * (0.6f + 0.6f * density);
                    tile.Add(new TreeSpec { x = px, z = pz, h = h, seed = rnd.Next() });
                }
            }

        int total = 0;
        for (float cx = -ForestHalfLength; cx < ForestHalfLength; cx += 100f)
        {
            var crown = new MB(); var trunk = new MB();
            foreach (var t in tile)
            {
                // place the copy of this tree that falls inside the current chunk
                float px = t.x + Mathf.Floor((cx - t.x) / Period) * Period;
                while (px < cx) px += Period;
                for (; px < cx + 100f; px += Period)
                {
                    Spruce(crown, trunk, new Vector3(px, Ground(px, t.z), t.z), t.h, t.seed);
                    total++;
                }
            }
            if (crown.v.Count == 0) continue;
            var chunk = new GameObject($"Forest_{cx:0}"); chunk.transform.SetParent(parent.transform, false);
            AddObj(chunk, $"Crowns_{cx:0}", crown.Build($"Crowns_{cx:0}"), needles, false);
            AddObj(chunk, $"Trunks_{cx:0}", trunk.Build($"Trunks_{cx:0}"), bark, false);
        }
        return total;
    }

    static void Spruce(MB crown, MB trunk, Vector3 p, float h, int treeSeed)
    {
        var rnd = new Random(treeSeed);
        // trunk
        const int TS = 6; float tr = 0.012f * h + 0.08f;
        int tb = trunk.v.Count;
        for (int k = 0; k <= TS; k++)
        {
            float a = 2f * Mathf.PI * k / TS; var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            trunk.V(p + o * tr + Vector3.down * 0.3f, new Vector2(k / (float)TS, 0));
            trunk.V(p + o * tr * 0.35f + Vector3.up * h * 0.6f, new Vector2(k / (float)TS, h * 0.3f));
        }
        for (int k = 0; k < TS; k++)
        {
            int a = tb + k * 2; var mid = (trunk.v[a] + trunk.v[a + 3]) * 0.5f - (p + Vector3.up * h * 0.3f);
            mid.y = 0; trunk.Q(a, a + 1, a + 3, a + 2, mid);
        }
        // crown: many thin, drooping, ragged branch whorls so the silhouette reads as a real spruce
        int layers = 9 + rnd.Next(3); const int S = 12;
        float baseY = h * (0.10f + 0.10f * (float)rnd.NextDouble());
        float rot = (float)rnd.NextDouble() * 6.28f;
        float lean = ((float)rnd.NextDouble() - 0.5f) * 0.04f;
        int seed = treeSeed & 0xFFFF;
        for (int l = 0; l < layers; l++)
        {
            float f = l / (float)layers;
            float y0 = p.y + baseY + (h - baseY) * Mathf.Pow(f, 0.9f) * 0.95f;
            float y1 = l == layers - 1 ? p.y + h : y0 + (h - baseY) * 0.16f;
            float r = (0.24f * h) * Mathf.Pow(1f - f, 0.95f) * (0.75f + 0.5f * (float)rnd.NextDouble()) + 0.25f;
            var centre = new Vector3(p.x + lean * (y0 - p.y), y0, p.z + lean * 0.5f * (y0 - p.y));
            var apex = new Vector3(centre.x, y1, centre.z);
            int ai = crown.V(apex, new Vector2(0.5f, (y1 - y0) / 2f));
            int bc = crown.V(centre + Vector3.up * (r * 0.25f), new Vector2(0.5f, 0));
            var ring = new int[S + 1];
            for (int k = 0; k <= S; k++)
            {
                int kk = k % S;
                float a = rot + 2f * Mathf.PI * k / S + l * 0.9f;
                // alternate long branch tips and short gaps -> ragged outline
                float tip = (kk % 2 == 0) ? 1f : 0.55f;
                float rr = r * tip * (0.7f + 0.6f * H01(kk, l, seed));
                float droop = r * (0.25f + 0.25f * H01(kk, l, seed + 5)) * tip;
                var q = new Vector3(centre.x + Mathf.Cos(a) * rr, y0 - droop, centre.z + Mathf.Sin(a) * rr);
                ring[k] = crown.V(q, new Vector2(k / (float)S * 3f, 0));
            }
            for (int k = 0; k < S; k++)
            {
                var outward = (crown.v[ring[k]] + crown.v[ring[k + 1]]) * 0.5f - centre;
                outward.y = r * 0.5f;
                crown.Tri(ring[k], ring[k + 1], ai, outward);
                crown.Tri(ring[k], ring[k + 1], bc, Vector3.down);
            }
        }
    }

    // ===========================================================================
    // Lighting, sky and post-processing
    // ===========================================================================
    static void SetupLighting(System.Text.StringBuilder log)
    {
        // Sun
        Light sun = RenderSettings.sun;
        if (sun == null) foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
        if (sun == null) { sun = new GameObject("Directional Light").AddComponent<Light>(); sun.type = LightType.Directional; }
        sun.name = "Sun";
        sun.transform.rotation = Quaternion.Euler(36f, 48f, 0f);
        sun.color = new Color(1f, 0.952f, 0.88f);
        sun.intensity = 2.4f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.95f;
        RenderSettings.sun = sun;

        // Sky
        string skyPath = $"{Dir}/Materials/Sky_Procedural.mat";
        var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
        if (sky == null) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, skyPath); }
        sky.SetFloat("_SunDisk", 2); sky.SetFloat("_SunSize", 0.03f); sky.SetFloat("_SunSizeConvergence", 6f);
        sky.SetFloat("_AtmosphereThickness", 0.85f); sky.SetColor("_SkyTint", new Color(0.46f, 0.52f, 0.62f));
        sky.SetColor("_GroundColor", new Color(0.36f, 0.35f, 0.33f)); sky.SetFloat("_Exposure", 1.25f);
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.0f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.0021f;
        RenderSettings.fogColor = new Color(0.70f, 0.76f, 0.84f);
        DynamicGI.UpdateEnvironment();

        // Post-processing volume
        string profPath = $"{Dir}/Highway_PostFX.asset";
        AssetDatabase.DeleteAsset(profPath);
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(prof, profPath);
        var tm = prof.Add<Tonemapping>(false); tm.mode.Override(TonemappingMode.ACES);
        var bloom = prof.Add<Bloom>(false); bloom.threshold.Override(1.1f); bloom.intensity.Override(0.25f); bloom.scatter.Override(0.65f);
        var ca = prof.Add<ColorAdjustments>(false); ca.postExposure.Override(0.15f); ca.contrast.Override(8f); ca.saturation.Override(4f);
        var wb = prof.Add<WhiteBalance>(false); wb.temperature.Override(4f);
        var vig = prof.Add<Vignette>(false); vig.intensity.Override(0.18f); vig.smoothness.Override(0.45f);
        // speed: the road blurs while the bike (moving with the camera) stays sharp
        var mb = prof.Add<MotionBlur>(false); mb.mode.Override(MotionBlurMode.CameraAndObjects);
        mb.intensity.Override(0.08f); mb.quality.Override(MotionBlurQuality.High); mb.clamp.Override(0.02f);
        mb.active = false;   // off by default for a crisp image; enable on the profile if wanted
        foreach (var c in prof.components) AssetDatabase.AddObjectToAsset(c, prof);
        EditorUtility.SetDirty(prof);

        var volGo = GameObject.Find("Highway_PostFX");
        if (volGo == null) volGo = new GameObject("Highway_PostFX");
        if (!volGo.TryGetComponent(out Volume vol)) vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true; vol.priority = 10; vol.sharedProfile = prof;

        // Shadows far enough to cover the road ahead
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp && urp.shadowDistance < 150f)
        {
            log.AppendLine($"URP shadow distance {urp.shadowDistance} -> 150");
            urp.shadowDistance = 150f; urp.shadowCascadeCount = 4;
            EditorUtility.SetDirty(urp);
        }
        log.AppendLine("Lighting: procedural sky, sun, fog, ACES post-processing.");
    }

    static void PlaceBikeAndCamera(System.Text.StringBuilder log)
    {
        var bike = GameObject.Find("SportMotorcycle");
        if (bike != null)
        {
            bike.transform.position = new Vector3(-7.7f, 0f, 0f);   // middle lane centre (MCTilt also uses this start position)
            bike.transform.rotation = Quaternion.identity;
            log.AppendLine("Motorcycle placed in the middle lane at (-7.7, 0, 0).");
        }
        var cam = Camera.main;
        if (cam != null)
        {
            // chase camera: straight behind, slightly above, looking down at the bike
            if (!cam.TryGetComponent(out KameraFoljare follow)) follow = cam.gameObject.AddComponent<KameraFoljare>();
            if (bike != null) follow.mal = bike.transform;
            cam.transform.position = new Vector3(-7.7f - follow.avstandBakom, follow.hojd, 0f);
            cam.transform.LookAt(new Vector3(-7.7f + follow.tittaFramfor, follow.tittaHojd, 0f));
            cam.fieldOfView = 50f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 2500f;
            if (!cam.TryGetComponent(out UniversalAdditionalCameraData data)) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
        }
        var probeGo = GameObject.Find("Bike_ReflectionProbe");
        if (probeGo == null) probeGo = new GameObject("Bike_ReflectionProbe");
        if (!probeGo.TryGetComponent(out ReflectionProbe probe)) probe = probeGo.AddComponent<ReflectionProbe>();
        probeGo.transform.position = new Vector3(-7.7f, 1.2f, 0f);
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
        probe.size = new Vector3(5000f, 60f, 60f);          // covers the whole road the bike drives on
        probe.resolution = 256;
        probe.boxProjection = false;
    }
}
