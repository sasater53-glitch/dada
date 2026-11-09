using UnityEngine;

namespace CityBiz.Game
{
    /// <summary>
    /// Процедурные текстуры: проект не зависит от внешних ассетов, но поверхности не выглядят «пластиковыми».
    /// Все текстуры бесшовные (tileable) и генерируются детерминированно.
    /// </summary>
    public static class ProceduralTextures
    {
        const int Size = 256;

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        /// <summary>Бесшовный value noise с периодом period клеток.</summary>
        static float Noise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int Wrap(int v) => ((v % period) + period) % period;
            float a = Hash(Wrap(x0), Wrap(y0), seed), b = Hash(Wrap(x0 + 1), Wrap(y0), seed);
            float c = Hash(Wrap(x0), Wrap(y0 + 1), seed), d = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(int px, int py, int baseCells, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int cells = baseCells;
            for (int o = 0; o < octaves; o++)
            {
                sum += Noise(px * cells / (float)Size, py * cells / (float)Size, cells, seed + o * 31) * amp;
                norm += amp;
                amp *= 0.5f;
                cells *= 2;
            }
            return sum / norm;
        }

        static Texture2D Make(string name, System.Func<int, int, Color> pixel, bool linear = false)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true, linear) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var data = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    data[y * Size + x] = pixel(x, y);
            tex.SetPixels32(data);
            tex.Apply(true, false);
            return tex;
        }

        public static Texture2D Asphalt() => Make("Asphalt", (x, y) =>
        {
            float n = Fbm(x, y, 16, 4, 1);
            float grain = Hash(x, y, 7);
            float v = 0.75f + 0.2f * n + (grain > 0.97f ? 0.25f : 0f) - (grain < 0.03f ? 0.15f : 0f);
            float crack = Mathf.Abs(Fbm(x, y, 4, 3, 9) - 0.5f) < 0.006f ? 0.75f : 1f;
            v *= crack;
            return new Color(v, v, v * 1.02f, 1f);
        });

        public static Texture2D SidewalkTiles() => Make("SidewalkTiles", (x, y) =>
        {
            const int tile = 64; // 4 плитки на текстуру
            int tx = x / tile, ty = y / tile;
            bool joint = x % tile < 2 || y % tile < 2;
            float tint = 0.88f + Hash(tx, ty, 3) * 0.12f;
            float n = 0.92f + 0.08f * Fbm(x, y, 32, 3, 4);
            float v = joint ? 0.55f : tint * n;
            return new Color(v, v * 0.98f, v * 0.95f, 1f);
        });

        public static Texture2D Pavers() => Make("Pavers", (x, y) =>
        {
            const int bw = 32, bh = 16;
            int row = y / bh;
            int ox = (row % 2) * (bw / 2);
            int bx = (x + ox) / bw;
            bool joint = (x + ox) % bw < 2 || y % bh < 2;
            float tint = 0.8f + Hash(bx, row, 5) * 0.2f;
            float v = joint ? 0.45f : tint * (0.9f + 0.1f * Fbm(x, y, 16, 2, 6));
            return new Color(v * 0.95f, v * 0.9f, v * 0.86f, 1f);
        });

        public static Texture2D Brick() => Make("Brick", (x, y) =>
        {
            const int bw = 64, bh = 21; // ~12 рядов на текстуру
            int row = y / bh;
            int ox = (row % 2) * (bw / 2);
            int bx = (x + ox) / bw;
            bool mortar = (x + ox) % bw < 3 || y % bh < 3;
            if (mortar) { float m = 0.78f + 0.08f * Hash(x, y, 2); return new Color(m, m * 0.97f, m * 0.92f, 1f); }
            float t = 0.82f + 0.18f * Hash(bx, row, 11);
            float n = 0.9f + 0.1f * Fbm(x, y, 32, 3, 12);
            return new Color(t * n, t * n * 0.98f, t * n * 0.95f, 1f); // оттенок задаёт цвет материала
        });

        public static Texture2D Plaster() => Make("Plaster", (x, y) =>
        {
            float n = 0.9f + 0.1f * Fbm(x, y, 8, 5, 21);
            float stain = Mathf.Clamp01(Fbm(x, y, 2, 3, 22) * 1.4f - 0.4f) * 0.08f;
            float v = n - stain * (1f - y / (float)Size);
            return new Color(v, v, v, 1f);
        });

        public static Texture2D Concrete() => Make("Concrete", (x, y) =>
        {
            float n = 0.85f + 0.15f * Fbm(x, y, 16, 5, 31);
            bool seam = y % 128 < 2 || x % 128 < 2;
            float v = seam ? n * 0.8f : n;
            float pores = Hash(x, y, 33) > 0.985f ? 0.85f : 1f;
            return new Color(v * pores, v * pores, v * pores, 1f);
        });

        public static Texture2D Corrugated() => Make("Corrugated", (x, y) =>
        {
            float wave = 0.82f + 0.18f * Mathf.Sin(x / (float)Size * Mathf.PI * 2f * 16f);
            float rust = Mathf.Clamp01(Fbm(x, y, 4, 4, 41) * 1.6f - 0.85f);
            float v = wave * (0.95f + 0.05f * Fbm(x, y, 32, 2, 42));
            return new Color(v * (1f + rust * 0.2f), v * (1f - rust * 0.1f), v * (1f - rust * 0.3f), 1f);
        });

        public static Texture2D Grass() => Make("Grass", (x, y) =>
        {
            float n = Fbm(x, y, 8, 5, 51);
            float blade = Hash(x, y, 52);
            float v = 0.7f + 0.3f * n + (blade - 0.5f) * 0.15f;
            return new Color(v * 0.9f, v, v * 0.8f, 1f);
        });

        public static Texture2D Wood() => Make("Wood", (x, y) =>
        {
            const int plank = 32;
            int p = y / plank;
            bool gap = y % plank < 2;
            float ring = Mathf.Sin((x / (float)Size * 6f + Fbm(x, y, 4, 3, 61 + p) * 3f) * Mathf.PI * 2f) * 0.5f + 0.5f;
            float v = gap ? 0.45f : (0.78f + 0.12f * ring + Hash(p, 0, 62) * 0.1f);
            return new Color(v, v * 0.9f, v * 0.8f, 1f);
        });

        public static Texture2D Parquet() => Make("Parquet", (x, y) =>
        {
            const int w = 64, h = 16;
            int bx = x / w, by = y / h;
            bool horizontal = (bx + by / 4) % 2 == 0;
            int lx = horizontal ? x : y, ly = horizontal ? y : x;
            bool joint = lx % w < 1 || ly % h < 1;
            float grain = 0.85f + 0.15f * Mathf.Sin(lx * 0.4f + Fbm(x, y, 16, 2, 71) * 6f);
            float tint = 0.8f + 0.2f * Hash(bx, by, 72);
            float v = joint ? 0.5f : grain * tint;
            return new Color(v, v * 0.88f, v * 0.75f, 1f);
        });

        public static Texture2D FloorTiles() => Make("FloorTiles", (x, y) =>
        {
            const int tile = 64;
            bool joint = x % tile < 2 || y % tile < 2;
            float marble = 0.9f + 0.1f * Mathf.Abs(Mathf.Sin(Fbm(x, y, 4, 4, 81) * 12f));
            float v = joint ? 0.6f : marble;
            return new Color(v, v, v, 1f);
        });

        public static Texture2D Carpet() => Make("Carpet", (x, y) =>
        {
            float v = 0.85f + 0.15f * Hash(x, y, 91) * Fbm(x, y, 32, 2, 92);
            return new Color(v, v, v, 1f);
        });

        public static Texture2D RoofGravel() => Make("RoofGravel", (x, y) =>
        {
            float v = 0.7f + 0.25f * Hash(x / 2, y / 2, 101) + 0.05f * Fbm(x, y, 8, 3, 102);
            return new Color(v, v, v * 0.97f, 1f);
        });

        public static Texture2D Leaves() => Make("Leaves", (x, y) =>
        {
            float n = Fbm(x, y, 16, 4, 111);
            float spot = Hash(x / 3, y / 3, 112);
            float v = 0.6f + 0.4f * n + (spot > 0.8f ? 0.15f : 0f);
            return new Color(v * 0.9f, v, v * 0.85f, 1f);
        });

        /// <summary>Отражение неба/города в стекле: вертикальный градиент с размытыми силуэтами.</summary>
        public static Texture2D GlassReflection() => Make("GlassReflection", (x, y) =>
        {
            float t = y / (float)Size;
            float skyline = Fbm(x, 0, 8, 3, 121) * 0.5f + 0.25f;
            float v = t > skyline ? Mathf.Lerp(0.65f, 1f, t) : 0.35f + 0.1f * Fbm(x, y, 16, 2, 122);
            return new Color(v * 0.92f, v * 0.97f, v, 1f);
        });

        /// <summary>Освещённое окно: тёплый свет, шторы, силуэты мебели.</summary>
        public static Texture2D LitWindow() => Make("LitWindow", (x, y) =>
        {
            float u = x / (float)Size, v = y / (float)Size;
            float curtain = (u < 0.18f || u > 0.82f) ? 0.6f + 0.15f * Mathf.Sin(u * 90f) : 1f;
            float lamp = Mathf.Exp(-((u - 0.5f) * (u - 0.5f) * 8f + (v - 0.75f) * (v - 0.75f) * 10f));
            float furniture = v < 0.3f && Fbm(x, 0, 4, 2, 131) > 0.5f ? 0.55f : 1f;
            float b = (0.55f + 0.45f * lamp) * curtain * furniture;
            return new Color(b, b * 0.82f, b * 0.6f, 1f);
        });

        /// <summary>Полоса окон для LOD1-фасадов: сетка окон на светлой стене (тонируется материалом).</summary>
        public static Texture2D WindowBand() => Make("WindowBand", (x, y) =>
        {
            int cx = x % 64, cy = y % 64;
            bool window = cx > 14 && cx < 50 && cy > 12 && cy < 54;
            if (!window) return new Color(1f, 1f, 1f, 1f);
            float v = 0.25f + 0.25f * (cy / 64f);
            return new Color(v * 0.8f, v * 0.9f, v, 1f);
        });

        /// <summary>Нормаль не используется — шум для лужи/мокрого асфальта хранится в альфе.</summary>
        public static Texture2D Noise(string name, int cells, int seed) => Make(name, (x, y) =>
        {
            float v = Fbm(x, y, cells, 4, seed);
            return new Color(v, v, v, v);
        }, true);
    }
}
