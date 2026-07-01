namespace CVRFury.Builder
{
    /// <summary>
    /// Single source of truth for ChilloutVR's per-parameter synced-bit cost, used by the sync-bit
    /// optimiser, the AAS estimator and the pre-flight check. Float is costed at 64 (conservative): CVR's
    /// own docs vary between 32 and 64, and over-estimating errs toward a harmless warning rather than a
    /// false pass that only surfaces as a failed upload.
    /// </summary>
    internal static class SyncCost
    {
        public const int Bool = 1;
        public const int Int = 8;
        public const int Float = 64;
        public const int Cap = 3200;

        /// <summary>Bits for a CVR AAS parameter-type name ("Bool"/"Int"/"Float"); unknown → Float (safe).</summary>
        public static int ForUsedType(string usedType) =>
            usedType == "Bool" ? Bool : usedType == "Int" ? Int : Float;
    }
}
