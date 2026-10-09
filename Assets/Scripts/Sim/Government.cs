using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    /// <summary>What a mayor cares about most. Shapes the requests City Hall sends and what moves approval.</summary>
    public enum Agenda { Growth, Thrift, Green, People }

    public enum RequestKind
    {
        ConnectDistrict, StationNear, Ridership, Satisfaction, ServedResidents, FareCap, NoSurcharge,
        Accessibility, Interchanges, CrowdRelief, Levy, ProfitDay, Event, RepayBailout
    }

    public enum RequestState { Proposed, Active, Done, Failed, Declined, Expired }

    public enum LandmarkKind { Hospital, University, Stadium, Arena, Museum, Airport, Market, Library, Ferry, Expo }

    /// <summary>A named place City Hall cares about (a new hospital, a stadium...). Shown on the map.</summary>
    public sealed class Landmark
    {
        public int id;
        public string name;
        public LandmarkKind kind;
        public Vector2 pos;
        public int districtId = -1;
        public bool built;      // stays on the map once its request succeeded
    }

    public sealed class Mayor
    {
        public string name;
        public Agenda agenda;
        public int face;        // seed for the drawn portrait
    }

    /// <summary>Something City Hall needs from the metro, with a deadline, a reward and a cost of failing.</summary>
    public sealed class CouncilRequest
    {
        public int id;
        public RequestKind kind;
        public RequestState state;
        public bool mandate;            // can't be declined; fined when missed
        public string title = "", letter = "", status = "";
        public int districtId = -1, stationId = -1, landmarkId = -1;
        public float target;
        public double issuedAt, answerBy, deadline, resolvedAt;
        public double eventStart, eventEnd;
        public float reward, fine;
        public int approvalReward, approvalPenalty;
        public float progress;
        public double holdSince = -1;   // FareCap / NoSurcharge: when the condition last started holding
        public float wanted, served;    // Event: trips that wanted the metro / got on it
        public float paidAmount;        // Levy / bailout: instalments paid so far
        public bool paid;

        public bool IsOpen => state == RequestState.Proposed || state == RequestState.Active;
        public bool IsHold => kind == RequestKind.FareCap || kind == RequestKind.NoSurcharge;
        public bool IsPayment => kind == RequestKind.Levy || kind == RequestKind.RepayBailout;
        public bool JudgedAtMidnight => kind == RequestKind.Satisfaction || kind == RequestKind.ProfitDay || kind == RequestKind.CrowdRelief;
    }

    public sealed class ElectionResult
    {
        public Mayor winner, loser;
        public bool incumbentWon;
        public int endorsed = -1;       // -1 nobody, 0 the incumbent, 1 the challenger
        public bool endorsedWon;
        public float incumbentShare;
        public float approvalBefore, approvalAfter;
    }

    /// <summary>The weekly council session: how City Hall sees the metro this week.</summary>
    public sealed class CouncilReport
    {
        public int week;
        public float approvalBefore, approvalNow;
        public int done, failed, declined;
        public string verdict = "", quote = "";
        public bool ultimatumIssued, ultimatumLifted, ultimatumActive;
        public ElectionResult election;
        public Mayor challengerAnnounced;
        public int electionDay = -1;
        public float subsidyFactor, permitFactor;
    }

    /// <summary>
    /// City Hall. A mayor with an agenda sends the metro requests (connect this district, serve the new
    /// hospital, handle the stadium crowd, pay a levy...). Doing them earns grants and approval; refusing
    /// or failing costs approval and, for mandates, fines. Approval sets the city's operating subsidy and
    /// permit costs. Weekly council sessions review the metro, elections bring new mayors with new
    /// agendas, City Hall bails out a bankrupt metro once, and a metro that loses the council's trust
    /// (or goes broke twice) loses its operating licence.
    /// </summary>
    public sealed class GovernmentSystem
    {
        readonly GameState gs;
        readonly Rng rng;

        public bool started;            // the council sends requests (Market town and up)
        public bool mandatesEnabled;    // mandates, levies and elections (City and up)
        /// <summary>Kinds of request this council never sends (challenges whose goal they would contradict).</summary>
        public readonly HashSet<RequestKind> blocked = new HashSet<RequestKind>();
        public float approval = Balance.ApprovalStart;
        public Mayor mayor, challenger;
        public int nextElectionDay = -1;
        public int endorsed = -1;
        public int lastSessionDay = -1;
        public float approvalAtLastSession = Balance.ApprovalStart;
        public double nextProposalAt = double.MaxValue;
        public int nextId, nextLandmarkId;
        public int completed, failed, declined, bailouts;
        public int weekDone, weekFailed, weekDeclined;
        public bool ultimatum;
        public bool gameOver;
        public string gameOverReason = "";
        public readonly List<CouncilRequest> open = new List<CouncilRequest>();
        public readonly List<CouncilRequest> history = new List<CouncilRequest>();
        public readonly List<Landmark> landmarks = new List<Landmark>();
        public readonly List<float> approvalHistory = new List<float>();

        public event Action<CouncilRequest> RequestChanged;
        public event Action<CouncilReport> SessionHeld;
        public event Action<string> LicenceRevoked;
        public event Action<float> BailoutGranted;

        public GovernmentSystem(GameState gs)
        {
            this.gs = gs;
            rng = new Rng(gs.options.seed * 31 + 7);
            mayor = NewMayor(null);
            gs.DistrictRevealed += OnDistrictRevealed;
        }

        // ------------------------------------------------------------------ effects of approval

        /// <summary>Multiplier on the per-rider operating subsidy the city pays.</summary>
        public float SubsidyFactor => started ? Mathf.Lerp(0.7f, 1.25f, approval / 100f) : 1f;

        /// <summary>Multiplier on construction costs: friendly councils fast-track permits, hostile ones drag their feet.</summary>
        public float PermitFactor
        {
            get
            {
                if (!started) return 1f;
                if (approval >= 80f) return 0.9f;
                if (approval >= 65f) return 0.95f;
                if (approval < 20f) return 1.2f;
                if (approval < 35f) return 1.1f;
                return 1f;
            }
        }

        public string Standing
        {
            get
            {
                if (approval >= 80f) return "Delighted";
                if (approval >= 65f) return "Pleased";
                if (approval >= 45f) return "Neutral";
                if (approval >= 25f) return "Concerned";
                return "Hostile";
            }
        }

        int MaxOpen => !started ? 0 : gs.progress.tier <= 2 ? 2 : gs.progress.tier == 3 ? 3 : 4;
        float DiffScale => gs.options.difficulty == 0 ? 1.2f : gs.options.difficulty == 1 ? 1f : 0.85f;
        float RewardScale => (1f + 0.5f * gs.progress.tier) * DiffScale;
        double Now => gs.clock.totalMinutes;

        // ------------------------------------------------------------------ lifecycle

        /// <summary>The council starts sending requests (Market town).</summary>
        public void Start(bool announce = true)
        {
            if (started) return;
            started = true;
            approval = Mathf.Max(approval, Balance.ApprovalStart);
            approvalAtLastSession = approval;
            nextProposalAt = Now + 90.0;
            if (announce)
                gs.advisor.Post(MsgCategory.City, MsgSeverity.Info,
                    $"Mayor {mayor.name} has formed a town council. City Hall will send requests: do them for grants and goodwill. Approval sets our subsidy.");
        }

        /// <summary>Mandates, levies and elections (City).</summary>
        public void EnableMandates(bool announce = true)
        {
            if (mandatesEnabled) return;
            Start(announce);
            mandatesEnabled = true;
            if (nextElectionDay < 0) nextElectionDay = NextSessionDay(gs.clock.Day + Balance.ElectionEveryDays - 7);
        }

        /// <summary>Called by the progression system when the game starts (silently) and after every new city size.</summary>
        public void SyncWithProgress(bool announce)
        {
            if (gs.Has(Feature.Council)) Start(announce);
            if (gs.Has(Feature.CityTargets)) EnableMandates(announce);
        }

        static int NextSessionDay(int fromDay)
        {
            int d = Mathf.Max(7, fromDay);
            return (d + 6) / 7 * 7;   // council sessions are on Mondays (day index 7, 14, 21...)
        }

        Mayor NewMayor(Mayor except)
        {
            var m = new Mayor
            {
                name = rng.Pick(FirstNames) + " " + rng.Pick(LastNames),
                agenda = (Agenda)rng.Range(0, 4),
                face = rng.Range(1, 1_000_000)
            };
            if (except != null)
            {
                while (m.agenda == except.agenda) m.agenda = (Agenda)rng.Range(0, 4);
                while (m.name == except.name) m.name = rng.Pick(FirstNames) + " " + rng.Pick(LastNames);
            }
            return m;
        }

        // ------------------------------------------------------------------ ticks

        public void MinuteTick()
        {
            if (!gs.governmentEnabled || gameOver) return;
            int minute = Mathf.RoundToInt(gs.clock.MinuteOfDay) % Balance.MinutesPerDay;
            if (started)
            {
                if (minute % 5 == 0) Evaluate();
                if (gs.clock.Day % 7 == 0 && gs.clock.Day > 0 && minute == Balance.CouncilSessionMinute && lastSessionDay != gs.clock.Day)
                    HoldSession();
                if (Now >= nextProposalAt && minute >= 7 * 60 && minute <= 21 * 60)
                {
                    // City Hall gives a metro in the red room to fix its books.
                    if (CountOpen() < MaxOpen && gs.econ.money >= 0) Propose();
                    nextProposalAt = Now + rng.Range(Balance.ProposalIntervalMin, Balance.ProposalIntervalMax) * Balance.MinutesPerDay;
                }
            }
        }

        public void DayTick()
        {
            if (!gs.governmentEnabled || gameOver) return;
            if (started)
            {
                JudgeDay();
                float mood = Mood();
                approval = Mathf.Clamp(approval + (mood - approval) * Balance.ApprovalDrift, 0f, 100f);
                approvalHistory.Add(approval);
                if (approvalHistory.Count > 28) approvalHistory.RemoveAt(0);
            }
            CheckFinances();
        }

        int CountOpen()
        {
            int n = 0;
            foreach (var r in open)
                if (r.kind != RequestKind.RepayBailout) n++;
            return n;
        }

        // ------------------------------------------------------------------ approval

        /// <summary>How the city feels about the metro (0-100), which approval drifts towards every night.</summary>
        public float Mood()
        {
            float sat = gs.satisfaction.yesterday;
            float m = 25f + 70f * Mathf.InverseLerp(0.4f, 0.88f, sat);
            m += (AgendaScore() - 0.5f) * 20f;
            var y = gs.econ.Yesterday;
            if (y != null) m -= Mathf.Min(15f, Mathf.Max(0f, -y.Get(LedgerCat.Fines)) / 100_000f * 3f);
            if (gs.econ.money < 0) m -= 8f;
            return Mathf.Clamp(m, 0f, 100f);
        }

        /// <summary>0..1: how well the metro does on what this mayor cares about.</summary>
        public float AgendaScore()
        {
            switch (mayor.agenda)
            {
                case Agenda.Growth: return Mathf.InverseLerp(0.1f, 0.4f, gs.city.CoverageRatio);
                case Agenda.Thrift:
                {
                    var y = gs.econ.Yesterday;
                    float op = y != null ? y.OperatingNet : 0f;
                    return Mathf.InverseLerp(-50_000f, 250_000f * (1f + gs.progress.tier), op);
                }
                case Agenda.Green:
                {
                    var last = gs.stats.LastDay;
                    float riders = last != null ? last.riders : 0f;
                    return Mathf.InverseLerp(0.03f, 0.15f, riders / Mathf.Max(1f, gs.city.revealedPopulation));
                }
                default: return Mathf.InverseLerp(4.2f, 1.8f, gs.econ.baseFare) * 0.5f + Mathf.InverseLerp(0.5f, 0.9f, gs.satisfaction.yesterday) * 0.5f;
            }
        }

        public static string AgendaName(Agenda a)
        {
            switch (a)
            {
                case Agenda.Growth: return "Growth";
                case Agenda.Thrift: return "Thrift";
                case Agenda.Green: return "Green";
                default: return "People first";
            }
        }

        public static string AgendaBlurb(Agenda a)
        {
            switch (a)
            {
                case Agenda.Growth: return "Wants every neighbourhood connected and the network expanding.";
                case Agenda.Thrift: return "Wants the metro to pay its own way and cost taxpayers less.";
                case Agenda.Green: return "Wants people out of their cars and onto trains.";
                default: return "Wants fair fares, comfortable trips and stations everyone can use.";
            }
        }

        public string AgendaKpi()
        {
            switch (mayor.agenda)
            {
                case Agenda.Growth: return $"Residents near a station: {Fmt.Pct(gs.city.CoverageRatio)}";
                case Agenda.Thrift:
                {
                    var y = gs.econ.Yesterday;
                    return $"Yesterday's operating result: {Fmt.MoneySigned(y != null ? y.OperatingNet : 0f)}";
                }
                case Agenda.Green:
                {
                    var last = gs.stats.LastDay;
                    float r = last != null ? last.riders : 0f;
                    return $"Trips per resident: {r / Mathf.Max(1f, gs.city.revealedPopulation):0.00}";
                }
                default: return $"Fare {Fmt.Fare(gs.econ.baseFare)} · satisfaction {Fmt.Pct(gs.satisfaction.yesterday)}";
            }
        }

        void ChangeApproval(float delta) => approval = Mathf.Clamp(approval + delta, 0f, 100f);

        // ------------------------------------------------------------------ requests: player actions

        public void Accept(CouncilRequest r)
        {
            if (r == null || r.state != RequestState.Proposed) return;
            r.state = RequestState.Active;
            if (r.kind != RequestKind.Event) r.deadline = Now + (r.deadline - r.issuedAt);
            r.issuedAt = Now;
            gs.advisor.Post(MsgCategory.City, MsgSeverity.Info, $"We agreed to City Hall's request: {r.title}.", district: r.districtId);
            Evaluate();
            RequestChanged?.Invoke(r);
        }

        public void Decline(CouncilRequest r)
        {
            if (r == null || r.state != RequestState.Proposed || r.mandate) return;
            r.state = RequestState.Declined;
            int cost = DeclineCost(r);
            ChangeApproval(-cost);
            declined++;
            weekDeclined++;
            Close(r);
            gs.advisor.Post(MsgCategory.City, MsgSeverity.Warning, $"We turned down City Hall: {r.title}. Approval −{cost}.", district: r.districtId);
        }

        public int DeclineCost(CouncilRequest r) => MatchesAgenda(r.kind) ? 3 : 1;

        /// <summary>What's still owed on a levy or the bailout.</summary>
        public static float Remaining(CouncilRequest r) => Mathf.Max(0f, r.target - r.paidAmount);

        /// <summary>Pays a levy or the bailout from cash, in full or as an instalment (whatever cash allows, up to max).</summary>
        public bool Pay(CouncilRequest r, float max = float.MaxValue)
        {
            if (r == null || r.state != RequestState.Active || !r.IsPayment || r.paid) return false;
            float amount = Mathf.Min(Remaining(r), Mathf.Min(max, (float)gs.econ.money));
            amount = Mathf.Floor(amount / 1000f) * 1000f;
            if (amount < 1000f && Remaining(r) >= 1000f) return false;
            if (amount > 0f) gs.econ.Charge(amount, LedgerCat.Fines);
            r.paidAmount += amount;
            if (Remaining(r) < 1000f) r.paid = true;
            Evaluate();
            RequestChanged?.Invoke(r);
            return true;
        }

        public void Endorse(int who)
        {
            if (challenger == null) return;
            endorsed = endorsed == who ? -1 : Mathf.Clamp(who, -1, 1);
        }

        // ------------------------------------------------------------------ requests: evaluation

        public void Evaluate()
        {
            double now = Now;
            for (int i = open.Count - 1; i >= 0; i--)
            {
                var r = open[i];
                if (r.state == RequestState.Proposed)
                {
                    Measure(r);
                    if (now > r.answerBy)
                    {
                        if (r.mandate) Accept(r);
                        else
                        {
                            r.state = RequestState.Expired;
                            ChangeApproval(-1f);
                            Close(r);
                            gs.advisor.Post(MsgCategory.City, MsgSeverity.Info, $"City Hall gave up waiting for an answer: {r.title}. Approval −1.", district: r.districtId);
                        }
                    }
                    continue;
                }
                if (r.state != RequestState.Active) continue;
                bool success = Measure(r);
                if (success) Succeed(r);
                else if (now > r.deadline)
                {
                    if (r.IsHold && r.holdSince >= 0 && now - r.holdSince >= Balance.HoldMinutes) Succeed(r);
                    else Fail(r);
                }
            }
        }

        /// <summary>Updates progress and status text. Returns true when the request is fulfilled right now.</summary>
        bool Measure(CouncilRequest r)
        {
            var net = gs.net;
            switch (r.kind)
            {
                case RequestKind.ConnectDistrict:
                {
                    var d = District(r.districtId);
                    float c = d != null ? d.Coverage : 0f;
                    r.progress = Mathf.Clamp01(c / r.target);
                    r.status = d != null ? $"{Fmt.Pct(c)} of {d.name} within a short walk (need {Fmt.Pct(r.target)})" : "";
                    return d != null && c >= r.target;
                }
                case RequestKind.StationNear:
                case RequestKind.Event:
                {
                    var lm = LandmarkById(r.landmarkId);
                    var st = lm != null ? ServedStationNear(lm.pos) : null;
                    if (r.kind == RequestKind.StationNear)
                    {
                        r.progress = st != null ? 1f : 0f;
                        r.status = st != null ? $"{st.name} serves it" : $"No served station within {Balance.WalkRadius:0} km yet";
                        return st != null;
                    }
                    double now = Now;
                    if (now < r.eventStart - Balance.EventArrivalMinutes)
                    {
                        r.progress = st != null ? 0.5f : 0f;
                        r.status = st != null ? $"{st.name} will serve the venue · event {EventWhen(r)}" : $"No station near the venue yet · event {EventWhen(r)}";
                        return false;
                    }
                    float share = r.wanted > 1f ? r.served / r.wanted : 0f;
                    r.progress = Mathf.Clamp01(share / Balance.EventServedShare);
                    r.status = $"{Fmt.Count(r.served)} of {Fmt.Count(r.wanted)} fans carried so far (need {Fmt.Pct(Balance.EventServedShare)})";
                    if (now > r.eventEnd + Balance.EventDepartMinutes) return share >= Balance.EventServedShare;
                    return false;
                }
                case RequestKind.Ridership:
                {
                    float best = gs.stats.ridersToday;
                    r.progress = Mathf.Clamp01(best / r.target);
                    r.status = $"{Fmt.Count(best)} trips today of {Fmt.Count(r.target)}";
                    return best >= r.target;
                }
                case RequestKind.Satisfaction:
                    r.progress = Mathf.Clamp01(gs.satisfaction.score / r.target);
                    r.status = $"Now {gs.satisfaction.Percent}% · judged at midnight";
                    return false;
                case RequestKind.ProfitDay:
                    r.progress = Mathf.Clamp01(gs.econ.today.OperatingNet / r.target);
                    r.status = $"So far today {Fmt.MoneySigned(gs.econ.today.OperatingNet)} · judged at midnight";
                    return false;
                case RequestKind.CrowdRelief:
                {
                    var s = net.GetStation(r.stationId);
                    r.status = s != null ? $"{s.name} peak today {Fmt.Pct(s.peakCrowdToday)} · needs a full day under 100%" : "Station closed";
                    r.progress = s != null && s.peakCrowdToday < 1f ? 0.5f : 0f;
                    return false;
                }
                case RequestKind.ServedResidents:
                {
                    float served = gs.city.servedPopulation;
                    r.progress = Mathf.Clamp01(served / r.target);
                    r.status = $"{Fmt.Count(served)} of {Fmt.Count(r.target)} residents near a station";
                    return served >= r.target;
                }
                case RequestKind.Accessibility:
                {
                    int n = 0;
                    foreach (var s in net.AliveStations())
                        if (s.elevators && s.lines.Count > 0) n++;
                    r.progress = Mathf.Clamp01(n / r.target);
                    r.status = $"{n} of {r.target:0} stations with elevators";
                    return n >= r.target;
                }
                case RequestKind.Interchanges:
                {
                    int n = 0;
                    foreach (var s in net.AliveStations())
                        if (s.lines.Count >= 2) n++;
                    r.progress = Mathf.Clamp01(n / r.target);
                    r.status = $"{n} of {r.target:0} interchange stations";
                    return n >= r.target;
                }
                case RequestKind.FareCap:
                case RequestKind.NoSurcharge:
                {
                    bool ok = r.kind == RequestKind.FareCap ? gs.econ.baseFare <= r.target + 0.001f : gs.econ.peakSurcharge <= 0.001f;
                    if (r.state == RequestState.Active)
                    {
                        if (!ok) r.holdSince = -1;
                        else if (r.holdSince < 0) r.holdSince = Now;
                    }
                    double held = r.holdSince >= 0 ? Now - r.holdSince : 0;
                    r.progress = Mathf.Clamp01((float)(held / Balance.HoldMinutes));
                    string cond = r.kind == RequestKind.FareCap ? $"fare {Fmt.Fare(gs.econ.baseFare)} (cap {Fmt.Fare(r.target)})" : $"surcharge {Fmt.Pct(gs.econ.peakSurcharge)}";
                    r.status = ok ? $"Holding: {cond} · {held / 60.0:0}h of {Balance.HoldMinutes / 60:0}h" : $"Not yet: {cond}";
                    return false;
                }
                case RequestKind.Levy:
                case RequestKind.RepayBailout:
                    r.progress = r.paid ? 1f : Mathf.Clamp01(r.paidAmount / Mathf.Max(1f, r.target));
                    r.status = r.paid ? "Paid in full" : r.paidAmount > 0f
                        ? $"Paid {Fmt.Money(r.paidAmount)} of {Fmt.Money(r.target)} · cash {Fmt.Money(gs.econ.money)}"
                        : $"Owe {Fmt.Money(r.target)}, payable in instalments · cash {Fmt.Money(gs.econ.money)}";
                    return r.paid;
            }
            return false;
        }

        void JudgeDay()
        {
            var y = gs.econ.Yesterday;
            for (int i = open.Count - 1; i >= 0; i--)
            {
                var r = open[i];
                if (r.state != RequestState.Active || !r.JudgedAtMidnight) continue;
                bool ok = false;
                switch (r.kind)
                {
                    case RequestKind.Satisfaction: ok = gs.satisfaction.yesterday >= r.target; break;
                    case RequestKind.ProfitDay: ok = y != null && y.OperatingNet >= r.target; break;
                    case RequestKind.CrowdRelief:
                    {
                        var s = gs.net.GetStation(r.stationId);
                        if (s == null)
                        {
                            r.state = RequestState.Expired;
                            Close(r);
                            continue;
                        }
                        // Only a full day of service counts.
                        ok = Now - r.issuedAt >= Balance.MinutesPerDay * 0.9 && s.peakCrowdYesterday < 1f;
                        break;
                    }
                }
                if (ok) Succeed(r);
            }
        }

        void Succeed(CouncilRequest r)
        {
            r.state = RequestState.Done;
            r.progress = 1f;
            if (r.reward > 0f) gs.econ.Earn(r.reward, LedgerCat.Grants);
            ChangeApproval(r.approvalReward);
            completed++;
            weekDone++;
            var lm = LandmarkById(r.landmarkId);
            if (lm != null) lm.built = true;
            gs.satisfaction.Nudge(0.005f);
            Close(r);
            string grant = r.reward > 0f ? $" Grant {Fmt.Money(r.reward)}." : "";
            gs.advisor.Post(MsgCategory.City, MsgSeverity.Good, $"City Hall is pleased: {r.title} — done.{grant} Approval +{r.approvalReward}.", district: r.districtId, station: r.stationId);
        }

        void Fail(CouncilRequest r)
        {
            r.state = RequestState.Failed;
            ChangeApproval(-r.approvalPenalty);
            if (r.fine > 0f) gs.econ.Charge(r.fine, LedgerCat.Fines);
            failed++;
            weekFailed++;
            Close(r);
            string fine = r.fine > 0f ? $" Fine {Fmt.Money(r.fine)}." : "";
            gs.advisor.Post(MsgCategory.City, MsgSeverity.Critical, $"We let City Hall down: {r.title}.{fine} Approval −{r.approvalPenalty}.", district: r.districtId, station: r.stationId);
            if (r.kind == RequestKind.RepayBailout)
            {
                if (gs.options.difficulty == 0)
                {
                    // Relaxed: City Hall grumbles and extends the deadline.
                    ChangeApproval(-10f);
                    var again = MakeRepay(r.target);
                    again.letter = "We extended the repayment deadline once more. Our patience is not endless.";
                    Add(again, true);
                }
                else RevokeLicence("The metro never repaid City Hall's bailout. The city has taken over the network.");
            }
        }

        void Close(CouncilRequest r)
        {
            r.resolvedAt = Now;
            open.Remove(r);
            history.Add(r);
            if (history.Count > 30) history.RemoveAt(0);
            // Landmarks of unsuccessful requests disappear unless another open request uses them.
            if (r.landmarkId >= 0 && r.state != RequestState.Done)
            {
                bool used = false;
                foreach (var o in open)
                    if (o.landmarkId == r.landmarkId) used = true;
                var lm = LandmarkById(r.landmarkId);
                if (!used && lm != null && !lm.built) landmarks.Remove(lm);
            }
            RequestChanged?.Invoke(r);
        }

        void Add(CouncilRequest r, bool active)
        {
            r.id = nextId++;
            r.issuedAt = Now;
            r.state = active ? RequestState.Active : RequestState.Proposed;
            if (r.answerBy <= 0) r.answerBy = Now + Balance.ProposalAnswerHours * 60.0;
            open.Add(r);
            Measure(r);
            RequestChanged?.Invoke(r);
        }

        /// <summary>Restores a saved request (save games).</summary>
        public void RestoreRequest(CouncilRequest r, bool isOpen)
        {
            if (isOpen) open.Add(r);
            else history.Add(r);
        }

        // ------------------------------------------------------------------ requests: generation

        bool MatchesAgenda(RequestKind k)
        {
            switch (mayor.agenda)
            {
                case Agenda.Growth: return k == RequestKind.ConnectDistrict || k == RequestKind.StationNear || k == RequestKind.ServedResidents || k == RequestKind.Interchanges;
                case Agenda.Thrift: return k == RequestKind.ProfitDay || k == RequestKind.Levy || k == RequestKind.Ridership;
                case Agenda.Green: return k == RequestKind.ServedResidents || k == RequestKind.Ridership || k == RequestKind.FareCap;
                default: return k == RequestKind.Satisfaction || k == RequestKind.FareCap || k == RequestKind.Accessibility || k == RequestKind.NoSurcharge || k == RequestKind.CrowdRelief;
            }
        }

        float Weight(RequestKind k)
        {
            float w = 1f;
            if (MatchesAgenda(k)) w = 3f;
            switch (k)
            {
                case RequestKind.Event: w = 1.3f; break;
                case RequestKind.ConnectDistrict:
                case RequestKind.StationNear: w *= 1.2f; break;
            }
            return w;
        }

        /// <summary>Sends a specific kind of request now (tests and debugging). Null if it can't be asked right now.</summary>
        public CouncilRequest Propose(RequestKind kind)
        {
            var r = Make(kind);
            if (r == null) return null;
            Issue(r);
            return r;
        }

        /// <summary>Brings the next election forward to the coming session and names the challenger.</summary>
        public void AnnounceElection()
        {
            EnableMandates(false);
            if (challenger == null) challenger = NewMayor(mayor);
            nextElectionDay = Mathf.Max(gs.clock.Day + 1, (gs.clock.Day / 7 + 1) * 7);
        }

        void Issue(CouncilRequest r)
        {
            if (r.kind == RequestKind.Levy) r.mandate = true;
            if (r.mandate)
            {
                r.fine = r.kind == RequestKind.Levy ? r.target * 1.5f : Balance.MandateFine * RewardScale * gs.FineMultiplier;
                r.approvalPenalty += 4;
            }
            r.letter += $"\n— Mayor {mayor.name}";
            Add(r, false);
            gs.advisor.Post(MsgCategory.City, r.mandate ? MsgSeverity.Warning : MsgSeverity.Info,
                r.mandate ? $"City Hall orders: {r.title}." : $"New request from City Hall: {r.title}. Accept or decline in the goals panel.", district: r.districtId, station: r.stationId);
        }

        /// <summary>Sends the next request. Returns it (or null if nothing sensible could be asked).</summary>
        public CouncilRequest Propose()
        {
            var kinds = new List<RequestKind>();
            var weights = new List<float>();
            foreach (RequestKind k in Enum.GetValues(typeof(RequestKind)))
            {
                if (k == RequestKind.RepayBailout || !Applicable(k)) continue;
                kinds.Add(k);
                weights.Add(Weight(k));
            }
            for (int attempt = 0; attempt < 4 && kinds.Count > 0; attempt++)
            {
                float total = 0f;
                foreach (var w in weights) total += w;
                float pick = rng.Range(0f, total);
                int idx = 0;
                for (; idx < kinds.Count - 1; idx++)
                {
                    pick -= weights[idx];
                    if (pick <= 0f) break;
                }
                var r = Make(kinds[idx]);
                if (r == null)
                {
                    kinds.RemoveAt(idx);
                    weights.RemoveAt(idx);
                    continue;
                }
                if (mandatesEnabled && r.kind != RequestKind.Event && r.kind != RequestKind.Levy && rng.Chance(Balance.MandateChance)) r.mandate = true;
                Issue(r);
                return r;
            }
            return null;
        }

        bool Applicable(RequestKind k)
        {
            if (blocked.Contains(k)) return false;
            foreach (var r in open)
                if (r.kind == k) return false;
            var net = gs.net;
            switch (k)
            {
                case RequestKind.Ridership:
                case RequestKind.ServedResidents:
                case RequestKind.Satisfaction: return net.LineCount > 0;
                case RequestKind.Interchanges: return gs.progress.MaxLines >= 2 && net.LineCount >= 1;
                case RequestKind.FareCap: return gs.Has(Feature.Finance) && gs.econ.baseFare > 1.7f;
                case RequestKind.NoSurcharge: return gs.Has(Feature.Surcharge) && gs.econ.peakSurcharge > 0.01f;
                case RequestKind.ProfitDay: return gs.Has(Feature.Finance) && net.LineCount > 0;
                case RequestKind.Accessibility: return gs.Has(Feature.Upgrades) && net.StationCount >= 6;
                case RequestKind.CrowdRelief: return gs.Has(Feature.Upgrades) && MostCrowded() != null;
                case RequestKind.Levy: return mandatesEnabled && gs.econ.money > 600_000;
                case RequestKind.Event: return net.LineCount > 0;
                default: return true;
            }
        }

        CouncilRequest Make(RequestKind k)
        {
            float s = RewardScale;
            var r = new CouncilRequest { kind = k };
            double day = Balance.MinutesPerDay;
            var last = gs.stats.LastDay;
            switch (k)
            {
                case RequestKind.ConnectDistrict:
                {
                    District pick = null;
                    foreach (var d in gs.city.districts)
                    {
                        if (!d.revealed || !gs.city.InPlayArea(d.center) || d.Coverage >= 0.15f || d.population < 15_000f || Targeted(d.id)) continue;
                        if (pick == null || d.population > pick.population) pick = d;
                    }
                    if (pick == null) return null;
                    r.districtId = pick.id;
                    r.target = 0.25f;
                    r.title = $"Connect {pick.name}";
                    r.letter = $"{pick.name} ({Fmt.Count(pick.population)} residents) feels forgotten by the metro. Get at least a quarter of its residents within a short walk of a station.";
                    r.deadline = Now + 2.5 * day;
                    r.reward = (200_000f + pick.population * 0.6f) * DiffScale;
                    r.approvalReward = 6;
                    r.approvalPenalty = 6;
                    return r;
                }
                case RequestKind.StationNear:
                {
                    var lm = NewLandmark(rng.Pick(new[] { LandmarkKind.Hospital, LandmarkKind.University, LandmarkKind.Museum, LandmarkKind.Market, LandmarkKind.Library, LandmarkKind.Ferry, LandmarkKind.Airport }));
                    if (lm == null) return null;
                    var d = District(lm.districtId);
                    r.landmarkId = lm.id;
                    r.districtId = lm.districtId;
                    r.title = $"Serve {lm.name}";
                    r.letter = $"{LandmarkIntro(lm)} in {(d != null ? d.name : "the city")}. People will need a station within a short walk ({Balance.WalkRadius:0} km) of it.";
                    r.deadline = Now + 3 * day;
                    r.reward = 300_000f * s;
                    r.approvalReward = 6;
                    r.approvalPenalty = 6;
                    return r;
                }
                case RequestKind.Ridership:
                {
                    float best = Mathf.Max(last != null ? last.riders : 0f, gs.stats.ridersToday);
                    if (best < 5000f) return null;
                    r.target = Mathf.Round(Mathf.Max(best * 1.1f, best + 6000f) / 1000f) * 1000f;
                    r.title = $"Carry {Fmt.Count(r.target)} trips in a day";
                    r.letter = $"The council wants proof the metro is worth the money: {Fmt.Count(r.target)} trips in a single day.";
                    r.deadline = Now + 3 * day;
                    r.reward = (150_000f + r.target * 1f) * DiffScale;
                    r.approvalReward = 5;
                    r.approvalPenalty = 5;
                    return r;
                }
                case RequestKind.Satisfaction:
                {
                    float cur = gs.satisfaction.score;
                    if (cur > 0.83f) return null;
                    r.target = Mathf.Min(0.85f, Mathf.Ceil((cur + 0.04f) * 20f) / 20f);
                    r.title = $"End a day with {Fmt.Pct(r.target)} satisfaction";
                    r.letter = "Our inbox is full of complaints about the metro. Show us riders are happy: end a day with satisfaction of at least " + Fmt.Pct(r.target) + ".";
                    r.deadline = Now + 3 * day;
                    r.reward = 300_000f * s;
                    r.approvalReward = 7;
                    r.approvalPenalty = 6;
                    return r;
                }
                case RequestKind.ServedResidents:
                {
                    float served = gs.city.servedPopulation;
                    r.target = Mathf.Round((served * 1.06f + 20_000f) / 10_000f) * 10_000f;
                    if (r.target > gs.city.revealedPopulation * 0.92f) return null;
                    r.title = $"Put {Fmt.Count(r.target)} residents near a station";
                    r.letter = $"Too many people still can't walk to a station. Get {Fmt.Count(r.target)} residents within a short walk of the metro.";
                    r.deadline = Now + 3 * day;
                    r.reward = 300_000f * s;
                    r.approvalReward = 6;
                    r.approvalPenalty = 6;
                    return r;
                }
                case RequestKind.FareCap:
                {
                    r.target = Mathf.Max(1.5f, Mathf.Floor((gs.econ.baseFare - 0.3f) * 10f) / 10f);
                    r.title = $"Cap the fare at {Fmt.Fare(r.target)}";
                    r.letter = $"Families say the metro is too expensive. Keep the ticket price at {Fmt.Fare(r.target)} or less for a full day. City Hall will top up our budget for the lost fares.";
                    r.deadline = Now + 2 * day;
                    r.reward = 200_000f * s;
                    r.approvalReward = 9;
                    r.approvalPenalty = 7;
                    return r;
                }
                case RequestKind.NoSurcharge:
                    r.title = "Drop the rush-hour surcharge";
                    r.letter = "Commuters are furious about paying extra at rush hour. Run without a peak surcharge for a full day.";
                    r.deadline = Now + 2 * day;
                    r.reward = 150_000f * s;
                    r.approvalReward = 7;
                    r.approvalPenalty = 6;
                    return r;
                case RequestKind.Accessibility:
                {
                    int n = 0, servedStations = 0;
                    foreach (var st in gs.net.AliveStations())
                    {
                        if (st.lines.Count == 0) continue;
                        servedStations++;
                        if (st.elevators) n++;
                    }
                    r.target = Mathf.Min(servedStations, n + (servedStations > 20 ? 3 : 2));
                    if (r.target <= n) return null;
                    r.title = $"Elevators at {r.target:0} stations";
                    r.letter = "Wheelchair users and parents with strollers can't use most of our stations. City Hall wants elevators at " + r.target.ToString("0") + " stations.";
                    r.deadline = Now + 3 * day;
                    r.reward = 250_000f * s;
                    r.approvalReward = 7;
                    r.approvalPenalty = 6;
                    return r;
                }
                case RequestKind.Interchanges:
                {
                    int n = 0;
                    foreach (var st in gs.net.AliveStations())
                        if (st.lines.Count >= 2) n++;
                    r.target = n + 1;
                    r.title = n == 0 ? "Build an interchange" : $"Have {r.target:0} interchange stations";
                    r.letter = "People are tired of trips that need a bus between two metro lines. Make the lines meet: " + (n == 0 ? "one interchange station." : $"{r.target:0} interchange stations in total.");
                    r.deadline = Now + 3 * day;
                    r.reward = 200_000f * s;
                    r.approvalReward = 5;
                    r.approvalPenalty = 5;
                    return r;
                }
                case RequestKind.CrowdRelief:
                {
                    var st = MostCrowded();
                    if (st == null) return null;
                    r.stationId = st.id;
                    r.districtId = st.district;
                    r.title = $"End the crush at {st.name}";
                    r.letter = $"Safety inspectors keep calling about {st.name}. Get it through a full day without the platform overflowing: more trains, longer trains, or a bigger station.";
                    r.deadline = Now + 3 * day;
                    r.reward = 250_000f * s;
                    r.approvalReward = 6;
                    r.approvalPenalty = 6;
                    return r;
                }
                case RequestKind.Levy:
                {
                    r.target = Mathf.Round(Mathf.Clamp((float)gs.econ.money * 0.08f, 150_000f, 800_000f * s) / 10_000f) * 10_000f;
                    r.title = $"Pay the {Fmt.Money(r.target)} infrastructure levy";
                    r.letter = "The city budget is tight and the roads need repairs your construction sites made worse. Pay an infrastructure levy of " + Fmt.Money(r.target) + ".";
                    r.deadline = Now + 3 * day;
                    r.approvalReward = 5;
                    r.approvalPenalty = 6;
                    return r;
                }
                case RequestKind.ProfitDay:
                {
                    var y = gs.econ.Yesterday;
                    float op = y != null ? y.OperatingNet : 0f;
                    r.target = Mathf.Round(Mathf.Max(100_000f, op * 1.15f + 50_000f) / 10_000f) * 10_000f;
                    r.title = $"Make {Fmt.Money(r.target)} operating profit in a day";
                    r.letter = "Taxpayers ask why they fund the metro. Show the council the metro can pay its way: end a day with an operating profit of " + Fmt.Money(r.target) + ".";
                    r.deadline = Now + 3 * day;
                    r.reward = 100_000f * s;
                    r.approvalReward = 6;
                    r.approvalPenalty = 5;
                    return r;
                }
                case RequestKind.Event:
                {
                    var lm = NewLandmark(rng.Pick(new[] { LandmarkKind.Stadium, LandmarkKind.Arena, LandmarkKind.Expo }));
                    if (lm == null) return null;
                    var d = District(lm.districtId);
                    int dayIdx = gs.clock.Day + (gs.clock.MinuteOfDay > 12 * 60 ? 2 : 1);
                    bool weekend = (dayIdx % 7) >= 5;
                    int startMinute = weekend ? rng.Range(13, 18) * 60 : rng.Range(18, 20) * 60;
                    r.eventStart = dayIdx * day + startMinute;
                    r.eventEnd = r.eventStart + 180.0;
                    r.landmarkId = lm.id;
                    r.districtId = lm.districtId;
                    r.target = Mathf.Round(Mathf.Clamp(6_000f * (1f + 0.5f * gs.progress.tier), 6_000f, 30_000f) / 1000f) * 1000f;
                    string what = lm.kind == LandmarkKind.Stadium ? rng.Pick(new[] { "The cup final", "The derby match", "A sold-out match" })
                        : lm.kind == LandmarkKind.Arena ? rng.Pick(new[] { "A stadium-sized concert", "The basketball finals", "A world-tour concert" })
                        : rng.Pick(new[] { "The trade fair", "The comic convention", "The car show" });
                    r.title = $"{what} at {lm.name}";
                    r.letter = $"{what} is on {GameClock.DayNameOf(dayIdx)} at {GameClock.FormatMinute(startMinute)} at {lm.name} in {(d != null ? d.name : "the city")}. About {Fmt.Count(r.target)} fans will want the metro there and back. Put a station within a short walk and make sure it can cope.";
                    r.deadline = r.eventEnd + Balance.EventDepartMinutes + 1;
                    r.answerBy = Math.Min(Now + Balance.ProposalAnswerHours * 60.0, r.eventStart - Balance.EventArrivalMinutes - 60);
                    if (r.answerBy <= Now + 30) return null;
                    r.reward = (150_000f + r.target * 12f) * DiffScale;
                    r.approvalReward = 8;
                    r.approvalPenalty = 7;
                    return r;
                }
            }
            return null;
        }

        CouncilRequest MakeRepay(float amount)
        {
            return new CouncilRequest
            {
                kind = RequestKind.RepayBailout,
                mandate = true,
                target = amount,
                title = $"Repay the {Fmt.Money(amount)} bailout",
                letter = "City Hall rescued the metro from bankruptcy. That money must come back to the city budget.",
                deadline = Now + Balance.BailoutRepayDays * (double)Balance.MinutesPerDay,
                approvalReward = 10,
                approvalPenalty = 15
            };
        }

        bool Targeted(int districtId)
        {
            foreach (var r in open)
                if (r.kind == RequestKind.ConnectDistrict && r.districtId == districtId) return true;
            return false;
        }

        Station MostCrowded()
        {
            Station best = null;
            foreach (var s in gs.net.AliveStations())
            {
                if (s.lines.Count == 0 || s.peakCrowdYesterday < 1f) continue;
                if (best == null || s.peakCrowdYesterday > best.peakCrowdYesterday) best = s;
            }
            return best;
        }

        void OnDistrictRevealed(District d)
        {
            // A brand-new district is the most natural thing for City Hall to ask about. Districts that
            // open with a new city size are left to the growth card (and later requests).
            if (!started || gameOver || !gs.governmentEnabled || CountOpen() >= MaxOpen || gs.progress.Growing) return;
            if (d.population < 1f) return;
            var r = new CouncilRequest
            {
                kind = RequestKind.ConnectDistrict,
                districtId = d.id,
                target = 0.25f,
                title = $"Connect {d.name}",
                letter = $"{d.name} ({d.TypeLabel.ToLowerInvariant()}) is opening up. Get a quarter of its residents within a short walk of a station while it fills up.\n— Mayor {mayor.name}",
                deadline = Now + 3.0 * Balance.MinutesPerDay,
                reward = (220_000f + 40_000f * gs.progress.tier) * DiffScale,
                approvalReward = 6,
                approvalPenalty = 5
            };
            Add(r, false);
        }

        // ------------------------------------------------------------------ landmarks

        public Landmark LandmarkById(int id)
        {
            if (id < 0) return null;
            foreach (var l in landmarks)
                if (l.id == id) return l;
            return null;
        }

        public Station ServedStationNear(Vector2 p)
        {
            Station best = null;
            float bd = Balance.WalkRadius;
            foreach (var s in gs.net.AliveStations())
            {
                if (s.lines.Count == 0) continue;
                float d = Vector2.Distance(s.pos, p);
                if (d <= bd)
                {
                    bd = d;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>Places a new landmark on populated land, away from existing service but not too far out.</summary>
        Landmark NewLandmark(LandmarkKind kind)
        {
            var city = gs.city;
            Vector2 best = default;
            float bestScore = float.MinValue;
            int bestDistrict = -1;
            bool anyStation = gs.net.StationCount > 0;
            for (int attempt = 0; attempt < 160; attempt++)
            {
                int i = rng.Range(0, city.CellCount);
                if (!city.IsCellActive(i)) continue;
                Vector2 p = city.CellCenter(i);
                if (!city.InPlayArea(p, 1.2f)) continue;
                float weight = city.res[i] + city.jobs[i] + city.att[i];
                if (weight <= 1f) continue;
                float near = float.MaxValue;
                foreach (var s in gs.net.AliveStations()) near = Mathf.Min(near, Vector2.Distance(s.pos, p));
                if (anyStation && (near < Balance.WalkRadius + 0.6f || near > 7f)) continue;
                bool clash = false;
                foreach (var l in landmarks)
                    if (Vector2.Distance(l.pos, p) < 2.5f) clash = true;
                if (clash) continue;
                float score = Mathf.Log(weight + 1f) - (anyStation ? near * 0.25f : 0f) + rng.Value() * 0.8f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                    bestDistrict = city.DistrictAt(p);
                }
            }
            if (bestScore == float.MinValue) return null;
            var d = District(bestDistrict);
            var lm = new Landmark { id = nextLandmarkId++, kind = kind, pos = best, districtId = bestDistrict };
            lm.name = LandmarkName(kind, d != null ? d.name : gs.city.name);
            landmarks.Add(lm);
            return lm;
        }

        string LandmarkName(LandmarkKind k, string place)
        {
            switch (k)
            {
                case LandmarkKind.Hospital: return rng.Pick(new[] { $"{place} General Hospital", $"St. {rng.Pick(Saints)}'s Hospital", $"{place} Children's Hospital" });
                case LandmarkKind.University: return rng.Pick(new[] { $"{place} University", $"the {place} Polytechnic", $"{place} Medical School" });
                case LandmarkKind.Stadium: return rng.Pick(new[] { $"{place} Stadium", $"{place} Park Stadium", "the City Ground" });
                case LandmarkKind.Arena: return rng.Pick(new[] { $"the {place} Arena", "the Riverside Arena", "the Dome" });
                case LandmarkKind.Museum: return rng.Pick(new[] { $"the {place} Museum", "the Museum of Modern Art", "the Science Museum" });
                case LandmarkKind.Airport: return rng.Pick(new[] { $"{place} Airport terminal", "the new airport terminal" });
                case LandmarkKind.Market: return rng.Pick(new[] { $"{place} Market Hall", "the Central Market", "the night market" });
                case LandmarkKind.Library: return rng.Pick(new[] { $"{place} Library", "the City Library" });
                case LandmarkKind.Ferry: return rng.Pick(new[] { $"{place} Ferry Terminal", "the harbour ferry pier" });
                default: return rng.Pick(new[] { $"the {place} Expo Centre", "the Convention Centre" });
            }
        }

        static string LandmarkIntro(Landmark lm)
        {
            string n = char.ToUpperInvariant(lm.name[0]) + lm.name.Substring(1);
            switch (lm.kind)
            {
                case LandmarkKind.Hospital: return $"{n} opens its doors next week";
                case LandmarkKind.University: return $"{n} is moving into a new campus";
                case LandmarkKind.Airport: return $"{n} has just opened";
                case LandmarkKind.Museum: return $"{n} reopens after its renovation";
                case LandmarkKind.Ferry: return $"{n} is expanding";
                default: return $"{n} is the city's newest attraction";
            }
        }

        District District(int id) => id >= 0 && id < gs.city.districts.Count ? gs.city.districts[id] : null;

        string EventWhen(CouncilRequest r)
        {
            int d = (int)(r.eventStart / Balance.MinutesPerDay);
            int m = (int)(r.eventStart - d * (double)Balance.MinutesPerDay);
            return $"{GameClock.DayNameOf(d)} {GameClock.FormatMinute(m)}";
        }

        // ------------------------------------------------------------------ special events: extra trips

        readonly List<int> evStations = new List<int>();
        readonly List<float> evWeights = new List<float>();
        int evVersion = -1;

        /// <summary>Adds the fans' trips to and from event venues. Called by the passenger system every minute.</summary>
        public void SpawnEventTrips(PassengerSystem pax)
        {
            if (!started || gameOver) return;
            double now = Now;
            foreach (var r in open)
            {
                if (r.kind != RequestKind.Event || r.state != RequestState.Active) continue;
                double arriveFrom = r.eventStart - Balance.EventArrivalMinutes;
                double leaveTo = r.eventEnd + Balance.EventDepartMinutes;
                bool arriving = now >= arriveFrom && now < r.eventStart;
                bool leaving = now >= r.eventEnd && now < leaveTo;
                if (!arriving && !leaving) continue;
                // Bell-shaped flows: most fans arrive in the last hour and leave right after the final whistle.
                float t = arriving ? (float)((now - arriveFrom) / Balance.EventArrivalMinutes) : (float)((now - r.eventEnd) / Balance.EventDepartMinutes);
                float shape = arriving ? Mathf.Sin(t * Mathf.PI * 0.95f) * (0.4f + t) : Mathf.Exp(-t * 1.2f);
                float norm = arriving ? Balance.EventArrivalMinutes * 0.62f : Balance.EventDepartMinutes * 0.582f;
                float perMinute = r.target * shape / norm;
                var lm = LandmarkById(r.landmarkId);
                var venue = lm != null ? ServedStationNear(lm.pos) : null;
                r.wanted += perMinute;
                if (venue == null) continue;
                RefreshEventWeights(venue.id);
                for (int i = 0; i < evStations.Count; i++)
                {
                    int s = evStations[i];
                    float n = perMinute * evWeights[i];
                    r.served += arriving ? pax.AddTrips(s, venue.id, n) : pax.AddTrips(venue.id, s, n);
                }
            }
        }

        void RefreshEventWeights(int venue)
        {
            if (evVersion == gs.net.version * 7 + venue) return;
            evVersion = gs.net.version * 7 + venue;
            evStations.Clear();
            evWeights.Clear();
            float total = 0f;
            foreach (var s in gs.net.AliveStations())
            {
                if (s.id == venue || s.lines.Count == 0) continue;
                float w = Mathf.Max(50f, s.entriesYesterday);
                evStations.Add(s.id);
                evWeights.Add(w);
                total += w;
            }
            for (int i = 0; i < evWeights.Count; i++) evWeights[i] /= Mathf.Max(1f, total);
        }

        // ------------------------------------------------------------------ council sessions & elections

        void HoldSession()
        {
            lastSessionDay = gs.clock.Day;
            var rep = new CouncilReport
            {
                week = gs.clock.Day / 7,
                approvalBefore = approvalAtLastSession,
                done = weekDone,
                failed = weekFailed,
                declined = weekDeclined
            };
            if (mandatesEnabled && nextElectionDay >= 0 && gs.clock.Day >= nextElectionDay)
            {
                rep.election = RunElection();
                nextElectionDay = gs.clock.Day + Balance.ElectionEveryDays;
            }
            if (mandatesEnabled && nextElectionDay >= 0 && challenger == null && nextElectionDay - gs.clock.Day <= 7)
            {
                challenger = NewMayor(mayor);
                endorsed = -1;
                rep.challengerAnnounced = challenger;
            }
            rep.electionDay = nextElectionDay;
            // Ultimatums: a hostile council gives one week's warning, then revokes the licence.
            if (approval < Balance.UltimatumApproval)
            {
                if (ultimatum && gs.options.difficulty != 0)
                {
                    rep.ultimatumActive = true;
                    rep.verdict = "Licence revoked";
                    rep.quote = "We gave you a week. The council has voted to hand the metro to a new operator.";
                    rep.approvalNow = approval;
                    rep.subsidyFactor = SubsidyFactor;
                    rep.permitFactor = PermitFactor;
                    SessionHeld?.Invoke(rep);
                    RevokeLicence($"The council lost all confidence in the metro (approval {approval:0}%) and voted to revoke its operating licence.");
                    return;
                }
                ultimatum = true;
                rep.ultimatumIssued = true;
            }
            else if (ultimatum)
            {
                ultimatum = false;
                rep.ultimatumLifted = true;
            }
            rep.ultimatumActive = ultimatum;
            rep.approvalNow = approval;
            rep.verdict = Standing;
            rep.quote = Quote(rep);
            rep.subsidyFactor = SubsidyFactor;
            rep.permitFactor = PermitFactor;
            approvalAtLastSession = approval;
            weekDone = weekFailed = weekDeclined = 0;
            gs.advisor.Post(MsgCategory.City, ultimatum ? MsgSeverity.Critical : MsgSeverity.Info,
                ultimatum ? $"Council session: approval {approval:0}%. FINAL WARNING — raise it above {Balance.UltimatumApproval:0}% by next Monday or lose the operating licence."
                          : $"Council session: approval {approval:0}% ({Standing.ToLowerInvariant()}). Subsidy ×{SubsidyFactor:0.00}.");
            SessionHeld?.Invoke(rep);
        }

        string Quote(CouncilReport rep)
        {
            float d = rep.approvalNow - rep.approvalBefore;
            if (rep.ultimatumIssued) return "Frankly, the council is out of patience. Turn this around within a week, or we will find an operator who can.";
            if (rep.ultimatumLifted) return "You listened. The council withdraws its warning — don't make us issue another.";
            if (approval >= 80f) return "The metro is the pride of the city. Keep it up and City Hall will keep paying for it.";
            if (d > 4f) return "Good week. The council noticed what you did for the city.";
            if (d < -4f) return "A disappointing week. The council expects better, and quickly.";
            if (approval < 40f) return "Many councillors are asking why we fund the metro at all.";
            return "The council will keep watching. We have plenty more to ask of you.";
        }

        ElectionResult RunElection()
        {
            if (challenger == null) challenger = NewMayor(mayor);
            float citySat = gs.satisfaction.yesterday;
            float share = Mathf.Clamp(0.22f + 0.45f * citySat + rng.Range(-0.15f, 0.15f), 0.2f, 0.85f);
            bool incumbentWins = share >= 0.5f;
            var res = new ElectionResult
            {
                winner = incumbentWins ? mayor : challenger,
                loser = incumbentWins ? challenger : mayor,
                incumbentWon = incumbentWins,
                endorsed = endorsed,
                incumbentShare = share,
                approvalBefore = approval
            };
            if (endorsed >= 0)
            {
                res.endorsedWon = (endorsed == 0) == incumbentWins;
                ChangeApproval(res.endorsedWon ? Balance.EndorseSwing : -Balance.EndorseSwing);
            }
            if (!incumbentWins)
            {
                mayor = challenger;
                // A new administration gives the metro a partly fresh start.
                approval = Mathf.Clamp(50f + (approval - 50f) * 0.6f, 0f, 100f);
            }
            res.approvalAfter = approval;
            challenger = null;
            endorsed = -1;
            gs.advisor.Post(MsgCategory.City, MsgSeverity.Info, incumbentWins
                ? $"Election night: Mayor {mayor.name} is re-elected with {Fmt.Pct(share)} of the vote."
                : $"Election night: {mayor.name} wins and becomes mayor. New agenda: {AgendaName(mayor.agenda)}.");
            return res;
        }

        // ------------------------------------------------------------------ bankruptcy

        void CheckFinances()
        {
            var e = gs.econ;
            if (e.money >= 0) return;
            float limit = Balance.BankruptBase + e.NetworkValue() * 0.25f;
            bool broke = e.daysInRed >= Balance.BailoutDaysInRed || e.money < -limit;
            if (!broke)
            {
                gs.advisor.Post(MsgCategory.Finance, MsgSeverity.Critical,
                    $"We're in the red ({Fmt.Money(e.money)}), day {e.daysInRed} of {Balance.BailoutDaysInRed}. City Hall steps in after that. Cut costs, raise fares or borrow.");
                return;
            }
            if (bailouts == 0 || gs.options.difficulty == 0) Bailout();
            else RevokeLicence("The metro went bankrupt a second time. City Hall won't rescue it again and has taken over the network.");
        }

        void Bailout()
        {
            var e = gs.econ;
            float grant = Mathf.Round(Mathf.Max(Balance.BailoutMinimum, (float)-e.money + Balance.BailoutCushion) / 10_000f) * 10_000f;
            e.Earn(grant, LedgerCat.Grants);
            e.daysInRed = 0;
            bailouts++;
            ChangeApproval(-Balance.BailoutApprovalCost);
            var repay = MakeRepay(Mathf.Round(grant / 10_000f) * 10_000f);
            repay.letter += $"\n— Mayor {mayor.name}";
            Add(repay, true);
            gs.advisor.Post(MsgCategory.City, MsgSeverity.Critical,
                $"The metro is bankrupt. City Hall bailed us out with {Fmt.Money(grant)} — it must be repaid within {Balance.BailoutRepayDays} days. Approval −{Balance.BailoutApprovalCost:0}.");
            BailoutGranted?.Invoke(grant);
        }

        public void RevokeLicence(string reason)
        {
            if (gameOver) return;
            gameOver = true;
            gameOverReason = reason;
            gs.advisor.Post(MsgCategory.City, MsgSeverity.Critical, reason);
            LicenceRevoked?.Invoke(reason);
        }

        // ------------------------------------------------------------------ text helpers

        public string TimeLeft(CouncilRequest r)
        {
            double t = (r.state == RequestState.Proposed ? r.answerBy : r.kind == RequestKind.Event && Now < r.eventStart ? r.eventStart : r.deadline) - Now;
            string prefix = r.state == RequestState.Proposed ? "answer within " : r.kind == RequestKind.Event && Now < r.eventStart ? "starts in " : "";
            if (t <= 0) return r.kind == RequestKind.Event && Now < r.deadline ? "in progress" : "due now";
            if (t < 60) return prefix + Mathf.CeilToInt((float)t) + " min";
            if (t < Balance.MinutesPerDay) return prefix + Mathf.CeilToInt((float)(t / 60.0)) + " h";
            return prefix + (t / Balance.MinutesPerDay).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " days";
        }

        public string RewardText(CouncilRequest r)
        {
            var parts = new List<string>();
            if (r.reward > 0f) parts.Add("grant " + Fmt.Money(r.reward));
            parts.Add($"approval +{r.approvalReward}");
            string good = string.Join(", ", parts);
            string bad = $"approval −{r.approvalPenalty}" + (r.fine > 0f ? ", fine " + Fmt.Money(r.fine) : "");
            return $"Success: {good}. Failure: {bad}.";
        }

        public int DaysToSession()
        {
            int d = gs.clock.Day;
            int next = d % 7 == 0 && Mathf.RoundToInt(gs.clock.MinuteOfDay) < Balance.CouncilSessionMinute ? d : (d / 7 + 1) * 7;
            return next - d;
        }

        static readonly string[] FirstNames =
        {
            "Rosa", "Amir", "Helen", "Kenji", "Marta", "Tobias", "Ines", "Samuel", "Priya", "Olu", "Freya", "Dmitri",
            "Lucia", "Hamza", "Greta", "Mateo", "Ayasha", "Bruno", "Mei", "Patrick", "Zanele", "Henrik", "Leila", "Victor"
        };

        static readonly string[] LastNames =
        {
            "Alvarez", "Okafor", "Lindqvist", "Tanaka", "Brennan", "Kowalski", "Haddad", "Moreau", "Nakamura", "Adeyemi",
            "Fischer", "Costa", "Novak", "Rahman", "Walsh", "Ivanova", "Bianchi", "Mensah", "Sato", "Delgado", "Byrne", "Holm"
        };

        static readonly string[] Saints = { "Mary", "Luke", "Anne", "George", "Catherine", "Thomas", "Brigid" };
    }
}
