using UnityEngine;

/// <summary>
/// The station passage's textures, generated in code (the project has no texture
/// assets): stone floor tiles, aluminium ceiling strips, mosaic wall tiles, fluorescent
/// tubes, yellow tactile paving (点状ブロック) and abstract poster pictures. Lighting is
/// baked in — the stage is unlit — so the floor carries the reflection of the tubes and
/// the ceiling and walls get brighter toward them.
///
/// U runs across a surface (lanes: inner edge → outer edge by the wall; walls: floor →
/// ceiling), V along the corridor. Each texture is made on first use and belongs to the
/// stage that asked for it; RhythmStageView destroys it with the stage's materials.
/// </summary>
public sealed class StageTextures
{
    /// <summary>Wall tiles along one repeat of the wall texture.</summary>
    public const int WallTilesAlong = 8;

    private Texture2D floor;
    private Texture2D ceiling;
    private Texture2D wall;
    private Texture2D tube;
    private Texture2D tactile;
    private readonly Texture2D[] posters = new Texture2D[6];

    public Texture2D Floor => floor != null ? floor : floor = CreateFloor();
    public Texture2D Ceiling => ceiling != null ? ceiling : ceiling = CreateCeiling();
    public Texture2D Wall => wall != null ? wall : wall = CreateWall();
    public Texture2D Tube => tube != null ? tube : tube = CreateTube();
    public Texture2D Tactile => tactile != null ? tactile : tactile = CreateTactile();

    public PosterList Posters => new PosterList(this);

    public readonly struct PosterList
    {
        private readonly StageTextures owner;

        public PosterList(StageTextures owner)
        {
            this.owner = owner;
        }

        public Texture2D this[int index]
        {
            get
            {
                int slot = Mathf.Abs(index) % owner.posters.Length;
                if (owner.posters[slot] == null)
                {
                    owner.posters[slot] = CreatePoster(slot);
                }

                return owner.posters[slot];
            }
        }
    }

