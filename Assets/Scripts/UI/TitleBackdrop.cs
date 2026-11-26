using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The title screen's moving route map, behind "metro": thick lines with rounded 45°
/// bends that grow in from one edge of the screen, stay a while with their stations,
/// then run off through the other edge, over and over; arrows gliding straight across;
/// small diamonds, dots and rings drifting. Every line has its own slot of hand-made
/// paths around the middle (where the title is), and picks path, colour, width, timing
/// and stations from a fixed-seed random sequence, so the show is the same every launch.
/// Units are the 1920 × 1080 reference, from the centre of the screen.
/// </summary>
public sealed class TitleBackdrop
{
    private const int Seed = 20261124;
    private const float HalfWidth = 960f;
    private const float HalfHeight = 540f;
    private const float Edge = 26f; // the board's steel frame
    private const float CornerRadius = 90f;
    private const float StartStagger = 0.4f;
    private const float ClearSeconds = 0.6f;
    private const int MaxStations = 3;

    /// <summary>Kept clear for the title and its prompt.</summary>
    private static readonly Rect KeepOut = new Rect(-500f, -240f, 1000f, 460f);

    public static Color Ink => SignKit.Ink;
    public static Color Grey => SignKit.Themed(0xA9ADB3, 0x5C6068);
    public static readonly Color Red = SignKit.MetroRed;
    public static readonly Color Orange = new Color32(0xF0, 0x83, 0x00, 0xFF);
    public static readonly Color Amber = new Color32(0xF5, 0xB7, 0x00, 0xFF);
    public static readonly Color Teal = new Color32(0x0A, 0x93, 0x96, 0xFF);

    /// <summary>The six line colours, each listed as often as it should come up.</summary>
    private static Color[] LineColors => new[] { Ink, Ink, Ink, Grey, Grey, Orange, Orange, Red, Amber, Teal };
    private static readonly float[] LineWidths = { 10f, 10f, 16f, 16f, 16f, 24f, 24f, 24f, 34f, 34f, 46f };

    private sealed class PathTemplate
    {
        public Vector2[] Corners;
    }

    private sealed class Slot
    {
        public PathTemplate[] Paths;
        public bool MirrorX;
        public bool MirrorY;
    }

    private sealed class Station
    {
        public RectTransform Rect;
        public CircleGraphic Ring;
        public CircleGraphic Face;
        public Text Label;
        public float Distance;
        public float Diameter;
        public bool Used;
    }

    private sealed class Route
    {
        public Slot Slot;
        public System.Random Random;
        public RouteLineGraphic Line;
        public readonly List<Station> Stations = new List<Station>();
        public float CycleStart;
        public float Speed;
        public float Hold;
        public float Gap;
        public float Length;
        public float Head;
        public float Tail;
        public float ClearHead;
        public float ClearTail;
    }

    private sealed class Floater
    {
        public RectTransform Rect;
        public Graphic[] Graphics;
        public System.Random Random;
        public bool IsArrow;
        public Vector2 Position;
        public Vector2 Velocity;
        public float Size;
        public Color BaseColor;
    }

    private readonly List<Route> routes = new List<Route>();
    private readonly List<Floater> floaters = new List<Floater>();
    private readonly List<Vector2> corners = new List<Vector2>();
    private readonly List<float> candidates = new List<float>();
    private RectTransform lineLayer;
    private RectTransform arrowLayer;
    private RectTransform stationLayer;
    private CanvasGroup decorGroup;
    private CanvasGroup arrowGroup;
    private float clock;
    private bool clearing;
    private float clearedFor;

    /// <summary>True once <see cref="Clear"/> has swept everything off.</summary>
    public bool IsCleared => clearing && clearedFor >= ClearSeconds;

