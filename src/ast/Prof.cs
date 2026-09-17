using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

/// <summary>
/// Opt-in phase timer for finding where engine time goes: `using (Prof.Time("minify.rename")) { ... }`.
/// Off by default (a disabled Time() costs a field read); the harness's `bench --phases` switches it on and prints
/// the totals. Nested phases are counted in both (a phase's time includes its children).
/// </summary>
public static class Prof
{
    public static bool Enabled;
    static readonly Dictionary<string, long> Ticks = new Dictionary<string, long>();
    static readonly Dictionary<string, int> Calls = new Dictionary<string, int>();
    static readonly Stopwatch Clock = Stopwatch.StartNew();

    sealed class Scope : IDisposable
    {
        readonly string _name;
        readonly long _start;
        public Scope(string name) { _name = name; _start = Clock.ElapsedTicks; }
        public void Dispose()
        {
            long d = Clock.ElapsedTicks - _start;
            lock (Ticks)
            {
                long t; Ticks.TryGetValue(_name, out t); Ticks[_name] = t + d;
                int c; Calls.TryGetValue(_name, out c); Calls[_name] = c + 1;
            }
        }
    }

    sealed class Nothing : IDisposable { public void Dispose() { } }
    static readonly Nothing None = new Nothing();

    public static IDisposable Time(string name)
    {
        return Enabled ? (IDisposable)new Scope(name) : None;
    }

    public static void Reset()
    {
        lock (Ticks) { Ticks.Clear(); Calls.Clear(); }
    }

    /// <summary>(name, milliseconds, calls), largest first.</summary>
    public static List<Tuple<string, double, int>> Report()
    {
        lock (Ticks)
            return Ticks.OrderByDescending(k => k.Value)
                .Select(k => Tuple.Create(k.Key, k.Value * 1000.0 / Stopwatch.Frequency, Calls[k.Key])).ToList();
    }
}
