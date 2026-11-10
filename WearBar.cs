using System.Globalization;

namespace WeaponPaints;

// One equally wide colour segment per condition; ten click positions per segment.
// The physical bar is deliberately not a linear 0..1 scale.
internal static class WearBar
{
    internal const int StepsPerCondition = 10;
    internal const int StepCount = StepsPerCondition * 5;

    private static readonly float[] Boundaries = [0f, 0.07f, 0.15f, 0.38f, 0.45f, 1f];

    internal static float ValueAt(int step)
    {
        if ((uint)step >= StepCount)
            throw new ArgumentOutOfRangeException(nameof(step));

        int condition = step / StepsPerCondition;
        int position = step % StepsPerCondition;
        float low = Boundaries[condition];
        // Keep the last position inside its condition (except the final endpoint).
        float high = Boundaries[condition + 1] - (condition < 4 ? 0.0001f : 0f);
        return low + (high - low) * position / (StepsPerCondition - 1);
    }

    internal static int Condition(float wear) => wear switch
    {
        < 0.07f => 0,
        < 0.15f => 1,
        < 0.38f => 2,
        < 0.45f => 3,
        _ => 4
    };

    internal static int NearestStep(float wear)
    {
        wear = float.IsFinite(wear) ? Math.Clamp(wear, 0f, 1f) : 0f;
        int condition = Condition(wear);
        int first = condition * StepsPerCondition;
        int closest = first;
        for (int step = first + 1; step < first + StepsPerCondition; step++)
        {
            if (Math.Abs(ValueAt(step) - wear) < Math.Abs(ValueAt(closest) - wear))
                closest = step;
        }
        return closest;
    }

    internal static bool TryParseButton(string buttonId, out int step)
    {
        const string prefix = "submenu_wear_step_";
        step = -1;
        return buttonId.StartsWith(prefix, StringComparison.Ordinal) &&
               int.TryParse(buttonId.AsSpan(prefix.Length), NumberStyles.None,
                   CultureInfo.InvariantCulture, out step) &&
               (uint)step < StepCount;
    }
}