    public void Build(RectTransform parent)
    {
        RectTransform field = UiKit.CreateRect(parent, "RouteField");
        UiKit.Stretch(field, Edge, Edge, Edge, Edge);
        field.gameObject.AddComponent<RectMask2D>();

        DotGridGraphic dots = UiKit.CreateRect(field, "Dots").gameObject.AddComponent<DotGridGraphic>();
        dots.color = UiKit.WithAlpha(SignKit.Ink, GameSettings.DarkMode ? 0.012f : 0.07f);
        dots.raycastTarget = false;
        dots.Configure(36f, 3f);
        UiKit.Stretch(dots);

        // Decor under the lines, arrows over them, stations on top of everything.
        RectTransform decorLayer = CreateLayer(field, "Decor");
        decorGroup = decorLayer.gameObject.AddComponent<CanvasGroup>();
        decorGroup.blocksRaycasts = false;
        lineLayer = CreateLayer(field, "Lines");
        arrowLayer = CreateLayer(field, "Arrows");
        arrowGroup = arrowLayer.gameObject.AddComponent<CanvasGroup>();
        arrowGroup.blocksRaycasts = false;
        stationLayer = CreateLayer(field, "Stations");

        Slot[] slots = CreateSlots();
        for (int index = 0; index < slots.Length; index++)
        {
            Route route = new Route { Slot = slots[index] };
            route.Line = UiKit.CreateRect(lineLayer, $"Line{index}").gameObject.AddComponent<RouteLineGraphic>();
            route.Line.raycastTarget = false;
            Place(route.Line.rectTransform);
            for (int station = 0; station < MaxStations; station++)
            {
                route.Stations.Add(CreateStation(index, station));
            }

            routes.Add(route);
        }

        for (int index = 0; index < 6; index++)
        {
            floaters.Add(CreateArrow(index));
        }

        for (int index = 0; index < 14; index++)
        {
            floaters.Add(CreateDecor(index, decorLayer));
        }

        Restart();
    }

    /// <summary>Starts the show over from an empty screen (the same sequence every time).</summary>
    public void Restart()
    {
        clock = 0f;
        clearing = false;
        clearedFor = 0f;
        SetFloaterAlpha(1f);
        foreach (Route route in routes)
        {
            foreach (Station station in route.Stations)
            {
                station.Used = false;
            }
        }

        for (int index = 0; index < routes.Count; index++)
        {
            Route route = routes[index];
            route.Random = new System.Random(Seed + index * 7919);
            StartCycle(route, index * StartStagger);
        }

        for (int index = 0; index < floaters.Count; index++)
        {
            Floater floater = floaters[index];
            floater.Random = new System.Random(Seed * 3 + index * 104729);
            if (floater.IsArrow)
            {
                RespawnArrow(floater, true);
            }
            else
            {
                RespawnDecor(floater);
            }
        }

        Tick(0f);
    }

    /// <summary>Sweeps every line off along its own path, and the arrows and decor with them.</summary>
    public void Clear()
    {
        if (clearing)
        {
            return;
        }

        clearing = true;
        clearedFor = 0f;
        foreach (Route route in routes)
        {
            route.ClearHead = route.Head;
            route.ClearTail = route.Tail;
        }
    }

    /// <summary>Shows the cleared state at once (no lines, no floaters).</summary>
    public void ClearInstantly()
    {
        Clear();
        clearedFor = ClearSeconds;
        Tick(0f);
    }

    public void Tick(float deltaTime)
    {
        if (clearing)
        {
            clearedFor += deltaTime;
            float sweep = Ease.InOutCubic(clearedFor / ClearSeconds);
            foreach (Route route in routes)
            {
                route.Head = Mathf.Lerp(route.ClearHead, route.Length, sweep);
                route.Tail = Mathf.Lerp(route.ClearTail, route.Length, sweep);
                ApplyRoute(route);
            }

            SetFloaterAlpha(1f - Ease.OutCubic(clearedFor / (ClearSeconds * 0.8f)));
            MoveFloaters(deltaTime * (1f + 6f * sweep));
            return;
        }

        clock += deltaTime;
        foreach (Route route in routes)
        {
            float grow = route.Length / route.Speed;
            while (clock - route.CycleStart > grow * 2f + route.Hold + route.Gap)
            {
                StartCycle(route, route.CycleStart + grow * 2f + route.Hold + route.Gap);
                grow = route.Length / route.Speed;
            }

            float age = clock - route.CycleStart;
            route.Head = Mathf.Clamp(age * route.Speed, 0f, route.Length);
            route.Tail = Mathf.Clamp((age - grow - route.Hold) * route.Speed, 0f, route.Length);
            ApplyRoute(route);
        }

        SetFloaterAlpha(Mathf.MoveTowards(arrowGroup.alpha, 1f, deltaTime * 3f));
        MoveFloaters(deltaTime);
    }

