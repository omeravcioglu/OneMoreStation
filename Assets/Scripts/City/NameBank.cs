using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    /// <summary>Generates plausible district, station and city names.</summary>
    public static class NameBank
    {
        public static readonly string[] CityNames =
        {
            "Port Avalon", "New Haverly", "Kestrel Bay", "Vellmouth", "Ashport", "Northam", "Lindmere", "Caldera",
            "Riverton", "Marlow Bay", "Westhaven", "Brightwater", "Sundale", "Corvin", "Halbrook", "Merrin"
        };

        static readonly string[] Pre =
        {
            "Ash", "Oak", "Elm", "Mill", "Kings", "Queens", "Stone", "Fair", "Green", "Red", "Black", "White", "High",
            "Marsh", "Cran", "Bel", "Hazel", "Rose", "Cedar", "Maple", "Silver", "Bright", "Wind", "Clay", "Fern",
            "Thorn", "Willow", "Pine", "Bram", "Dun", "Lin", "Ken", "Hart", "Holly", "Lark", "Swan", "Camber", "Ever",
            "Wex", "Harrow", "Mor", "Ald", "Brack", "Chal", "Pen", "Sel", "Tal", "Wren", "Yar", "Ross", "Glen", "Nor"
        };

        static readonly string[] Suf =
        {
            "field", "gate", "wood", "ham", "ton", "bury", "stead", "worth", "dale", "mere", "wick", "view", "moor",
            "croft", "ley", "well", "cross", "heath", "vale", "row", "hurst", "minster", "end", "combe", "thorpe",
            "by", "green", "grove", "park", "hill"
        };

        static readonly string[] RiverSuf = { "ford", "bridge", "side", "brook", "bank", "lock", "weir" };
        static readonly string[] CoastSuf = { "haven", "port", "sands", "mouth", "cliff", "bay", "shore" };

        static readonly string[] IndustrialNames = { "Docklands", "Ironworks", "Foundry", "Canalside", "Gasworks", "Shipyards", "Steelworks" };
        static readonly string[] UniversityNames = { "University", "College Hill", "Campus" };
        static readonly string[] BusinessNames = { "Meridian Park", "Gateway", "Northpoint", "Sterling Park", "Commerce Park", "Tech Valley" };
        static readonly string[] StadiumNames = { "Stadium", "Arena Park", "Olympic Park" };

        static readonly string[] CentralStations =
        {
            "Central", "Market Square", "City Hall", "Cathedral", "Exchange", "Grand Avenue", "Union Square", "Museum",
            "Opera", "Parliament", "Bank", "Royal Arcade", "Civic Centre", "Courthouse", "Library", "Theatre Row"
        };

        static readonly string[] OldTownStations =
        {
            "Old Town", "Castle Hill", "Guild Street", "Clock Tower", "Old Market", "Abbey", "Town Gate", "Cloth Hall",
            "St Mary's", "Chapel Row", "Bell Lane", "Mint Street"
        };

        static readonly string[] Streets =
        {
            "Victoria", "Albert", "Station", "Church", "Mill", "High", "Bridge", "Market", "King", "Queen", "Elm", "Oak",
            "Garden", "Chapel", "Farm", "Orchard", "Canal", "Tower", "Abbey", "Hospital", "College", "School", "Lime",
            "Spring", "Union", "Manor", "Priory", "Baker", "Fisher", "Weaver", "Cooper", "Hatter", "Tanner", "Brewer",
            "Forge", "Granary", "Cherry", "Hawthorn", "Larch", "Poplar", "Beacon", "Crown", "Anchor", "Compass", "Lantern"
        };

        static readonly string[] StreetTypes = { "Street", "Road", "Square", "Lane", "Cross", "Avenue", "Gate", "Place" };

        public static string RandomCityName(Rng rng) => rng.Pick(CityNames);

        public static string DistrictName(Rng rng, HashSet<string> used, District d, CityModel city)
        {
            string[] special = null;
            switch (d.type)
            {
                case DistrictType.Central: return Claim(used, "Central");
                case DistrictType.OldTown: return Claim(used, "Old Town");
                case DistrictType.Airport: return Claim(used, "Airport");
                case DistrictType.Industrial: special = IndustrialNames; break;
                case DistrictType.University: special = UniversityNames; break;
                case DistrictType.BusinessPark: special = BusinessNames; break;
                case DistrictType.Stadium: special = StadiumNames; break;
            }
            if (special != null)
            {
                foreach (var s in special)
                    if (!used.Contains(s)) return Claim(used, s);
            }

            bool nearCoast = city.water.DistanceToCoast(d.center) < 2.2f;
            bool nearRiver = city.water.DistanceToRiver(d.center) < 1.4f;
            for (int attempt = 0; attempt < 200; attempt++)
            {
                string pre = rng.Pick(Pre);
                string suf;
                if (nearCoast && rng.Chance(0.6f)) suf = rng.Pick(CoastSuf);
                else if (nearRiver && rng.Chance(0.6f)) suf = rng.Pick(RiverSuf);
                else suf = rng.Pick(Suf);
                if (pre.ToLowerInvariant().EndsWith(suf.Substring(0, 1)) && suf.Length > 3) continue;
                string name = pre + suf;
                if (d.type == DistrictType.NewTown) name = "New " + name;
                if (!used.Contains(name)) return Claim(used, name);
            }
            return Claim(used, "District " + d.id);
        }

        static string Claim(HashSet<string> used, string name)
        {
            used.Add(name);
            return name;
        }

        public static string StationName(CityModel city, MetroNetwork net, Vector2 pos)
        {
            var used = new HashSet<string>();
            foreach (var s in net.AliveStations()) used.Add(s.name);
            var d = city.DistrictObjAt(pos);
            if (d == null) return Unique(used, "Station");

            var cands = new List<string>();
            switch (d.type)
            {
                case DistrictType.Central: cands.AddRange(CentralStations); break;
                case DistrictType.OldTown: cands.AddRange(OldTownStations); break;
                case DistrictType.University: cands.AddRange(new[] { "University", "Campus", "Science Park", "Student Quarter" }); break;
                case DistrictType.Airport: cands.AddRange(new[] { "Airport", "Airport Terminal 2", "Cargo Terminal", "Airport Parkway" }); break;
                case DistrictType.Industrial: cands.AddRange(new[] { d.name, "Foundry Lane", "Canal Basin", "Warehouse Row", "Gasworks", "Shipyard" }); break;
                case DistrictType.Stadium: cands.AddRange(new[] { "Stadium", "Arena", "Sports Park", d.name }); break;
                case DistrictType.BusinessPark: cands.AddRange(new[] { d.name, "Innovation Way", "Commerce Plaza", "Tech Park" }); break;
                default: cands.Add(d.name); break;
            }

            if (city.water.DistanceToCoast(pos) < 0.9f)
                cands.AddRange(new[] { "Harbour", d.name + " Harbour", "Seafront", "Pier", d.name + " Quay", "Lighthouse" });
            if (city.water.DistanceToRiver(pos) < 0.7f)
                cands.AddRange(new[] { d.name + " Bridge", "Riverside", d.name + " Wharf", "Embankment", "Ferry Lane" });
            foreach (var park in city.parks)
            {
                if (Vector2.Distance(Geo.Centroid(park), pos) < 1.3f)
                {
                    cands.AddRange(new[] { d.name + " Park", "Gardens", "Park Gate" });
                    break;
                }
            }

            Vector2 rel = pos - d.center;
            string dir = Mathf.Abs(rel.x) > Mathf.Abs(rel.y) ? (rel.x > 0 ? "East" : "West") : (rel.y > 0 ? "North" : "South");
            cands.Add(d.name + " " + dir);
            cands.Add(d.name + " Central");

            var rng = new Rng(Mathf.RoundToInt(pos.x * 997f) ^ Mathf.RoundToInt(pos.y * 7919f) ^ city.seed);
            var streets = new List<string>(Streets);
            rng.Shuffle(streets);
            foreach (var st in streets) cands.Add(st + " " + rng.Pick(StreetTypes));

            foreach (var c in cands)
                if (!used.Contains(c)) return c;
            return Unique(used, d.name);
        }

        static string Unique(HashSet<string> used, string baseName)
        {
            for (int i = 2; i < 999; i++)
            {
                string n = baseName + " " + i;
                if (!used.Contains(n)) return n;
            }
            return baseName;
        }
    }
}
