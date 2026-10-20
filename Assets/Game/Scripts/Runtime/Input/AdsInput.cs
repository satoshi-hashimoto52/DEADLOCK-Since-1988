namespace Deadlock.Input
{
    /// <summary>
    /// Turns the raw ADS button (held / pressed this frame) into the ADS intent for one input source, in hold or toggle
    /// mode. Toggle flips once per press (holding never repeats). While ADS cannot be used (knife out, switching) input
    /// is ignored and nothing is remembered for later. After <see cref="Cancel"/> a button that is still held does not
    /// count until it is released and pressed again (no automatic resume).
    /// </summary>
    public sealed class AdsInput
    {
        bool latched;
        bool waitForRelease;

        public bool Active { get; private set; }

        public bool Update(AdsMode mode, bool held, bool pressed, bool available)
        {
            if (waitForRelease && !held)
                waitForRelease = false;
            if (!available)
            {
                latched = false;
                waitForRelease |= held; // a press made while unavailable is not kept for later
                return Active = false;
            }
            if (waitForRelease)
                return Active = mode == AdsMode.Toggle && latched;
            if (mode == AdsMode.Toggle)
            {
                if (pressed)
                    latched = !latched;
                return Active = latched;
            }
            latched = false;
            return Active = held;
        }

        /// <param name="stillHeld">Whether the button is physically down right now.</param>
        public void Cancel(bool stillHeld)
        {
            latched = false;
            Active = false;
            waitForRelease = stillHeld;
        }
    }
}