    // ---- Routes ---------------------------------------------------------------------

    private void StartCycle(Route route, float start)
    {
        System.Random random = route.Random;
        Slot slot = route.Slot;
        PathTemplate path = slot.Paths[random.Next(slot.Paths.Length)];
        bool mirrorX = slot.MirrorX && random.Next(2) == 0;
        bool mirrorY = slot.MirrorY && random.Next(2) == 0;
        bool reverse = random.Next(2) == 0;

        corners.Clear();
        foreach (Vector2 corner in path.Corners)
        {
            corners.Add(new Vector2(mirrorX ? -corner.x : corner.x, mirrorY ? -corner.y : corner.y));
        }

        if (reverse)
        {
            corners.Reverse();
        }

        float width = LineWidths[random.Next(LineWidths.Length)];
        Color color = LineColors[random.Next(LineColors.Length)];
        route.Line.color = color;
        route.Line.Width = width;
        route.Line.SetPath(corners, CornerRadius + width * 0.5f);
        route.Line.SetRange(0f, 0f);
        route.Length = route.Line.Length;
        route.CycleStart = start;
        route.Speed = Mathf.Lerp(560f, 780f, (float)random.NextDouble());
        route.Hold = Mathf.Lerp(4.5f, 9f, (float)random.NextDouble());
        route.Gap = Mathf.Lerp(0.3f, 2.2f, (float)random.NextDouble());
        route.Head = 0f;
        route.Tail = 0f;
        PlaceStations(route, width, color);
    }

    private void PlaceStations(Route route, float width, Color color)
    {
        System.Random random = route.Random;
        float diameter = Mathf.Clamp(width * Mathf.Lerp(3.6f, 5f, (float)random.NextDouble()), 44f, 200f);
        float thickness = Mathf.Clamp(width * 0.8f, 5f, diameter * 0.16f);

        // Spots on straight stretches, fully on screen and clear of the title.
        candidates.Clear();
        float margin = diameter * 0.5f + 12f;
        for (float distance = diameter; distance < route.Length - diameter; distance += 12f)
        {
            Vector2 point = route.Line.GetPoint(distance);
            if (Mathf.Abs(point.x) > HalfWidth - Edge - margin || Mathf.Abs(point.y) > HalfHeight - Edge - margin)
            {
                continue;
            }

            Rect keepOut = new Rect(KeepOut.x - margin, KeepOut.y - margin, KeepOut.width + margin * 2f, KeepOut.height + margin * 2f);
            if (keepOut.Contains(point))
            {
                continue;
            }

            Vector2 before = route.Line.GetDirection(distance - diameter * 0.6f);
            Vector2 after = route.Line.GetDirection(distance + diameter * 0.6f);
            if (Vector2.Dot(before, after) > 0.999f)
            {
                candidates.Add(distance);
            }
        }

        int wanted = random.Next(1, MaxStations + 1);
        float spacing = Mathf.Max(260f, diameter * 3f);
        foreach (Station station in route.Stations)
        {
            station.Used = false;
        }

        int placed = 0;
        for (int attempt = 0; attempt < 24 && placed < wanted && candidates.Count > 0; attempt++)
        {
            float distance = candidates[random.Next(candidates.Count)];
            bool free = true;
            for (int other = 0; other < placed; other++)
            {
                free &= Mathf.Abs(route.Stations[other].Distance - distance) >= spacing;
            }

            free &= !NearOtherStation(route, route.Line.GetPoint(distance), diameter);

            if (!free)
            {
                continue;
            }

            Station station = route.Stations[placed++];
            station.Used = true;
            station.Distance = distance;
            station.Diameter = diameter;
            station.Rect.sizeDelta = new Vector2(diameter, diameter);
            station.Rect.anchoredPosition = route.Line.GetPoint(distance);
            station.Ring.color = color;
            station.Ring.Thickness = thickness;
            UiKit.Stretch(station.Face, thickness * 0.5f, thickness * 0.5f, thickness * 0.5f, thickness * 0.5f);

            // "01 –" like a platform number plate; small stations carry the number alone.
            string number = random.Next(0, 30).ToString("00");
            float inner = diameter - thickness * 2f;
            bool withDash = diameter >= 90f;
            int fontSize = Mathf.RoundToInt(inner * (withDash ? 0.28f : 0.38f));
            station.Label.text = withDash ? number + " –" : number;
            station.Label.fontSize = fontSize;
            station.Label.enabled = inner >= 24f;
            station.Label.rectTransform.anchoredPosition = new Vector2(0f, fontSize * SignKit.CapRaise);
        }
    }

