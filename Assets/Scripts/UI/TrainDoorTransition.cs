using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The screen change played as a train stop: the view pulls back from an open doorway
/// to the side of a train car, (1) the doors close, the station badge beside them
/// spins to the next station's number while the screens are swapped behind the doors,
/// (2) the doors open and the view goes through the doorway into the new screen.
/// Sits on its own canvas above every other UI.
/// </summary>
public sealed class TrainDoorTransition : MonoBehaviour
{
    // Layout of the car in 1920 x 1080 canvas units around the screen centre.
    private const float LeafWidth = 320f;
    private const float DoorTop = 404f;
    private const float DoorBottom = -330f;
    private const float WallExtent = 6000f;
    private const float StripeY = -215f;
    private const float StripeHeight = 46f;

    private const float PullBackSeconds = 0.28f;
    private const float CloseSeconds = 0.36f;
    private const float SpinSeconds = 0.52f;
    private const float OpenSeconds = 0.34f;
    private const float GoThroughSeconds = 0.3f;

    private static Color Body => SignKit.Themed(0xC8CCD2, 0x24272C);
    private static Color BodyShade => SignKit.Themed(0xB2B7BF, 0x191B1F);
    private static Color DoorFace => SignKit.Themed(0xD5D8DD, 0x2D3036);
    private static Color Rubber => SignKit.Themed(0x2A2C30, 0x08090A);
    private static Color FrameGrey => SignKit.Themed(0x6F757D, 0x484D55);
    private static readonly Color GlassTop = new Color32(0x3A, 0x48, 0x58, 0xFF);
    private static readonly Color GlassBottom = new Color32(0x10, 0x15, 0x1C, 0xFF);
    private static readonly Color LampOff = new Color32(0x5E, 0x2A, 0x26, 0xFF);
    private static readonly Color LampOn = new Color32(0xFF, 0x4A, 0x3A, 0xFF);

    private RectTransform canvasRect;
    private RectTransform car;
    private RectTransform leftLeaf;
    private RectTransform rightLeaf;
    private LineBadge badge;
    private RoundedRectGraphic lamp;
    private Graphic[] stripes;
    private AngularGraphic blocker;

    public bool IsRunning { get; private set; }

    private static float DoorCentreY => (DoorTop + DoorBottom) * 0.5f;
    private static float DoorHeight => DoorTop - DoorBottom;

    public void Build()
    {
        canvasRect = (RectTransform)transform;

        // Swallows clicks while the doors run.
        blocker = UiKit.CreatePanel(canvasRect, "InputBlocker", Color.clear);
        blocker.raycastTarget = true;
        UiKit.Stretch(blocker);

        car = UiKit.CreateRect(canvasRect, "Car");
        UiKit.Place(car, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920f, 1080f));

        // The leaves come first so the walls hide them when they slide open.
        leftLeaf = BuildLeaf("LeftLeaf", true);
        rightLeaf = BuildLeaf("RightLeaf", false);
        BuildWalls();
        BuildDecorations();

        stripes = new Graphic[]
        {
            car.Find("LeftWall/Stripe").GetComponent<Graphic>(),
            car.Find("RightWall/Stripe").GetComponent<Graphic>(),
            leftLeaf.Find("Stripe").GetComponent<Graphic>(),
            rightLeaf.Find("Stripe").GetComponent<Graphic>()
        };

