using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Metro
{
    [Serializable] public sealed class StationSave
    {
        public int id, district, platformLevel, capacityLevel;
        public string name;
        public float x, y, builtDay;
        public bool elevators, concourse, alive;
    }

    [Serializable] public sealed class LineSave
    {
        public int id, colorIndex, wagons;
        public string name;
        public bool loop, alive;
        public float builtDay;
        public int[] stops;
    }

    [Serializable] public sealed class DepotSave
    {
        public int id, stationId, level;
        public bool alive;
        public float builtDay, dirX, dirY;
    }

    [Serializable] public sealed class BendSave
    {
        public long key;
        public bool diag;
    }

    [Serializable] public sealed class TrainSave
    {
        public int lineId, stopIndex, dir;
    }

    [Serializable] public sealed class LedgerSave
    {
        public int day;
        public float riders;
        public float[] amounts;
    }

    [Serializable] public sealed class ObjectiveSave
    {
        public int kind, districtId;
        public string title, detail;
        public float target, reward, penalty, progress;
        public double deadline;
        public bool done, failed;
    }

    [Serializable] public sealed class RequestSave
    {
        public int id, kind, state, districtId, stationId, landmarkId, approvalReward, approvalPenalty;
        public bool mandate, paid;
        public string title, letter, status;
        public float target, reward, fine, progress, wanted, served, paidAmount;
        public double issuedAt, answerBy, deadline, resolvedAt, eventStart, eventEnd, holdSince;
    }

    [Serializable] public sealed class LandmarkSave
    {
        public int id, kind, districtId;
        public string name;
        public float x, y;
        public bool built;
    }

    [Serializable] public sealed class MayorSave
    {
        public string name;
        public int agenda, face;
    }

    [Serializable] public sealed class GovernmentSave
    {
        public bool present;            // false when loading saves from before City Hall existed
        public bool started, mandatesEnabled, ultimatum, gameOver, hasChallenger;
        public float approval, approvalAtLastSession;
        public MayorSave mayor = new MayorSave(), challenger = new MayorSave();
        public int nextElectionDay, endorsed, lastSessionDay, nextId, nextLandmarkId, completed, failed, declined, bailouts;
        public int weekDone, weekFailed, weekDeclined;
        public double nextProposalAt;
        public string gameOverReason;
        public List<RequestSave> open = new List<RequestSave>();
        public List<RequestSave> history = new List<RequestSave>();
        public List<LandmarkSave> landmarks = new List<LandmarkSave>();
        public float[] approvalHistory;
    }

    [Serializable] public sealed class DayStatsSave
    {
        public int day, stations, lines, trains;
        public float riders, delivered, abandoned, turnedAway, denied, transfers, income, expenses, net, satisfaction, population, coverage;
        public float[] hourly;
    }

    [Serializable] public sealed class MessageSave
    {
        public double time;
        public int category, severity, stationId, lineId, districtId;
        public string text;
        public bool read;
    }

    [Serializable] public sealed class SaveData
    {
        public int version = 3;
        public string savedAt, city;
        public int seed, difficulty;
        public double totalMinutes;
        public float cityGrowth;
        public bool[] revealed;
        public float[] revealedAt, maturity;
        public double nextRevealAt;
        public bool revealsPaused;
        public int tier = -1;
        public bool earnedSizes;
        public bool[] goalsDone;
        public int connectDistrict = -1;
        public float[] area;
        public float[] res, jobs, att, resBase, jobBase, attBase;
        public List<StationSave> stations = new List<StationSave>();
        public List<LineSave> lines = new List<LineSave>();
        public List<DepotSave> depots = new List<DepotSave>();
        public List<BendSave> bends = new List<BendSave>();
        public List<TrainSave> trains = new List<TrainSave>();
        public List<PencilPlan> plans = new List<PencilPlan>();
        public double money;
        public float baseFare, peakSurcharge, loan;
        public int adLevel, daysInRed;
        public float[] todayLedger;
        public List<LedgerSave> ledgers = new List<LedgerSave>();
        public float satisfaction;
        public bool objectivesStarted;                                    // version 2: City Hall targets
        public List<ObjectiveSave> objectives = new List<ObjectiveSave>();
        public GovernmentSave government = new GovernmentSave();
        public List<DayStatsSave> days = new List<DayStatsSave>();
        public float recordRiders;
        public float[] yesterdayHourly;
        public List<MessageSave> messages = new List<MessageSave>();
    }

    [Serializable] public sealed class SlotMeta
    {
        public string city, savedAt, size;
        public int day;
        public double money;
    }

    public struct SlotInfo
    {
        public bool exists;
        public string city, savedAt, size;
        public int day;
        public double money;
    }

    /// <summary>JSON save slots under persistentDataPath/saves. The city itself is regenerated from its seed.</summary>
    public static class SaveSystem
    {
        public const string AutosaveSlot = "autosave";
        public static readonly string[] Slots = { AutosaveSlot, "slot1", "slot2", "slot3" };

        // Scripted test runs (-autotest) keep their saves apart from the player's.
        static string Dir => Path.Combine(Application.persistentDataPath, Metro.Platform.PlatformServices.HasArg("-autotest") ? "saves_test" : "saves");
        public static string PathFor(string slot) => Path.Combine(Dir, slot + ".json");
        public static string MetaFor(string slot) => Path.Combine(Dir, slot + ".meta.json");

        public static string SlotLabel(string slot) => slot == AutosaveSlot ? "Autosave" : "Slot " + slot.Substring(4);

        public static SlotInfo Info(string slot)
        {
            try
            {
                if (!File.Exists(MetaFor(slot)) || !File.Exists(PathFor(slot))) return new SlotInfo();
                var m = JsonUtility.FromJson<SlotMeta>(File.ReadAllText(MetaFor(slot)));
                return new SlotInfo { exists = true, city = m.city, day = m.day, money = m.money, savedAt = m.savedAt, size = m.size };
            }
            catch
            {
                return new SlotInfo();
            }
        }

        public static bool Save(GameState gs, string slot)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var data = Capture(gs);
                // Crash-safe: written to a temp file first, the previous save is kept as .bak.
                Metro.Platform.SafeFile.WriteAllText(PathFor(slot), JsonUtility.ToJson(data));
                var meta = new SlotMeta { city = data.city, day = gs.clock.DayNumber, money = gs.econ.money, savedAt = data.savedAt, size = gs.progress.Current.name };
                Metro.Platform.SafeFile.WriteAllText(MetaFor(slot), JsonUtility.ToJson(meta));
                Metro.Platform.CloudSync.PushSlot(slot);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[Metro] Save failed: " + e);
                return false;
            }
        }

        public static GameState Load(string slot)
        {
            string path = PathFor(slot);
            foreach (var candidate in new[] { path, path + ".bak" })
            {
                try
                {
                    if (!File.Exists(candidate)) continue;
                    var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(candidate));
                    if (data == null || data.stations == null) throw new InvalidDataException("not a save file");
                    var gs = Restore(data);
                    if (candidate != path) Debug.LogWarning("[Metro] " + slot + " was damaged; loaded the previous version instead.");
                    return gs;
                }
                catch (Exception e)
                {
                    Debug.LogError("[Metro] Load of " + candidate + " failed: " + e);
                }
            }
            return null;
        }

        public static void Delete(string slot)
        {
            foreach (var f in new[] { PathFor(slot), MetaFor(slot), PathFor(slot) + ".bak", MetaFor(slot) + ".bak" })
            {
                try
                {
                    if (File.Exists(f)) File.Delete(f);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Metro] Could not delete " + f + ": " + e.Message);
                }
            }
            Metro.Platform.CloudSync.DeleteSlot(slot);
        }

        public static SaveData Capture(GameState gs)
        {
            var city = gs.city;
            var d = new SaveData
            {
                savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                city = city.name,
                seed = gs.options.seed,
                difficulty = gs.options.difficulty,
                totalMinutes = gs.clock.totalMinutes,
                cityGrowth = city.cityGrowth,
                revealed = new bool[city.districts.Count],
                revealedAt = new float[city.districts.Count],
                maturity = new float[city.districts.Count],
                nextRevealAt = gs.growth.nextRevealAt,
                revealsPaused = gs.growth.revealsPaused,
                tier = gs.progress.tier,
                earnedSizes = gs.options.earnedSizes,
                goalsDone = new bool[gs.progress.goals.Count],
                area = new[] { city.playArea.x, city.playArea.y, city.playArea.width, city.playArea.height },
                res = city.res,
                jobs = city.jobs,
                att = city.att,
                resBase = city.resBase,
                jobBase = city.jobBase,
                attBase = city.attBase,
                money = gs.econ.money,
                baseFare = gs.econ.baseFare,
                peakSurcharge = gs.econ.peakSurcharge,
                loan = gs.econ.loan,
                adLevel = gs.econ.adLevel,
                daysInRed = gs.econ.daysInRed,
                todayLedger = gs.econ.today.amounts,
                satisfaction = gs.satisfaction.score,
                government = CaptureGovernment(gs.gov),
                recordRiders = gs.stats.recordRiders,
                yesterdayHourly = gs.stats.yesterdayHourRiders
            };
            for (int i = 0; i < city.districts.Count; i++)
            {
                d.revealed[i] = city.districts[i].revealed;
                d.revealedAt[i] = city.districts[i].revealedAtDay;
                d.maturity[i] = city.districts[i].maturity;
            }
            for (int i = 0; i < gs.progress.goals.Count; i++)
            {
                var goal = gs.progress.goals[i];
                d.goalsDone[i] = goal.done;
                if (goal.kind == GoalKind.ConnectDistrict) d.connectDistrict = goal.districtId;
            }
            foreach (var s in gs.net.stations)
            {
                d.stations.Add(new StationSave
                {
                    id = s.id, name = s.name, x = s.pos.x, y = s.pos.y, district = s.district, platformLevel = s.platformLevel,
                    capacityLevel = s.capacityLevel, elevators = s.elevators, concourse = s.concourse, alive = s.alive, builtDay = s.builtDay
                });
            }
            foreach (var l in gs.net.lines)
            {
                d.lines.Add(new LineSave
                {
                    id = l.id, name = l.name, colorIndex = l.colorIndex, wagons = l.wagons, loop = l.loop, alive = l.alive,
                    builtDay = l.builtDay, stops = l.stops.ToArray()
                });
            }
            foreach (var dep in gs.net.depots)
            {
                d.depots.Add(new DepotSave
                {
                    id = dep.id, stationId = dep.stationId, level = dep.level, alive = dep.alive, builtDay = dep.builtDay,
                    dirX = dep.iconDir.x, dirY = dep.iconDir.y
                });
            }
            foreach (var kv in gs.net.bendPrefs) d.bends.Add(new BendSave { key = kv.Key, diag = kv.Value });
            foreach (var plan in gs.plans)
                if (plan != null) d.plans.Add(plan.Copy());
            foreach (var t in gs.trains.trains)
                if (t.alive) d.trains.Add(new TrainSave { lineId = t.lineId, stopIndex = t.stopIndex, dir = t.dir });
            foreach (var led in gs.econ.history) d.ledgers.Add(new LedgerSave { day = led.day, riders = led.riders, amounts = led.amounts });
            foreach (var ds in gs.stats.days)
            {
                d.days.Add(new DayStatsSave
                {
                    day = ds.day, stations = ds.stations, lines = ds.lines, trains = ds.trains, riders = ds.riders, delivered = ds.delivered,
                    abandoned = ds.abandoned, turnedAway = ds.turnedAway, denied = ds.denied, transfers = ds.transfers, income = ds.income,
                    expenses = ds.expenses, net = ds.net, satisfaction = ds.satisfaction, population = ds.population, coverage = ds.coverage,
                    hourly = ds.hourly
                });
            }
            int from = Mathf.Max(0, gs.advisor.messages.Count - 40);
            for (int i = from; i < gs.advisor.messages.Count; i++)
            {
                var m = gs.advisor.messages[i];
                d.messages.Add(new MessageSave
                {
                    time = m.time, category = (int)m.category, severity = (int)m.severity, text = m.text, stationId = m.stationId,
                    lineId = m.lineId, districtId = m.districtId, read = m.read
                });
            }
            return d;
        }

        public static GameState Restore(SaveData d)
        {
            int maxTier = ProgressionSystem.Tiers.Length - 1;
            int tier = d.tier >= 0 ? Mathf.Min(d.tier, maxTier) : maxTier;   // saves from before city sizes: fully grown
            var opts = new NewGameOptions { seed = d.seed, cityName = d.city, difficulty = d.difficulty, startTier = tier, earnedSizes = d.earnedSizes };
            var gs = new GameState(opts);
            var city = gs.city;
            gs.clock.totalMinutes = d.totalMinutes;
            city.cityGrowth = d.cityGrowth;
            if (d.area != null && d.area.Length == 4) city.playArea = new Rect(d.area[0], d.area[1], d.area[2], d.area[3]);
            for (int i = 0; i < city.districts.Count && i < d.revealed.Length; i++)
            {
                var dist = city.districts[i];
                dist.revealed = d.revealed[i];
                dist.revealedAtDay = d.revealedAt[i];
                dist.maturity = d.maturity[i];
                dist.revealAnim = dist.revealed ? 1f : 0f;
            }
            if (d.res != null && d.res.Length == city.CellCount)
            {
                city.res = d.res;
                city.jobs = d.jobs;
                city.att = d.att;
                city.resBase = d.resBase;
                city.jobBase = d.jobBase;
                city.attBase = d.attBase;
            }
            else CityGenerator.InitialPopulation(city);
            gs.growth.nextRevealAt = d.nextRevealAt;
            gs.growth.revealsPaused = d.revealsPaused && d.tier >= 0;

            var net = gs.net;
            foreach (var s in d.stations)
            {
                net.stations.Add(new Station
                {
                    id = s.id, name = s.name, pos = new Vector2(s.x, s.y), district = s.district, platformLevel = s.platformLevel,
                    capacityLevel = s.capacityLevel, elevators = s.elevators, concourse = s.concourse, alive = s.alive, builtDay = s.builtDay
                });
            }
            foreach (var l in d.lines)
            {
                var line = new MetroLine
                {
                    id = l.id, name = l.name, colorIndex = l.colorIndex, wagons = l.wagons, loop = l.loop, alive = l.alive, builtDay = l.builtDay
                };
                if (l.stops != null) line.stops.AddRange(l.stops);
                net.lines.Add(line);
            }
            foreach (var dep in d.depots)
            {
                net.depots.Add(new Depot
                {
                    id = dep.id, stationId = dep.stationId, level = dep.level, alive = dep.alive, builtDay = dep.builtDay,
                    iconDir = new Vector2(dep.dirX, dep.dirY)
                });
            }
            foreach (var b in d.bends) net.bendPrefs[b.key] = b.diag;
            // Older saves have no pencil plans.
            if (d.plans != null)
                foreach (var plan in d.plans)
                    if (plan != null && plan.points != null && plan.points.Count > 0) gs.plans.Add(plan.Copy());
            net.Rebuild();
            foreach (var t in d.trains)
            {
                var line = net.GetLine(t.lineId);
                if (line != null) gs.trains.RestoreTrain(line, t.stopIndex, t.dir);
            }

            var e = gs.econ;
            e.money = d.money;
            e.baseFare = d.baseFare;
            e.peakSurcharge = d.peakSurcharge;
            e.loan = d.loan;
            e.adLevel = Mathf.Clamp(d.adLevel, 0, 3);
            e.daysInRed = d.daysInRed;
            e.today = new DayLedger { day = gs.clock.Day };
            if (d.todayLedger != null && d.todayLedger.Length == DayLedger.Count) e.today.amounts = d.todayLedger;
            foreach (var l in d.ledgers) e.history.Add(new DayLedger { day = l.day, riders = l.riders, amounts = l.amounts ?? new float[DayLedger.Count] });

            gs.satisfaction.score = d.satisfaction;
            gs.satisfaction.yesterday = d.satisfaction;
            foreach (var ds in d.days)
            {
                gs.stats.days.Add(new DayStats
                {
                    day = ds.day, stations = ds.stations, lines = ds.lines, trains = ds.trains, riders = ds.riders, delivered = ds.delivered,
                    abandoned = ds.abandoned, turnedAway = ds.turnedAway, denied = ds.denied, transfers = ds.transfers, income = ds.income,
                    expenses = ds.expenses, net = ds.net, satisfaction = ds.satisfaction, population = ds.population, coverage = ds.coverage,
                    hourly = ds.hourly ?? new float[24]
                });
            }
            gs.stats.recordRiders = d.recordRiders;
            if (d.yesterdayHourly != null && d.yesterdayHourly.Length == 24) Array.Copy(d.yesterdayHourly, gs.stats.yesterdayHourRiders, 24);
            foreach (var m in d.messages)
            {
                gs.advisor.Restore(new AdvisorMessage
                {
                    time = m.time, category = (MsgCategory)m.category, severity = (MsgSeverity)m.severity, text = m.text,
                    stationId = m.stationId, lineId = m.lineId, districtId = m.districtId, read = m.read
                });
            }
            city.RefreshAggregates();
            gs.MarkNetworkChanged();
            gs.EnsureFresh();
            RestoreGovernment(gs, d);
            gs.progress.Restore(tier, d.goalsDone, d.connectDistrict);
            return gs;
        }

        // ------------------------------------------------------------------ City Hall

        static RequestSave SaveRequest(CouncilRequest r) => new RequestSave
        {
            id = r.id, kind = (int)r.kind, state = (int)r.state, districtId = r.districtId, stationId = r.stationId, landmarkId = r.landmarkId,
            approvalReward = r.approvalReward, approvalPenalty = r.approvalPenalty, mandate = r.mandate, paid = r.paid,
            title = r.title, letter = r.letter, status = r.status, target = r.target, reward = r.reward, fine = r.fine, progress = r.progress,
            wanted = r.wanted, served = r.served, paidAmount = r.paidAmount, issuedAt = r.issuedAt, answerBy = r.answerBy, deadline = r.deadline,
            resolvedAt = r.resolvedAt, eventStart = r.eventStart, eventEnd = r.eventEnd, holdSince = r.holdSince
        };

        static CouncilRequest LoadRequest(RequestSave r) => new CouncilRequest
        {
            id = r.id, kind = (RequestKind)r.kind, state = (RequestState)r.state, districtId = r.districtId, stationId = r.stationId,
            landmarkId = r.landmarkId, approvalReward = r.approvalReward, approvalPenalty = r.approvalPenalty, mandate = r.mandate,
            paid = r.paid, title = r.title ?? "", letter = r.letter ?? "", status = r.status ?? "", target = r.target, reward = r.reward,
            fine = r.fine, progress = r.progress, wanted = r.wanted, served = r.served, paidAmount = r.paidAmount, issuedAt = r.issuedAt, answerBy = r.answerBy,
            deadline = r.deadline, resolvedAt = r.resolvedAt, eventStart = r.eventStart, eventEnd = r.eventEnd, holdSince = r.holdSince
        };

        static MayorSave SaveMayor(Mayor m) => m == null ? new MayorSave() : new MayorSave { name = m.name, agenda = (int)m.agenda, face = m.face };

        static Mayor LoadMayor(MayorSave m) => new Mayor { name = string.IsNullOrEmpty(m.name) ? "Rosa Alvarez" : m.name, agenda = (Agenda)Mathf.Clamp(m.agenda, 0, 3), face = m.face };

        static GovernmentSave CaptureGovernment(GovernmentSystem g)
        {
            var s = new GovernmentSave
            {
                present = true, started = g.started, mandatesEnabled = g.mandatesEnabled, ultimatum = g.ultimatum, gameOver = g.gameOver,
                hasChallenger = g.challenger != null, approval = g.approval, approvalAtLastSession = g.approvalAtLastSession,
                mayor = SaveMayor(g.mayor), challenger = SaveMayor(g.challenger), nextElectionDay = g.nextElectionDay, endorsed = g.endorsed,
                lastSessionDay = g.lastSessionDay, nextId = g.nextId, nextLandmarkId = g.nextLandmarkId, completed = g.completed,
                failed = g.failed, declined = g.declined, bailouts = g.bailouts, weekDone = g.weekDone, weekFailed = g.weekFailed,
                weekDeclined = g.weekDeclined, nextProposalAt = g.nextProposalAt, gameOverReason = g.gameOverReason,
                approvalHistory = g.approvalHistory.ToArray()
            };
            foreach (var r in g.open) s.open.Add(SaveRequest(r));
            foreach (var r in g.history) s.history.Add(SaveRequest(r));
            foreach (var l in g.landmarks)
                s.landmarks.Add(new LandmarkSave { id = l.id, kind = (int)l.kind, districtId = l.districtId, name = l.name, x = l.pos.x, y = l.pos.y, built = l.built });
            return s;
        }

        static void RestoreGovernment(GameState gs, SaveData d)
        {
            var g = gs.gov;
            var s = d.government;
            if (s != null && s.present)
            {
                g.started = s.started;
                g.mandatesEnabled = s.mandatesEnabled;
                g.ultimatum = s.ultimatum;
                g.gameOver = s.gameOver;
                g.gameOverReason = s.gameOverReason ?? "";
                g.approval = s.approval;
                g.approvalAtLastSession = s.approvalAtLastSession;
                g.mayor = LoadMayor(s.mayor);
                g.challenger = s.hasChallenger ? LoadMayor(s.challenger) : null;
                g.nextElectionDay = s.nextElectionDay;
                g.endorsed = s.endorsed;
                g.lastSessionDay = s.lastSessionDay;
                g.nextId = s.nextId;
                g.nextLandmarkId = s.nextLandmarkId;
                g.completed = s.completed;
                g.failed = s.failed;
                g.declined = s.declined;
                g.bailouts = s.bailouts;
                g.weekDone = s.weekDone;
                g.weekFailed = s.weekFailed;
                g.weekDeclined = s.weekDeclined;
                g.nextProposalAt = s.nextProposalAt;
                g.approvalHistory.Clear();
                if (s.approvalHistory != null) g.approvalHistory.AddRange(s.approvalHistory);
                g.landmarks.Clear();
                foreach (var l in s.landmarks)
                    g.landmarks.Add(new Landmark { id = l.id, kind = (LandmarkKind)l.kind, districtId = l.districtId, name = l.name ?? "", pos = new Vector2(l.x, l.y), built = l.built });
                g.open.Clear();
                g.history.Clear();
                foreach (var r in s.open) g.RestoreRequest(LoadRequest(r), true);
                foreach (var r in s.history) g.RestoreRequest(LoadRequest(r), false);
                return;
            }
            // Version 2 saves: City Hall targets become accepted requests.
            foreach (var o in d.objectives)
            {
                var r = new CouncilRequest
                {
                    id = g.nextId++, state = RequestState.Active, districtId = o.districtId, title = o.title ?? "", letter = o.detail ?? "",
                    reward = o.reward, fine = o.penalty, deadline = o.deadline, issuedAt = gs.clock.totalMinutes, answerBy = gs.clock.totalMinutes,
                    approvalReward = 5, approvalPenalty = 5
                };
                switch (o.kind)
                {
                    case 1: r.kind = RequestKind.ConnectDistrict; r.target = o.target; break;
                    case 2: r.kind = RequestKind.Ridership; r.target = o.target; break;
                    case 3: r.kind = RequestKind.Satisfaction; r.target = o.target; break;
                    case 4: r.kind = RequestKind.ServedResidents; r.target = Mathf.Max(10_000f, o.target * gs.city.revealedPopulation); break;
                    default: continue;
                }
                g.RestoreRequest(r, true);
            }
        }
    }
}
