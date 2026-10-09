using UnityEngine;

namespace Metro
{
    /// <summary>
    /// Rider satisfaction (0..1) from waits, train comfort, platform crowding, reliability,
    /// price and amenities. It feeds back into mode share and the city subsidy.
    /// </summary>
    public sealed class SatisfactionModel
    {
        readonly GameState gs;
        public float score = 0.7f;
        public float target = 0.7f;
        public float yesterday = 0.7f;
        public float wait = 0.8f, comfort = 0.85f, crowding = 0.9f, reliability = 0.9f, price = 0.7f, amenities = 0.5f;
        public float avgWait = 4f, avgLoad = 0.4f;

        public SatisfactionModel(GameState gs)
        {
            this.gs = gs;
        }

        public int Percent => Mathf.RoundToInt(score * 100f);

        public void MinuteTick()
        {
            var net = gs.net;
            float wSum = 0f, wW = 0f, lSum = 0f, lW = 0f;
            bool anyService = false;
            foreach (var l in net.AliveLines())
            {
                if (l.trains.Count == 0) continue;
                anyService = true;
                float weight = l.boardingsToday + 50f;
                wSum += l.avgWaitRecent * weight;
                wW += weight;
                lSum += l.avgLoadRecent * weight;
                lW += weight;
            }
            if (wW > 0f) avgWait = wSum / wW;
            if (lW > 0f) avgLoad = lSum / lW;

            float waitT = 1f - Mathf.Clamp01((avgWait - 2.5f) / 10f);
            float comfortT = 1f - Mathf.Clamp01((avgLoad - 0.55f) / 0.5f);

            float crowdedPeople = 0f, allPeople = 0f;
            int stations = 0, elevators = 0;
            foreach (var s in net.AliveStations())
            {
                if (s.lines.Count == 0) continue;
                stations++;
                if (s.elevators) elevators++;
                allPeople += s.waiting;
                if (s.Crowding > 0.8f) crowdedPeople += s.waiting;
            }
            float crowdT = allPeople > 1f ? 1f - Mathf.Clamp01(crowdedPeople / allPeople * 1.4f) : 1f;

            var st = gs.stats;
            float denom = Mathf.Max(60f, st.HourEntries + st.HourTurnedAway);
            float bad = st.HourAbandoned + st.HourTurnedAway + st.HourDenied * 0.25f;
            float relT = 1f - Mathf.Clamp01(bad / denom * 3f);

            var e = gs.econ;
            float priceT = Mathf.Clamp01(1f - (e.baseFare - 1.2f) / 4.5f - e.peakSurcharge * 0.15f);
            float amenT = Mathf.Clamp01(0.45f + 0.55f * (stations > 0 ? elevators / (float)stations : 0f)
                                        - Balance.AdSatisfactionPenalty[e.adLevel] * 4f);

            float k = 1f - Mathf.Exp(-1f / 90f);
            wait += (waitT - wait) * k;
            comfort += (comfortT - comfort) * k;
            crowding += (crowdT - crowding) * k;
            reliability += (relT - reliability) * k;
            price += (priceT - price) * 0.1f;
            amenities += (amenT - amenities) * 0.1f;

            target = anyService
                ? 0.24f * wait + 0.2f * comfort + 0.16f * crowding + 0.2f * reliability + 0.14f * price + 0.06f * amenities
                : 0.6f;
            score += (target - score) * (1f - Mathf.Exp(-1f / 120f));
            score = Mathf.Clamp01(score);
        }

        public void Nudge(float delta) => score = Mathf.Clamp01(score + delta);

        public void EndDay() => yesterday = score;
    }
}