        gameObject.SetActive(false);
    }

    /// <summary>
    /// Plays the stop. <paramref name="onClosed"/> runs while the doors are shut (swap
    /// the screens there); <paramref name="onDone"/> once the new screen is uncovered.
    /// </summary>
    public void Run(Color lineColor, string fromLetter, string fromNumber, string toLetter, string toNumber, Action onClosed, Action onDone = null)
    {
        if (IsRunning)
        {
            return;
        }

        foreach (Graphic stripe in stripes)
        {
            stripe.color = lineColor;
        }

        badge.SetLineColor(lineColor);
        badge.SetCode(fromLetter, fromNumber);
        gameObject.SetActive(true);
        StartCoroutine(Play(fromLetter, fromNumber, toLetter, toNumber, onClosed, onDone));
    }

    private IEnumerator Play(string fromLetter, string fromNumber, string toLetter, string toNumber, Action onClosed, Action onDone)
    {
        IsRunning = true;
        float openScale = GetOpenScale();
        SetDoors(1f);
        badge.Rect.localScale = Vector3.one;

        // Pull back from the open doorway: the car appears around the old screen.
        yield return Animate(PullBackSeconds, t => SetZoom(Mathf.Lerp(openScale, 1f, Ease.OutCubic(t))));

        // (1) The doors close.
        yield return Animate(CloseSeconds, t =>
        {
            SetDoors(1f - Ease.InOutCubic(t));
            SetLamp(t);
        });
        SetDoors(0f);
        SetLamp(-1f);
        onClosed?.Invoke();

        // The badge spins to the next station; the new number shows after the first half-turn.
        bool switched = false;
        yield return Animate(SpinSeconds, t =>
        {
            float angle = Ease.InOutCubic(t) * Mathf.PI * 2f;
            badge.Rect.localScale = new Vector3(Mathf.Max(0.02f, Mathf.Abs(Mathf.Cos(angle))), 1f, 1f);
            if (!switched && angle >= Mathf.PI * 0.5f)
            {
                switched = true;
                badge.SetCode(toLetter, toNumber);
            }

            // The doors meeting shake the car a little.
            float shake = Mathf.Exp(-t * 10f) * Mathf.Sin(t * 70f) * 5f;
            leftLeaf.anchoredPosition = new Vector2(-LeafWidth * 0.5f - shake, DoorCentreY);
            rightLeaf.anchoredPosition = new Vector2(LeafWidth * 0.5f + shake, DoorCentreY);
        });
        badge.Rect.localScale = Vector3.one;
        badge.SetCode(toLetter, toNumber);

        // (2) The doors open, then the view goes through the doorway.
        yield return Animate(OpenSeconds, t =>
        {
            SetDoors(Ease.InOutCubic(t));
            SetLamp(t);
        });
        SetLamp(-1f);
        yield return Animate(GoThroughSeconds, t => SetZoom(Mathf.Lerp(1f, openScale, Ease.InCubic(t))));

        gameObject.SetActive(false);
        IsRunning = false;
        onDone?.Invoke();
    }

    private static IEnumerator Animate(float seconds, Action<float> step)
    {
        for (float elapsed = 0f; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
        {
            step(elapsed / seconds);
            yield return null;
        }

        step(1f);
    }

    /// <summary>Scale at which the open doorway covers the whole canvas, whatever its aspect.</summary>
    private float GetOpenScale()
    {
        Vector2 size = canvasRect.rect.size;
        float cover = Mathf.Max(size.x / (LeafWidth * 2f), (size.y * 0.5f + Mathf.Abs(DoorCentreY)) * 2f / DoorHeight);
        return cover * 1.04f;
    }

    /// <summary>Scales the car about the doorway's centre.</summary>
    private void SetZoom(float scale)
    {
        car.localScale = new Vector3(scale, scale, 1f);
        car.anchoredPosition = new Vector2(0f, DoorCentreY * (1f - scale));
    }

    /// <summary>0 = shut, 1 = open (each leaf hidden in the wall beside the doorway).</summary>
    private void SetDoors(float open)
    {
        float travel = LeafWidth * open;
        leftLeaf.anchoredPosition = new Vector2(-LeafWidth * 0.5f - travel, DoorCentreY);
        rightLeaf.anchoredPosition = new Vector2(LeafWidth * 0.5f + travel, DoorCentreY);
    }

    /// <summary>The door lamp blinks while the doors move; negative turns it off.</summary>
    private void SetLamp(float t)
    {
        bool on = t >= 0f && Mathf.Repeat(t * 3f, 1f) < 0.6f;
        lamp.color = on ? LampOn : LampOff;
    }

    private RectTransform BuildLeaf(string name, bool left)
    {
        RoundedRectGraphic leaf = SignKit.CreateRoundedRect(car, name, Color.white, 4f);
        leaf.SetVerticalGradient(DoorFace, BodyShade);
        RectTransform rect = leaf.rectTransform;
        UiKit.Place(rect, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(LeafWidth, DoorHeight));

        // Window in the upper part of the leaf.
        RoundedRectGraphic frame = SignKit.CreateRoundedRect(rect, "WindowFrame", FrameGrey, 30f);
        UiKit.Place(frame, new Vector2(0.5f, 1f), new Vector2(0f, -62f), new Vector2(204f, 308f));
        RoundedRectGraphic glass = SignKit.CreateRoundedRect(frame.rectTransform, "Glass", Color.white, 24f);
        glass.SetVerticalGradient(GlassTop, GlassBottom);
        UiKit.Stretch(glass, 7f, 7f, 7f, 7f);

        AngularGraphic stripe = UiKit.CreatePanel(rect, "Stripe", SignKit.MetroRed);
        UiKit.Place(stripe, new Vector2(0.5f, 0.5f), new Vector2(0f, StripeY - DoorCentreY), new Vector2(LeafWidth, StripeHeight));

        // The rubber edge where the two leaves meet.
        AngularGraphic rubber = UiKit.CreatePanel(rect, "Rubber", Rubber);
        Vector2 anchor = left ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f);
        UiKit.Place(rubber, anchor, Vector2.zero, new Vector2(12f, DoorHeight));
        return rect;
    }

    private void BuildWalls()
    {
        float sideWidth = WallExtent;
        RectTransform leftWall = CreateWall("LeftWall", new Vector2(-LeafWidth - sideWidth * 0.5f, 0f), new Vector2(sideWidth, WallExtent * 2f));
        RectTransform rightWall = CreateWall("RightWall", new Vector2(LeafWidth + sideWidth * 0.5f, 0f), new Vector2(sideWidth, WallExtent * 2f));
        CreateWall("TopWall", new Vector2(0f, DoorTop + WallExtent * 0.5f), new Vector2(LeafWidth * 2f + 2f, WallExtent));
        RectTransform bottomWall = CreateWall("BottomWall", new Vector2(0f, DoorBottom - WallExtent * 0.5f), new Vector2(LeafWidth * 2f + 2f, WallExtent));

        // The line colour runs along the car body at the same height as on the doors.
        foreach (RectTransform wall in new[] { leftWall, rightWall })
        {
            AngularGraphic stripe = UiKit.CreatePanel(wall, "Stripe", SignKit.MetroRed);
            UiKit.Place(stripe, new Vector2(0.5f, 0.5f), new Vector2(0f, StripeY), new Vector2(sideWidth, StripeHeight));
        }

        // The skirt below the doorway is a shade darker than the body.
        bottomWall.GetComponent<AngularGraphic>().color = BodyShade;

        // Threshold at the foot of the doorway.
        AngularGraphic step = UiKit.CreatePanel(bottomWall, "Threshold", FrameGrey);
        UiKit.Place(step, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(LeafWidth * 2f, 22f));
    }

    private RectTransform CreateWall(string name, Vector2 position, Vector2 size)
    {
        AngularGraphic wall = UiKit.CreatePanel(car, name, Body);
        UiKit.Place(wall, new Vector2(0.5f, 0.5f), position, size);
        return wall.rectTransform;
    }

    private void BuildDecorations()
    {
        // Frame round the doorway (outside it, so the opening stays clear).
        RoundedRectGraphic doorFrame = SignKit.CreateRoundedRect(car, "DoorFrame", FrameGrey, 10f, 10f);
        UiKit.Place(doorFrame, new Vector2(0.5f, 0.5f), new Vector2(0f, DoorCentreY), new Vector2(LeafWidth * 2f + 20f, DoorHeight + 20f));

        // Door lamp above the doorway.
        lamp = SignKit.CreateRoundedRect(car, "DoorLamp", LampOff, 12f);
        UiKit.Place(lamp, new Vector2(0.5f, 0.5f), new Vector2(0f, DoorTop + 52f), new Vector2(76f, 24f));

        // Side window of the car, running off the left edge as in the sketch.
        RoundedRectGraphic windowFrame = SignKit.CreateRoundedRect(car, "SideWindowFrame", FrameGrey, 46f);
        UiKit.Place(windowFrame, new Vector2(0.5f, 0.5f), new Vector2(-860f, 136f), new Vector2(560f, 318f));
        RoundedRectGraphic glass = SignKit.CreateRoundedRect(windowFrame.rectTransform, "Glass", Color.white, 38f);
        glass.SetVerticalGradient(GlassTop, GlassBottom);
        UiKit.Stretch(glass, 8f, 8f, 8f, 8f);

        // Station badge right of the doors: it spins to the next stop.
        badge = LineBadge.Create(car, "StationBadge", SignKit.MetroRed, 300f);
        badge.Rect.anchoredPosition = new Vector2(600f, 96f);
    }
}
