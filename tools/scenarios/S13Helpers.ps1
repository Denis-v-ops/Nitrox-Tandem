<#
    Movement measurement for scenario scripts (S13). Dot-source after ScenarioHelpers.ps1:
        . (Join-Path $PSScriptRoot 'S13Helpers.ps1')

    Start-Recording / Stop-Recording drive the games' "record" bridge command (NitroxClient/Debuggers/Bridge/BridgeRecorder.cs):
    per-frame samples (wall clock, position, rotation, local position relative to a second object, rigidbody velocity,
    replicator latency/buffer, pawn handle) plus state-change events. Both games run on this PC, so their tracks share a
    clock and can be compared sample by sample. The maths is in C# (Add-Type) because PowerShell loops are too slow for
    a few thousand samples.
#>

if (-not ('S13Track' -as [type])) {
    Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

public sealed class S13Event
{
    public double Ms;
    public int Frame;
    public string Key;
    public string Value;
    public override string ToString() { return Frame + " " + Key + "=" + Value; }
}

public sealed class S13Track
{
    public string Name;
    public string[] Fields;
    public double[][] Rows;
    public List<S13Event> Events = new List<S13Event>();
    /// <summary>Time column used for comparisons, in milliseconds.</summary>
    public string TimeField = "ms";

    public int Count { get { return Rows.Length; } }

    public int Col(string field)
    {
        int index = Array.IndexOf(Fields, field);
        if (index < 0) { throw new ArgumentException("No field " + field); }
        return index;
    }

    public double[] Column(string field)
    {
        int c = Col(field);
        return Rows.Select(r => r[c]).ToArray();
    }

    public double[] Times { get { return Column(TimeField); } }

    public static S13Track Parse(string json)
    {
        int pos = 0;
        Dictionary<string, object> root = (Dictionary<string, object>)ParseValue(json, ref pos);
        S13Track track = new S13Track();
        track.Name = (string)root["name"];
        track.Fields = ((List<object>)root["fields"]).Cast<string>().ToArray();
        track.Rows = ((List<object>)root["rows"]).Select(row => ((List<object>)row).Select(v => v == null ? double.NaN : (double)v).ToArray()).ToArray();
        foreach (object e in (List<object>)root["events"])
        {
            List<object> list = (List<object>)e;
            track.Events.Add(new S13Event { Ms = (double)list[0], Frame = (int)(double)list[1], Key = (string)list[2], Value = (string)list[3] });
        }
        return track;
    }

    private static object ParseValue(string s, ref int i)
    {
        SkipWs(s, ref i);
        char c = s[i];
        if (c == '{')
        {
            Dictionary<string, object> obj = new Dictionary<string, object>();
            i++;
            SkipWs(s, ref i);
            if (s[i] == '}') { i++; return obj; }
            while (true)
            {
                SkipWs(s, ref i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                i++; // :
                obj[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (s[i++] == '}') { return obj; }
            }
        }
        if (c == '[')
        {
            List<object> list = new List<object>();
            i++;
            SkipWs(s, ref i);
            if (s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (s[i++] == ']') { return list; }
            }
        }
        if (c == '"') { return ParseString(s, ref i); }
        if (s.Substring(i, Math.Min(4, s.Length - i)) == "null") { i += 4; return null; }
        if (s.Substring(i, Math.Min(4, s.Length - i)) == "true") { i += 4; return 1.0; }
        if (s.Substring(i, Math.Min(5, s.Length - i)) == "false") { i += 5; return 0.0; }
        int start = i;
        while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) { i++; }
        return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static string ParseString(string s, ref int i)
    {
        StringBuilder b = new StringBuilder();
        i++; // opening quote
        while (s[i] != '"')
        {
            if (s[i] == '\\') { i++; }
            b.Append(s[i++]);
        }
        i++;
        return b.ToString();
    }

    private static void SkipWs(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) { i++; }
    }
}

public static class S13Analysis
{
    private static bool At(double[] times, double[][] rows, int cx, double t, out double[] v)
    {
        v = null;
        int n = times.Length;
        if (n == 0 || t < times[0] || t > times[n - 1]) { return false; }
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (times[mid] <= t) { lo = mid; } else { hi = mid; }
        }
        double span = times[hi] - times[lo];
        double f = span > 0 ? (t - times[lo]) / span : 0;
        v = new double[3];
        for (int k = 0; k < 3; k++)
        {
            v[k] = rows[lo][cx + k] + (rows[hi][cx + k] - rows[lo][cx + k]) * f;
        }
        return !double.IsNaN(v[0]);
    }

    private static double Dist(double[] a, double[] b)
    {
        double dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static double[] Vec(double[] row, int c) { return new[] { row[c], row[c + 1], row[c + 2] }; }

    private static double Percentile(List<double> values, double p)
    {
        if (values.Count == 0) { return double.NaN; }
        List<double> sorted = values.OrderBy(x => x).ToList();
        return sorted[Math.Min(sorted.Count - 1, (int)Math.Floor(p * sorted.Count))];
    }

    private static double R(double value, int digits) { return Math.Round(value, digits); }

    /// <summary>
    ///     Frame-rate statistics of a track between two times (ms, the track's time field).
    /// </summary>
    public static Dictionary<string, object> Frames(S13Track track, double from, double to)
    {
        double[] t = track.Times;
        List<double> dts = new List<double>();
        for (int i = 1; i < t.Length; i++)
        {
            if (t[i] >= from && t[i] <= to) { dts.Add(t[i] - t[i - 1]); }
        }
        return new Dictionary<string, object>
        {
            { "frames", dts.Count },
            { "fps", dts.Count > 0 ? R(1000.0 / dts.Average(), 1) : 0 },
            { "dtP95Ms", R(Percentile(dts, 0.95), 1) },
            { "dtMaxMs", dts.Count > 0 ? R(dts.Max(), 1) : 0 }
        };
    }

    /// <summary>
    ///     Compares the observer's track of an object with the owner's (the game that simulates it), both world positions
    ///     ("px".."pz"), over [from, to] of the observer's clock. Lag = the time shift that best aligns the tracks (the
    ///     replication delay); residual = what remains after the shift (shape error: stutter, jumps, drift); per-frame
    ///     checks compare each observer frame's step with the owner's step over the same (shifted) interval.
    /// </summary>
    public static Dictionary<string, object> Compare(S13Track owner, S13Track observer, double from, double to, double maxLagMs)
    {
        double[] to_ = owner.Times;
        double[] tr = observer.Times;
        int ox = owner.Col("px");
        int rx = observer.Col("px");
        List<int> idx = new List<int>();
        for (int i = 0; i < tr.Length; i++)
        {
            if (tr[i] >= from && tr[i] <= to && !double.IsNaN(observer.Rows[i][rx])) { idx.Add(i); }
        }
        Dictionary<string, object> result = new Dictionary<string, object>();
        result["observerFrames"] = idx.Count;
        if (idx.Count < 10) { result["error"] = "too few observer samples"; return result; }

        double bestLag = double.NaN, bestRms = double.MaxValue;
        for (double lag = 0; lag <= maxLagMs; lag += 2)
        {
            double sum = 0;
            int n = 0;
            foreach (int i in idx)
            {
                double[] o;
                if (!At(to_, owner.Rows, ox, tr[i] - lag, out o)) { continue; }
                double d = Dist(Vec(observer.Rows[i], rx), o);
                sum += d * d;
                n++;
            }
            if (n < idx.Count / 2) { continue; }
            double rms = Math.Sqrt(sum / n);
            if (rms < bestRms) { bestRms = rms; bestLag = lag; }
        }
        result["lagMs"] = bestLag;

        List<double> residual = new List<double>();
        List<double> instant = new List<double>();
        int moving = 0, stalls = 0, jumps = 0, reversals = 0;
        double stepErrSq = 0, ownerPath = 0, ownerTime = 0, maxStepErr = 0;
        for (int k = 0; k < idx.Count; k++)
        {
            int i = idx[k];
            double[] r = Vec(observer.Rows[i], rx);
            double[] o0;
            if (At(to_, owner.Rows, ox, tr[i], out o0)) { instant.Add(Dist(r, o0)); }
            double[] o;
            if (double.IsNaN(bestLag) || !At(to_, owner.Rows, ox, tr[i] - bestLag, out o)) { continue; }
            residual.Add(Dist(r, o));
            if (k == 0 || idx[k - 1] != i - 1) { continue; }
            double[] rPrev = Vec(observer.Rows[i - 1], rx);
            double[] oPrev;
            if (!At(to_, owner.Rows, ox, tr[i - 1] - bestLag, out oPrev)) { continue; }
            double step = Dist(r, rPrev);
            double expected = Dist(o, oPrev);
            ownerPath += expected;
            ownerTime += tr[i] - tr[i - 1];
            if (expected < 0.005) { continue; }
            moving++;
            double err = step - expected;
            stepErrSq += err * err;
            maxStepErr = Math.Max(maxStepErr, Math.Abs(err));
            if (step < 0.25 * expected) { stalls++; }
            if (step > 2 * expected + 0.01) { jumps++; }
            double dot = (r[0] - rPrev[0]) * (o[0] - oPrev[0]) + (r[1] - rPrev[1]) * (o[1] - oPrev[1]) + (r[2] - rPrev[2]) * (o[2] - oPrev[2]);
            if (dot < 0) { reversals++; }
        }
        result["ownerSpeed"] = ownerTime > 0 ? R(ownerPath / ownerTime * 1000.0, 2) : 0;
        result["residualRms"] = residual.Count > 0 ? R(Math.Sqrt(residual.Select(x => x * x).Average()), 3) : double.NaN;
        result["residualP95"] = R(Percentile(residual, 0.95), 3);
        result["residualMax"] = residual.Count > 0 ? R(residual.Max(), 3) : double.NaN;
        result["instantMean"] = instant.Count > 0 ? R(instant.Average(), 3) : double.NaN;
        result["instantMax"] = instant.Count > 0 ? R(instant.Max(), 3) : double.NaN;
        result["movingFrames"] = moving;
        result["stallFrames"] = stalls;
        result["jumpFrames"] = jumps;
        result["reverseFrames"] = reversals;
        result["stepErrRms"] = moving > 0 ? R(Math.Sqrt(stepErrSq / moving), 4) : 0;
        result["stepErrMax"] = R(maxStepErr, 4);
        return result;
    }

    /// <summary>
    ///     Smoothness of one track on its own: per-frame step compared with the mean of its neighbours' steps
    ///     (a constant-speed path has equal steps per unit time). For the owner's own rendering as a baseline.
    /// </summary>
    public static Dictionary<string, object> Smoothness(S13Track track, string field, double from, double to)
    {
        double[] t = track.Times;
        int c = track.Col(field);
        List<double> speeds = new List<double>();
        List<int> rows = new List<int>();
        for (int i = 1; i < t.Length; i++)
        {
            if (t[i] < from || t[i] > to || t[i] <= t[i - 1] || double.IsNaN(track.Rows[i][c]) || double.IsNaN(track.Rows[i - 1][c])) { continue; }
            speeds.Add(Dist(Vec(track.Rows[i], c), Vec(track.Rows[i - 1], c)) / (t[i] - t[i - 1]) * 1000.0);
            rows.Add(i);
        }
        int stalls = 0, jumps = 0, moving = 0;
        List<double> rel = new List<double>();
        for (int k = 2; k < speeds.Count - 2; k++)
        {
            double around = (speeds[k - 2] + speeds[k - 1] + speeds[k + 1] + speeds[k + 2]) / 4;
            if (around < 0.2) { continue; }
            moving++;
            rel.Add(Math.Abs(speeds[k] - around) / around);
            if (speeds[k] < 0.25 * around) { stalls++; }
            if (speeds[k] > 2 * around) { jumps++; }
        }
        return new Dictionary<string, object>
        {
            { "movingFrames", moving },
            { "meanSpeed", speeds.Count > 0 ? R(speeds.Average(), 2) : 0 },
            { "speedDevRelP95", R(Percentile(rel, 0.95), 3) },
            { "stallFrames", stalls },
            { "jumpFrames", jumps }
        };
    }

    /// <summary>
    ///     How far a vector column (e.g. "lx" = local position in the sub) strays from its first value, and its range per axis.
    /// </summary>
    public static Dictionary<string, object> Drift(S13Track track, string field, double from, double to)
    {
        double[] t = track.Times;
        int c = track.Col(field);
        double[] first = null;
        double max = 0;
        double[] min = { double.MaxValue, double.MaxValue, double.MaxValue };
        double[] maxv = { double.MinValue, double.MinValue, double.MinValue };
        int n = 0;
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] < from || t[i] > to || double.IsNaN(track.Rows[i][c])) { continue; }
            double[] v = Vec(track.Rows[i], c);
            if (first == null) { first = v; }
            max = Math.Max(max, Dist(v, first));
            for (int k = 0; k < 3; k++) { min[k] = Math.Min(min[k], v[k]); maxv[k] = Math.Max(maxv[k], v[k]); }
            n++;
        }
        Dictionary<string, object> result = new Dictionary<string, object> { { "samples", n } };
        if (n == 0) { return result; }
        result["first"] = first.Select(x => R(x, 3)).ToArray();
        result["maxDistFromFirst"] = R(max, 3);
        result["min"] = min.Select(x => R(x, 3)).ToArray();
        result["max"] = maxv.Select(x => R(x, 3)).ToArray();
        return result;
    }

    /// <summary>
    ///     Distance between two tracks' vector columns at the same time (e.g. a passenger's local position in the sub as
    ///     their own game has it vs. as the partner's game shows their body), after the best time shift.
    /// </summary>
    public static Dictionary<string, object> CompareField(S13Track a, string fieldA, S13Track b, string fieldB, double from, double to, double maxLagMs)
    {
        double[] ta = a.Times;
        double[] tb = b.Times;
        int ca = a.Col(fieldA);
        int cb = b.Col(fieldB);
        List<int> idx = new List<int>();
        for (int i = 0; i < tb.Length; i++)
        {
            if (tb[i] >= from && tb[i] <= to && !double.IsNaN(b.Rows[i][cb])) { idx.Add(i); }
        }
        Dictionary<string, object> result = new Dictionary<string, object> { { "samples", idx.Count } };
        if (idx.Count < 5) { return result; }
        double bestLag = 0, bestRms = double.MaxValue;
        for (double lag = 0; lag <= maxLagMs; lag += 5)
        {
            double sum = 0;
            int n = 0;
            foreach (int i in idx)
            {
                double[] v;
                if (!At(ta, a.Rows, ca, tb[i] - lag, out v)) { continue; }
                double d = Dist(Vec(b.Rows[i], cb), v);
                sum += d * d;
                n++;
            }
            if (n < idx.Count / 2) { continue; }
            double rms = Math.Sqrt(sum / n);
            if (rms < bestRms) { bestRms = rms; bestLag = lag; }
        }
        List<double> err = new List<double>();
        foreach (int i in idx)
        {
            double[] v;
            if (At(ta, a.Rows, ca, tb[i] - bestLag, out v)) { err.Add(Dist(Vec(b.Rows[i], cb), v)); }
        }
        result["lagMs"] = bestLag;
        result["rms"] = err.Count > 0 ? R(Math.Sqrt(err.Select(x => x * x).Average()), 3) : double.NaN;
        result["p95"] = R(Percentile(err, 0.95), 3);
        result["max"] = err.Count > 0 ? R(err.Max(), 3) : double.NaN;
        return result;
    }

    /// <summary>
    ///     Min/mean/max of a scalar column, or of a vector column's length when vector is true.
    /// </summary>
    public static Dictionary<string, object> Stats(S13Track track, string field, double from, double to, bool vector)
    {
        double[] t = track.Times;
        int c = track.Col(field);
        List<double> values = new List<double>();
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] < from || t[i] > to || double.IsNaN(track.Rows[i][c])) { continue; }
            double[] row = track.Rows[i];
            values.Add(vector ? Math.Sqrt(row[c] * row[c] + row[c + 1] * row[c + 1] + row[c + 2] * row[c + 2]) : row[c]);
        }
        Dictionary<string, object> result = new Dictionary<string, object> { { "samples", values.Count } };
        if (values.Count == 0) { return result; }
        result["min"] = R(values.Min(), 4);
        result["mean"] = R(values.Average(), 4);
        result["max"] = R(values.Max(), 4);
        return result;
    }

    /// <summary>
    ///     Clock check: the smallest positive step of the wall-clock column (its resolution) and the median offset between
    ///     the wall clock and the performance counter (equal across games when both clocks are shared by the processes).
    /// </summary>
    public static Dictionary<string, object> Clock(S13Track track)
    {
        double[] ms = track.Column("ms");
        double[] qpc = track.Column("qpc");
        double[] rt = track.Column("rt");
        List<double> steps = new List<double>();
        List<double> rtSteps = new List<double>();
        int sameMs = 0, sameRt = 0;
        for (int i = 1; i < ms.Length; i++)
        {
            double d = ms[i] - ms[i - 1];
            if (d > 0) { steps.Add(d); } else { sameMs++; }
            double r = (rt[i] - rt[i - 1]) * 1000.0;
            if (r > 0) { rtSteps.Add(r); } else { sameRt++; }
        }
        List<double> offsets = ms.Zip(qpc, (a, b) => a - b).ToList();
        return new Dictionary<string, object>
        {
            { "msMinStep", steps.Count > 0 ? R(steps.Min(), 3) : 0 },
            { "framesWithSameMs", sameMs },
            { "rtMinStepMs", rtSteps.Count > 0 ? R(rtSteps.Min(), 3) : 0 },
            { "framesWithSameRt", sameRt },
            { "msMinusQpcMedian", R(Percentile(offsets, 0.5), 1) },
            { "msMinusQpcSpread", offsets.Count > 0 ? R(offsets.Max() - offsets.Min(), 1) : 0 },
            { "frames", ms.Length }
        };
    }
}
'@
}

