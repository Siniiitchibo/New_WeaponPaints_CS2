namespace WeaponPaints;

internal static class KnifeWearPolicy
{
    // Reserve the low float bits for a model tag. Unlike adding an offset and
    // clamping, this cannot collapse different models to the same float at 1.0.
    private const int TagBits = 10;
    private const int BucketSize = 1 << TagBits;
    private const int TagMask = BucketSize - 1;

    internal static float AutomaticWear(int knifeSlot) => 0.001f + knifeSlot * 0.004f;

    internal static float RenderWear(float requestedWear, bool custom, int knifeSlot, int modelCount)
    {
        if (!custom)
            return AutomaticWear(knifeSlot);

        if (knifeSlot < 0 || knifeSlot >= TagMask)
            throw new ArgumentOutOfRangeException(nameof(knifeSlot));

        float requested = float.IsFinite(requestedWear)
            ? Math.Clamp(requestedWear, 0f, 1f) : 0.01f;
        float seed = Math.Clamp(requested, 0.000001f, MathF.BitDecrement(1f));
        int bucket = BitConverter.SingleToInt32Bits(seed) & ~TagMask;
        int tag = knifeSlot + 1;
        int condition = WearBar.Condition(requested);

        // Normally the first candidate succeeds. Adjacent buckets handle exact
        // condition boundaries and collisions with the unmodified automatic fix.
        for (int distance = 0; distance <= modelCount + 1; distance++)
        {
            if (TryCandidate(bucket + distance * BucketSize, out float above))
                return above;
            if (distance > 0 && TryCandidate(bucket - distance * BucketSize, out float below))
                return below;
        }

        throw new InvalidOperationException("Unable to allocate a distinct knife wear value.");

        bool TryCandidate(int candidateBucket, out float candidate)
        {
            candidate = BitConverter.Int32BitsToSingle(candidateBucket | tag);
            if (!float.IsFinite(candidate) || candidate <= 0f || candidate >= 1f ||
                WearBar.Condition(candidate) != condition)
                return false;

            for (int slot = 0; slot < modelCount; slot++)
            {
                if (candidate == AutomaticWear(slot))
                    return false;
            }
            return true;
        }
    }
}