    /// <summary>True when a station of another line still on screen is too close to <paramref name="point"/>.</summary>
    private bool NearOtherStation(Route route, Vector2 point, float diameter)
    {
        foreach (Route other in routes)
        {
            if (other == route || other.Tail >= other.Length)
            {
                continue;
            }

            foreach (Station station in other.Stations)
            {
                if (station.Used && Vector2.Distance(station.Rect.anchoredPosition, point) < (station.Diameter + diameter) * 0.5f + 40f)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void ApplyRoute(Route route)
    {
        route.Line.SetRange(route.Tail, route.Head);
        foreach (Station station in route.Stations)
        {
            if (!station.Used)
            {
                station.Rect.gameObject.SetActive(false);
                continue;
            }

            float pop = station.Diameter * 1.5f + 40f;
            float grow = Ease.OutBack((route.Head - station.Distance) / pop);
            float shrink = Ease.OutCubic((station.Distance - route.Tail) / pop);
            float scale = route.Head <= station.Distance ? 0f : Mathf.Min(grow, route.Tail >= station.Distance ? 0f : shrink);
            station.Rect.gameObject.SetActive(scale > 0.01f);
            station.Rect.localScale = Vector3.one * scale;
        }
    }

    private Station CreateStation(int route, int index)
    {
        Station station = new Station();
        station.Rect = UiKit.CreateRect(stationLayer, $"Station{route}_{index}");
        Place(station.Rect);
        station.Face = SignKit.CreateCircle(station.Rect, "Face", SignKit.Face);
        station.Ring = SignKit.CreateCircle(station.Rect, "Ring", SignKit.Ink, 10f);
        UiKit.Stretch(station.Ring);
        station.Label = SignKit.CreateText(station.Rect, "Number", string.Empty, 20, TextAnchor.MiddleCenter, SignKit.Ink, true);
        UiKit.Stretch(station.Label);
        station.Rect.gameObject.SetActive(false);
        return station;
    }

    // ---- Arrows and decor -------------------------------------------------------------

    private Floater CreateArrow(int index)
    {
        RectTransform rect = UiKit.CreateRect(arrowLayer, $"Arrow{index}");
        Place(rect);
        AngularGraphic shaft = UiKit.CreatePanel(rect, "Shaft", Color.white);
        ChevronGraphic head = SignKit.CreateChevron(rect, "Head", Color.white, 10f);
        return new Floater { Rect = rect, Graphics = new Graphic[] { shaft, head }, IsArrow = true };
    }

    private void RespawnArrow(Floater arrow, bool anywhere)
    {
        System.Random random = arrow.Random;
        float[] sizes = { 44f, 60f, 76f, 96f, 120f };
        Color[] colors = { Ink, Ink, Grey, SignKit.Themed(0xD3D6DA, 0x33363C), Orange, Teal, Red };
        float size = sizes[random.Next(sizes.Length)];
        Color color = colors[random.Next(colors.Length)];
        int direction = random.Next(4); // right, left, up, down
        bool horizontal = direction < 2;
        float sign = direction == 0 || direction == 2 ? 1f : -1f;

        // Horizontal arrows keep above or below the title, vertical ones beside it.
        float lane = (horizontal ? Mathf.Lerp(260f, 450f, (float)random.NextDouble()) : Mathf.Lerp(580f, 880f, (float)random.NextDouble()))
            * (random.Next(2) == 0 ? 1f : -1f);
        if (horizontal && lane < 0f)
        {
            lane -= 30f; // the prompt reaches lower than the title reaches up
        }

        float speed = Mathf.Lerp(70f, 150f, (float)random.NextDouble());
        float extent = (horizontal ? HalfWidth : HalfHeight) + size;
        float along = anywhere ? Mathf.Lerp(-extent, extent, (float)random.NextDouble()) : -extent * sign;
        Vector2 axis = horizontal ? new Vector2(sign, 0f) : new Vector2(0f, sign);

        arrow.Size = size;
        arrow.Velocity = axis * speed;
        arrow.Position = horizontal ? new Vector2(along, lane) : new Vector2(lane, along);
        arrow.Rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg);
        arrow.Rect.sizeDelta = new Vector2(size, size * 0.72f);

        float stroke = size * 0.13f;
        RectTransform shaft = arrow.Graphics[0].rectTransform;
        UiKit.Place(shaft, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(size * 0.86f, stroke));
        ChevronGraphic head = (ChevronGraphic)arrow.Graphics[1];
        UiKit.Place(head, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(size * 0.42f, size * 0.72f));
        head.Thickness = stroke * 1.45f;
        foreach (Graphic graphic in arrow.Graphics)
        {
            graphic.color = color;
        }
    }

    private Floater CreateDecor(int index, RectTransform parent)
    {
        RectTransform rect = UiKit.CreateRect(parent, $"Decor{index}");
        Place(rect);
        Graphic graphic;
        switch (index % 3)
        {
            case 0:
                graphic = SignKit.CreateRoundedRect(rect, "Diamond", Color.white);
                rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
                break;
            case 1:
                graphic = SignKit.CreateCircle(rect, "Ring", Color.white, 3.5f);
                break;
            default:
                graphic = SignKit.CreateCircle(rect, "Dot", Color.white);
                break;
        }

        UiKit.Stretch(graphic);
        return new Floater { Rect = rect, Graphics = new[] { graphic } };
    }

    private void RespawnDecor(Floater decor)
    {
        System.Random random = decor.Random;
        Graphic graphic = decor.Graphics[0];
        bool ring = graphic is CircleGraphic circle && circle.Thickness > 0f;
        bool dot = graphic is CircleGraphic && !ring;
        Color[] colors = ring
            ? new[] { Teal, Orange, Red, Amber, Grey }
            : new[] { Ink, Ink, Grey, Orange };
        decor.Size = ring ? Mathf.Lerp(22f, 44f, (float)random.NextDouble()) : dot ? Mathf.Lerp(5f, 9f, (float)random.NextDouble()) : Mathf.Lerp(9f, 15f, (float)random.NextDouble());
        decor.Rect.sizeDelta = new Vector2(decor.Size, decor.Size);
        decor.BaseColor = colors[random.Next(colors.Length)];
        graphic.color = decor.BaseColor;
        decor.Position = new Vector2(Mathf.Lerp(-HalfWidth, HalfWidth, (float)random.NextDouble()), Mathf.Lerp(-HalfHeight, HalfHeight, (float)random.NextDouble()));
        float angle = (float)random.NextDouble() * Mathf.PI * 2f;
        decor.Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Lerp(6f, 20f, (float)random.NextDouble());
    }

    private void SetFloaterAlpha(float alpha)
    {
        decorGroup.alpha = alpha;
        arrowGroup.alpha = alpha;
    }

    private void MoveFloaters(float deltaTime)
    {
        foreach (Floater floater in floaters)
        {
            floater.Position += floater.Velocity * deltaTime;
            float marginX = HalfWidth + floater.Size;
            float marginY = HalfHeight + floater.Size;
            if (floater.IsArrow)
            {
                if (Mathf.Abs(floater.Position.x) > marginX || Mathf.Abs(floater.Position.y) > marginY)
                {
                    RespawnArrow(floater, false);
                }
            }
            else
            {
                // Decor drifts on a torus: off one edge, back in at the other.
                if (Mathf.Abs(floater.Position.x) > marginX)
                {
                    floater.Position.x -= Mathf.Sign(floater.Position.x) * marginX * 2f;
                }

                if (Mathf.Abs(floater.Position.y) > marginY)
                {
                    floater.Position.y -= Mathf.Sign(floater.Position.y) * marginY * 2f;
                }

                // It fades out while it drifts past the title.
                float outside = Mathf.Max(
                    Mathf.Max(KeepOut.xMin - floater.Position.x, floater.Position.x - KeepOut.xMax),
                    Mathf.Max(KeepOut.yMin - floater.Position.y, floater.Position.y - KeepOut.yMax));
                floater.Graphics[0].color = UiKit.WithAlpha(floater.BaseColor, Mathf.Clamp01(outside / 60f));
            }

            floater.Rect.anchoredPosition = floater.Position;
        }
    }

    // ---- Layout ---------------------------------------------------------------------

    private static RectTransform CreateLayer(RectTransform parent, string name)
    {
        RectTransform layer = UiKit.CreateRect(parent, name);
        UiKit.Stretch(layer);
        return layer;
    }

    /// <summary>A rect hanging from the field's centre, so its position is in centre units.</summary>
    private static void Place(RectTransform rect)
    {
        Vector2 centre = new Vector2(0.5f, 0.5f);
        rect.anchorMin = centre;
        rect.anchorMax = centre;
        rect.pivot = centre;
        rect.sizeDelta = Vector2.zero;
    }

    private static PathTemplate P(params float[] coordinates)
    {
        Vector2[] points = new Vector2[coordinates.Length / 2];
        for (int index = 0; index < points.Length; index++)
        {
            points[index] = new Vector2(coordinates[index * 2], coordinates[index * 2 + 1]);
        }

        return new PathTemplate { Corners = points };
    }

    /// <summary>
    /// Each slot keeps to its own part of the screen (top, bottom, the sides, the corners),
    /// so lines cross each other now and then but never run on top of one another, and
    /// none passes through the title. Every path starts and ends off screen.
    /// </summary>
    private static Slot[] CreateSlots()
    {
        return new[]
        {
            // Top, high.
            new Slot
            {
                MirrorX = true,
                Paths = new[]
                {
                    P(-1100f, 470f, -260f, 470f, -100f, 630f),
                    P(-1100f, 410f, 240f, 410f, 470f, 640f),
                    P(-1100f, 440f, 1100f, 440f),
                },
            },
            // Top, low.
            new Slot
            {
                MirrorX = true,
                Paths = new[]
                {
                    P(1100f, 280f, 560f, 280f, 200f, 640f),
                    P(1100f, 300f, -1100f, 300f),
                    P(-1100f, 140f, -620f, 140f, -460f, 300f, 1100f, 300f),
                },
            },
            // Bottom, low.
            new Slot
            {
                MirrorX = true,
                Paths = new[]
                {
                    P(-1100f, -460f, -300f, -460f, -120f, -640f),
                    P(-1100f, -410f, 300f, -410f, 530f, -640f),
                    P(-1100f, -480f, 1100f, -480f),
                },
            },
            // Bottom, high.
            new Slot
            {
                MirrorX = true,
                Paths = new[]
                {
                    P(1100f, -290f, 600f, -290f, 250f, -640f),
                    P(1100f, -310f, -1100f, -310f),
                    P(-1100f, -130f, -640f, -130f, -460f, -310f, 1100f, -310f),
                },
            },
            // Left side.
            new Slot
            {
                MirrorY = true,
                Paths = new[]
                {
                    P(-760f, 640f, -760f, 360f, -640f, 240f, -640f, -640f),
                    P(-860f, -640f, -860f, 0f, -680f, 180f, -680f, 640f),
                    P(-1100f, 120f, -760f, 120f, -600f, -40f, -600f, -640f),
                },
            },
            // Right side.
            new Slot
            {
                MirrorY = true,
                Paths = new[]
                {
                    P(780f, -640f, 780f, -200f, 640f, -60f, 640f, 640f),
                    P(880f, 640f, 880f, 100f, 700f, -80f, 700f, -640f),
                    P(1100f, -60f, 760f, -60f, 600f, 100f, 600f, 640f),
                },
            },
            // Corners.
            new Slot
            {
                MirrorX = true,
                MirrorY = true,
                Paths = new[]
                {
                    P(-1100f, 200f, -860f, 200f, -420f, 640f),
                    P(-1100f, -40f, -960f, -40f, -540f, -460f, -540f, -640f),
                    P(-460f, 640f, -460f, 400f, -700f, 160f, -1100f, 160f),
                },
            },
        };
    }
}