function Start-Recording {
    param([Parameter(Mandatory)][string]$Player, [Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Target, [string]$RelativeTo)
    $command = "record start $Name $Target"
    if ($RelativeTo) { $command += " $RelativeTo" }
    Invoke-Bridge $Player $command | Out-Null
}

function Stop-Recording {
    param([Parameter(Mandatory)][string]$Player, [Parameter(Mandatory)][string]$Name, [string]$SaveTo)
    $json = Invoke-Bridge $Player "record stop $Name" -TimeoutSeconds 120
    if ($SaveTo) { Set-Content -LiteralPath $SaveTo -Value $json -Encoding utf8 }
    [S13Track]::Parse($json)
}

function Format-Result {
    param($Result)
    ($Result.GetEnumerator() | ForEach-Object {
        $value = if ($_.Value -is [array]) { '(' + (($_.Value | ForEach-Object { ([double]$_).ToString('0.###', [Globalization.CultureInfo]::InvariantCulture) }) -join ' ') + ')' }
                 elseif ($_.Value -is [double]) { ([double]$_.Value).ToString('0.####', [Globalization.CultureInfo]::InvariantCulture) }
                 else { "$($_.Value)" }
        "$($_.Key)=$value"
    }) -join ' '
}

function Show-Events {
    param([S13Track]$Track, [string[]]$Keys)
    foreach ($e in $Track.Events) {
        if (-not $Keys -or $Keys -contains $e.Key) { Write-Host "    [$($Track.Name)] frame $($e.Frame) $($e.Key) = $($e.Value)" }
    }
}