    /// <summary>
    /// Polished dark stone, four square tiles across the lane and four along: a value per
    /// tile, fine grain and light chips (terrazzo), grout with a bevel, the light falling
    /// off away from the wall and the tubes' reflection as a soft streak near it.
    /// </summary>
    private static Texture2D CreateFloor()
    {
        const int size = 512;
        const int tile = 128;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                int lx = x % tile;
                int ly = y % tile;
                float tileValue = 0.36f + 0.09f * Hash(x / tile, y / tile, 11);
                float grain = (Hash(x, y, 12) - 0.5f) * 0.05f + (ValueNoise(x, y, 24, size, size, 13) - 0.5f) * 0.08f;
                float chip = Hash(x, y, 14) > 0.993f ? 0.16f : 0f;
                float light = 0.55f + 0.55f * Smooth(u);
                float streak = Mathf.Exp(-Sq((u - 0.86f) / 0.05f)) * (0.42f + 0.12f * ValueNoise(x, y, 8, size, size, 15));
                float value = (tileValue + grain + chip) * light + streak;

                int edge = Mathf.Min(Mathf.Min(lx, ly), Mathf.Min(tile - 1 - lx, tile - 1 - ly));
                if (edge < 2)
                {
                    value = 0.10f * light + streak * 0.2f;
                }
                else if (edge == 2)
                {
                    value *= 1.15f;
                }

                pixels[y * size + x] = Grey(value, 0.97f, 0.985f, 1.02f);
            }
        }

        return Finish("PassageFloor", size, size, pixels, TextureWrapMode.Clamp, TextureWrapMode.Repeat);
    }

    /// <summary>
    /// Aluminium spandrel ceiling: ten rounded strips across the lane running along the
    /// passage, dark gaps between them, a panel joint once per repeat, faint brushing,
    /// brighter toward the tubes at the wall.
    /// </summary>
    private static Texture2D CreateCeiling()
    {
        const int width = 512;
        const int height = 256;
        const float strips = 10f;
        var pixels = new Color32[width * height];
        float stripWidth = width / strips;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float lx = x - Mathf.Floor(x / stripWidth) * stripWidth;
                float profile = 0.34f + 0.16f * Mathf.Sin(Mathf.PI * lx / stripWidth);
                float brushed = (ValueNoise(x, y, 1, width, height, 21) - 0.5f) * 0.03f + (Hash(x, y, 22) - 0.5f) * 0.03f;
                float light = 0.42f + 0.95f * Mathf.Pow(u, 2.4f);
                float value = (profile + brushed) * light;
                if (lx < 3f || y < 2)
                {
                    value = 0.05f;
                }

                pixels[y * width + x] = Grey(value, 0.98f, 0.99f, 1.02f);
            }
        }

        return Finish("PassageCeiling", width, height, pixels, TextureWrapMode.Clamp, TextureWrapMode.Repeat);
    }

    /// <summary>
    /// Small square glazed tiles in a grid up the wall (as in station passages), 32 rows
    /// from floor to ceiling and <see cref="WallTilesAlong"/> along one repeat: a value per
    /// tile, a glaze highlight toward the upper edge of each, grout, and the light from the
    /// tubes along the top falling off down the wall.
    /// </summary>
    private static Texture2D CreateWall()
    {
        const int width = 512;
        const int tile = 16;
        const int height = tile * WallTilesAlong;
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                int lx = x % tile;
                int ly = y % tile;
                float tileValue = 0.50f + 0.10f * Hash(x / tile, y / tile, 31);
                float glaze = 0.10f * Sq(lx / (float)tile);
                float noise = (Hash(x, y, 32) - 0.5f) * 0.03f + (ValueNoise(x, y, 32, width, height, 33) - 0.5f) * 0.06f;
                float light = 0.30f + 0.85f * Mathf.Pow(u, 2.2f) + 0.10f * Mathf.Pow(1f - u, 4f);
                float value = (tileValue + glaze + noise) * light;
                if (lx < 1 || ly < 1)
                {
                    value = 0.16f * light;
                }

                pixels[y * width + x] = Grey(value, 0.96f, 0.98f, 1.03f);
            }
        }

        return Finish("PassageWall", width, height, pixels, TextureWrapMode.Clamp, TextureWrapMode.Repeat);
    }

    /// <summary>One fluorescent tube per repeat: bright, rounded across, dark end caps.</summary>
    private static Texture2D CreateTube()
    {
        const int width = 16;
        const int height = 256;
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            float along = (y + 0.5f) / height;
            float ends = Mathf.Min(along, 1f - along);
            for (int x = 0; x < width; x++)
            {
                float across = Mathf.Sin(Mathf.PI * (x + 0.5f) / width);
                float value = ends < 0.035f ? 0.04f : (0.72f + 0.28f * across) * Mathf.Clamp01(0.75f + ends * 6f);
                pixels[y * width + x] = Grey(value, 1f, 1f, 1f);
            }
        }

        return Finish("PassageTube", width, height, pixels, TextureWrapMode.Clamp, TextureWrapMode.Repeat);
    }

    /// <summary>
    /// One warning block of tactile paving (grey, the material tints it yellow): 5 × 5
    /// domed dots lit from the far side, a slight shadow ring around each, worn surface,
    /// and a dark seam at the block's edges.
    /// </summary>
    private static Texture2D CreateTactile()
    {
        const int size = 128;
        const int dots = 5;
        const float radius = 9.2f;
        var pixels = new Color32[size * size];
        float pitch = size / (float)dots;
        Vector3 lightDirection = new Vector3(-0.35f, 0.55f, 0.75f).normalized;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float worn = (ValueNoise(x, y, 16, size, size, 41) - 0.5f) * 0.10f + (Hash(x, y, 42) - 0.5f) * 0.05f;
                float value = 0.86f + worn;

                float cx = (Mathf.Floor(x / pitch) + 0.5f) * pitch;
                float cy = (Mathf.Floor(y / pitch) + 0.5f) * pitch;
                float dx = (x + 0.5f - cx) / radius;
                float dy = (y + 0.5f - cy) / radius;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                if (distance < 1f)
                {
                    Vector3 normal = new Vector3(dx, dy, Mathf.Sqrt(1f - distance * distance)).normalized;
                    value = 0.60f + 0.42f * Vector3.Dot(normal, lightDirection) + worn * 0.5f;
                }
                else if (distance < 1.25f)
                {
                    value *= 0.78f + 0.22f * (distance - 1f) / 0.25f;
                }

                int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));
                if (edge < 2)
                {
                    value = 0.30f;
                }

                pixels[y * size + x] = Grey(value, 1f, 1f, 1f);
            }
        }

        return Finish("TactilePaving", size, size, pixels, TextureWrapMode.Repeat, TextureWrapMode.Repeat);
    }

    /// <summary>
    /// An abstract advertisement for a lit poster box: a white paper margin, a colour
    /// gradient, a large disc or panel, a headline bar and lines of "copy". U is the
    /// poster's height (floor → ceiling), V its length along the wall.
    /// </summary>
    private static Texture2D CreatePoster(int variant)
    {
        const int height = 128;
        const int length = 256;
        var random = new System.Random(7919 * (variant + 1));
        float Next() => (float)random.NextDouble();

        float hue = Next();
        Color top = Color.HSVToRGB(hue, 0.35f + 0.35f * Next(), 0.55f + 0.35f * Next());
        Color bottom = Color.HSVToRGB(Mathf.Repeat(hue + 0.08f + 0.1f * Next(), 1f), 0.45f + 0.4f * Next(), 0.25f + 0.35f * Next());
        Color accent = Color.HSVToRGB(Mathf.Repeat(hue + 0.4f + 0.3f * Next(), 1f), 0.5f + 0.4f * Next(), 0.85f + 0.15f * Next());
        bool disc = Next() > 0.4f;
        Vector2 shapeCentre = new Vector2(0.25f + 0.5f * Next(), 0.45f + 0.25f * Next());
        float shapeSize = 0.22f + 0.18f * Next();
        bool darkCopy = Next() > 0.6f;
        Color copy = darkCopy ? new Color(0.12f, 0.12f, 0.14f) : new Color(0.96f, 0.96f, 0.94f);
        float headlineEnd = 0.45f + 0.4f * Next();
        int copyLines = 2 + random.Next(3);
        float[] lineEnds = new float[copyLines];
        for (int line = 0; line < copyLines; line++)
        {
            lineEnds[line] = 0.3f + 0.55f * Next();
        }

        var pixels = new Color32[height * length];
        for (int y = 0; y < length; y++)
        {
            for (int x = 0; x < height; x++)
            {
                float up = (x + 0.5f) / height;
                float along = (y + 0.5f) / length;
                Color colour = Color.Lerp(bottom, top, up);

                // Aspect-correct distance for the shape (the poster is twice as long as high).
                float dx = (along - shapeCentre.x) * 2f;
                float dy = up - shapeCentre.y;
                bool inShape = disc ? dx * dx + dy * dy < shapeSize * shapeSize : Mathf.Abs(dx) < shapeSize && Mathf.Abs(dy) < shapeSize * 0.7f;
                if (inShape)
                {
                    colour = Color.Lerp(accent, Color.white, 0.15f * up);
                }

                // Headline near the top, copy lines in the lower third.
                if (up > 0.80f && up < 0.88f && along > 0.07f && along < headlineEnd)
                {
                    colour = copy;
                }

                for (int line = 0; line < copyLines; line++)
                {
                    float lineUp = 0.30f - line * 0.065f;
                    if (up > lineUp - 0.022f && up < lineUp + 0.022f && along > 0.07f && along < lineEnds[line] * 0.6f)
                    {
                        colour = Color.Lerp(colour, copy, 0.85f);
                    }
                }

                // A small logo block in the corner.
                if (up > 0.08f && up < 0.2f && along > 0.86f && along < 0.93f)
                {
                    colour = accent;
                }

                // White paper margin and a soft fall-off of the box's light toward its edges.
                float margin = Mathf.Min(Mathf.Min(up, 1f - up) * height, Mathf.Min(along, 1f - along) * length);
                if (margin < 5f)
                {
                    colour = new Color(0.9f, 0.9f, 0.88f);
                }

                float glow = 0.82f + 0.18f * Mathf.Sin(Mathf.PI * up) * Mathf.Sin(Mathf.PI * along);
                colour *= glow;
                colour.a = 1f;
                pixels[y * height + x] = colour;
            }
        }

        return Finish($"Poster_{variant}", height, length, pixels, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
    }

    private static Texture2D Finish(string name, int width, int height, Color32[] pixels, TextureWrapMode wrapU, TextureWrapMode wrapV)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, true, false)
        {
            name = name,
            wrapModeU = wrapU,
            wrapModeV = wrapV,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 8
        };
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return texture;
    }

    private static Color32 Grey(float value, float r, float g, float b)
    {
        return new Color(Mathf.Clamp01(value * r), Mathf.Clamp01(value * g), Mathf.Clamp01(value * b), 1f);
    }

    private static float Sq(float value)
    {
        return value * value;
    }

    private static float Smooth(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    /// <summary>A repeatable pseudo-random value in 0..1 for an integer cell.</summary>
    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    /// <summary>Smooth noise on a grid of <paramref name="cell"/> pixels that tiles over width × height.</summary>
    private static float ValueNoise(int x, int y, int cell, int width, int height, int seed)
    {
        int cellsX = Mathf.Max(1, width / cell);
        int cellsY = Mathf.Max(1, height / cell);
        float fx = (float)x / cell;
        float fy = (float)y / cell;
        int x0 = Mathf.FloorToInt(fx);
        int y0 = Mathf.FloorToInt(fy);
        float tx = Smooth(fx - x0);
        float ty = Smooth(fy - y0);
        float a = Hash(x0 % cellsX, y0 % cellsY, seed);
        float b = Hash((x0 + 1) % cellsX, y0 % cellsY, seed);
        float c = Hash(x0 % cellsX, (y0 + 1) % cellsY, seed);
        float d = Hash((x0 + 1) % cellsX, (y0 + 1) % cellsY, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }
}
