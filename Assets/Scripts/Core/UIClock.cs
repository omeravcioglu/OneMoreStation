using UnityEngine;

namespace Metro
{
    /// <summary>
    /// The clock that UI and map animations (wobbles, pulses, sways, marching dashes) run on. It is real
    /// time, except while a frame-exact capture is recording (Time.captureFramerate &gt; 0): then it steps
    /// exactly one capture frame per rendered frame, carrying on from wherever real time was when the
    /// capture started, so recorded footage animates at the right speed however slowly frames are made.
    /// GameRoot calls <see cref="Tick"/> once at the start of every frame.
    /// </summary>
    public static class UIClock
    {
        static int tickedFrame = -1;

        /// <summary>Seconds on the animation clock.</summary>
        public static float Now { get; private set; }

        /// <summary>This frame's step on the animation clock, in seconds.</summary>
        public static float Delta { get; private set; }

        public static void Tick()
        {
            int frame = Time.frameCount;
            if (frame == tickedFrame) return;
            bool first = tickedFrame < 0;
            tickedFrame = frame;
            if (first)
            {
                Now = Time.unscaledTime;
                Delta = 0f;
            }
            else if (Time.captureFramerate > 0)
            {
                Delta = 1f / Time.captureFramerate;
                Now += Delta;
            }
            else
            {
                Delta = Time.unscaledDeltaTime;
                Now = Time.unscaledTime;
            }
        }
    }
}
