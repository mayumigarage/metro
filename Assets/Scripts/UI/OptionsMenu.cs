using UnityEngine;

/// <summary>
/// The option rows, shared by the song select screen and the pause menu. Every
/// row writes straight to <see cref="GameSettings"/>.
/// </summary>
public static class OptionsMenu
{
    public static void AddItems(MenuList list)
    {
        list.AddValue(
            "HI-SPEED",
            "ノーツとレーザーが流れる速さ",
            () => $"x{GameSettings.HiSpeed:0.0}",
            direction => GameSettings.HiSpeed += direction * 0.1f);

        list.AddValue(
            "JUDGE OFFSET",
            "判定タイミングの補正。遅れて押しがちなら +、早く押しがちなら -",
            () => $"{GameSettings.JudgeOffsetMs:+0;-0;0} ms",
            direction => GameSettings.JudgeOffsetMs += direction * 5);

        list.AddValue(
            "STICK ANGLE",
            "スティックの向きの許容角度（±）。レーザーの進む向きと方向ノーツに使う。大きいほど斜めがずれていても取れる",
            () => $"±{GameSettings.StickAngleDegrees}°",
            direction => GameSettings.StickAngleDegrees += direction * 5);

        list.AddValue(
            "APPROACH",
            "ノーツの流れ方。ACCEL は奥ではかなりゆっくり、判定ラインでは LINEAR と同じ速さまで加速（その分早くから見える。判定のタイミングは同じ）",
            () => GameSettings.ApproachAccelerate ? "ACCEL" : "LINEAR",
            direction => GameSettings.ApproachAccelerate = !GameSettings.ApproachAccelerate);

        list.AddValue(
            "ACCEL LEVEL",
            "APPROACH が ACCEL のときの奥での遅さ。大きいほど奥でゆっくりになり、その分ノーツが早くから見える",
            () => $"{GameSettings.ApproachStrength}",
            direction => GameSettings.ApproachStrength += direction);

        list.AddValue(
            "MIRROR",
            "譜面の左右を反転（次に曲を始めた時から反映）",
            () => OnOff(GameSettings.Mirror),
            direction => GameSettings.Mirror = !GameSettings.Mirror);

        list.AddValue(
            "AUTO PLAY",
            "自動でプレイする（ベストスコアには記録されません）",
            () => OnOff(GameSettings.AutoPlay),
            direction => GameSettings.AutoPlay = !GameSettings.AutoPlay);

        list.AddValue(
            "FAST / SLOW",
            "早い・遅いを判定の下に表示",
            () => OnOff(GameSettings.ShowFastSlow),
            direction => GameSettings.ShowFastSlow = !GameSettings.ShowFastSlow);

        list.AddValue(
            "TIMING DETAIL",
            "判定のズレを ms で表示",
            () => OnOff(GameSettings.ShowTimingDetail),
            direction => GameSettings.ShowTimingDetail = !GameSettings.ShowTimingDetail);

        list.AddValue(
            "BLOOM",
            "ノーツとレーザーの発光の強さ",
            () => $"{GameSettings.BloomPercent}%",
            direction => GameSettings.BloomPercent += direction * 10);

        list.AddValue(
            "GRID",
            "ノーツと一緒に流れるトンネルの枠の間隔（拍）。OFF で表示しない",
            () => FormatGrid(GameSettings.GridBeats),
            direction => GameSettings.GridIntervalIndex += direction);

        list.AddValue(
            "LEFT LASER COLOR",
            "左スティックのレーザー・方向ノーツ・画面左の予告の色",
            () => GameSettings.LaserColorNames[GameSettings.LeftLaserColorIndex],
            direction => GameSettings.LeftLaserColorIndex += direction);

        list.AddValue(
            "RIGHT LASER COLOR",
            "右スティックのレーザー・方向ノーツ・画面右の予告の色",
            () => GameSettings.LaserColorNames[GameSettings.RightLaserColorIndex],
            direction => GameSettings.RightLaserColorIndex += direction);

        list.AddValue(
            "STAGE BRIGHTNESS",
            "レーンと奥の壁の明るさ",
            () => $"{GameSettings.StageBrightnessPercent}%",
            direction => GameSettings.StageBrightnessPercent += direction * 10);

        list.AddValue(
            "THEME",
            "メニュー画面の配色。DARK は背景が黒になる",
            () => GameSettings.DarkMode ? "DARK" : "LIGHT",
            direction => GameSettings.DarkMode = !GameSettings.DarkMode);
    }

    private static string FormatGrid(float beats)
    {
        if (beats <= 0f)
        {
            return "OFF";
        }

        string amount = beats < 1f ? $"1/{Mathf.RoundToInt(1f / beats)}" : $"{beats:0}";
        return beats <= 1f ? $"{amount} BEAT" : $"{amount} BEATS";
    }

    private static string OnOff(bool value)
    {
        return value ? "ON" : "OFF";
    }
}
