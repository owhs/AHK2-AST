using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AstHarness
{
    /// <summary>Proves the sandbox guarantees before anything relies on them.</summary>
    public static class SelfTest
    {
        static int _fail;

        static void Check(bool cond, string what, string detail)
        {
            Console.WriteLine((cond ? "  PASS  " : "  FAIL  ") + what + (cond || detail == null ? "" : "\n          " + detail));
            if (!cond) _fail++;
        }

        static string Describe(AhkOutcome o)
        {
            var parts = new List<string> { "status=" + o.Status, "exit=" + o.ExitCode, "ms=" + o.ElapsedMs };
            foreach (var e in o.Errors) parts.Add("E:" + e);
            foreach (var w in o.Warnings) parts.Add("W:" + w);
            foreach (var w in o.Windows) parts.Add("win:" + w.Kind + "/" + w.Class + " '" + Util.Clip(w.Text, 80) + "' buttons=[" + string.Join(",", w.Buttons) + "] action=" + w.Action);
            if (o.Stdout.Length > 0) parts.Add("stdout='" + Util.Clip(o.Stdout, 120) + "'");
            return string.Join(" | ", parts);
        }

        static Dictionary<int, string> AhkCommandLines()
        {
            var map = new Dictionary<int, string>();
            using (var q = new System.Management.ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name LIKE 'AutoHotkey%'"))
                foreach (System.Management.ManagementObject mo in q.Get())
                    map[Convert.ToInt32(mo["ProcessId"])] = Convert.ToString(mo["CommandLine"] ?? "");
            return map;
        }

        static List<int> UserAhkPids()
        {
            string harnessTemp = Paths.Temp;
            return AhkCommandLines().Where(kv => kv.Value.Length > 0 && kv.Value.IndexOf(harnessTemp, StringComparison.OrdinalIgnoreCase) < 0).Select(kv => kv.Key).ToList();
        }

        public static int Run(string workDir, bool verbose)
        {
            _fail = 0;
            string dir = Path.Combine(workDir, "selftest");
            Util.SafeDeleteTree(dir);
            Directory.CreateDirectory(dir);

            // Everything the user already has running must still be running afterwards.
            // (Harness-owned AHK copies live under %TEMP%\AstHarness, so other harness runs don't confuse this.)
            var before = UserAhkPids();
            Console.WriteLine("AutoHotkey: " + Ahk.Exe64 + " (" + Ahk.Version + ")");
            Console.WriteLine("Sandbox desktop: " + Sandbox.Instance.DesktopName + "; pre-existing AHK processes: " + before.Count);

            Func<string, string, string> write = (name, body) =>
            {
                string p = Path.Combine(dir, name);
                Util.WriteScript(p, "#Requires AutoHotkey v2.0\r\n" + body);
                return p;
            };

            // 1. clean validate
            var o = Ahk.Validate(Ahk.Exe64, write("ok.ahk", "x := 1\r\nMsgBox x"), 0, 15000);
            Check(o.Ok && o.Windows.Count == 0, "validate: clean script is ok, shows nothing", Describe(o));

            // 2. load error
            o = Ahk.Validate(Ahk.Exe64, write("bad.ahk", "x := (1\r\nMsgBox x"), 0, 15000);
            Check(o.Status == "load-error" && o.Errors.Count > 0 && o.Errors[0].Message.Contains("Missing"), "validate: syntax error is parsed from stderr", Describe(o));
            if (o.Errors.Count > 0) Check(o.Errors[0].Line == 2, "validate: error line number", Describe(o));

            // 3. #Warn MsgBox dialog appears on the hidden desktop, is read and continued
            string warnPath = write("warn.ahk", "#Warn\r\nf() {\r\n  y := 1\r\n  return z\r\n}\r\nf()");
            o = Ahk.Validate(Ahk.Exe64, warnPath, 0, 15000);
            Check(o.Warnings.Count > 0 && o.Windows.Any(w => w.Kind == "AhkWarning"), "validate: #Warn dialog captured on hidden desktop", Describe(o));
            if (verbose) foreach (var w in o.Windows) Console.WriteLine("          controls: " + string.Join(" ; ", w.Controls));

            // 4. same script, prepared: warnings come back as text, no dialog at all
            var prep = Ahk.Prepare(Util.ReadScript(warnPath), warnPath, true);
            string warnPrepared = Path.Combine(dir, "warn.prepared.ahk");
            Util.WriteScript(warnPrepared, prep.Text);
            o = Ahk.Validate(Ahk.Exe64, warnPrepared, prep.LineOffset, 15000);
            Check(o.Ok && o.Warnings.Count > 0 && o.Windows.Count == 0, "validate: forced '#Warn ..., StdOut' yields text warnings, no dialogs", Describe(o));
            if (o.Warnings.Count > 0) Check(o.Warnings[0].Line == 5, "validate: warning line mapped back through the injected line", Describe(o));

            // 5. runtime MsgBox is read + dismissed, script continues to completion
            o = Ahk.Execute(Ahk.Exe64, write("msgbox.ahk", "MsgBox 'hello from sandbox'\r\nFileAppend 'after', '*'\r\nExitApp 7"), 0, 5000, null);
            if (verbose) foreach (var w in o.Windows) Console.WriteLine("          controls: " + string.Join(" ; ", w.Controls));
            Check(o.Windows.Any(w => w.Kind == "MsgBox" && w.Text.Contains("hello from sandbox")) && o.Stdout.Contains("after") && o.ExitCode == 7,
                "exec: MsgBox text captured, dismissed, stdout + exit code captured", Describe(o));

            // 6. runtime error dialog detected, job killed, screenshot taken
            string shots = Path.Combine(dir, "shots");
            o = Ahk.Execute(Ahk.Exe64, write("rterr.ahk", "x := [1]\r\nMsgBox x[5]"), 0, 15000, shots);
            Check(o.Status == "runtime-error" && o.Errors.Count > 0 && o.Errors[0].Message.Contains("Invalid index"), "exec: runtime error dialog detected + parsed", Describe(o));
            Check(o.Windows.Any(w => w.Screenshot != null && File.Exists(w.Screenshot)), "exec: runtime error dialog screenshot", Describe(o));
            if (verbose) foreach (var w in o.Windows) Console.WriteLine("          controls: " + string.Join(" ; ", w.Controls));

            // 7. persistent GUI: captured with its controls, screenshotted, killed at the deadline
            o = Ahk.Execute(Ahk.Exe64, write("gui.ahk", "g := Gui(, 'Harness GUI')\r\ng.Add('Text',, 'Label one')\r\ng.Add('Edit', 'w200', 'edit text')\r\ng.Add('Button',, 'Press')\r\ng.Show()"), 0, 1500, shots);
            var gw = o.Windows.FirstOrDefault(w => w.Kind == "Window");
            Check(o.Alive && gw != null && gw.Title == "Harness GUI" && gw.Controls.Any(c => c.Contains("Label one")), "exec: GUI window + controls captured, process alive until deadline", Describe(o));
            Check(gw != null && gw.Screenshot != null && File.Exists(gw.Screenshot), "exec: GUI screenshot (PrintWindow on hidden desktop)", gw == null ? "no window" : "shot=" + gw.Screenshot);

            // 8. relocation: includes with spaces, sub-dirs, local Lib, %A_ScriptDir%, directory includes
            string proj = Path.Combine(dir, "proj with space");
            Util.WriteScript(Path.Combine(proj, @"sub dir\inc one.ahk"), "IncOne() => 1\r\n");
            Util.WriteScript(Path.Combine(proj, @"sub dir\inc two.ahk"), "IncTwo() => 2\r\n");
            Util.WriteScript(Path.Combine(proj, @"Lib\MyLib.ahk"), "MyLib() => 3\r\n");
            Util.WriteScript(Path.Combine(proj, "three.ahk"), "Three() => 3\r\n");
            string mainPath = Path.Combine(proj, "main.ahk");
            Util.WriteScript(mainPath, "#Requires AutoHotkey v2.0\r\n#Include sub dir\\inc one.ahk ; comment\r\n#Include \"%A_ScriptDir%\\three.ahk\"\r\n#Include <MyLib>\r\n#Include sub dir\\\r\n#Include inc two.ahk\r\n#Include *i missing.ahk\r\nx := IncOne() + IncTwo() + MyLib() + Three()\r\n");
            o = Ahk.Validate(Ahk.Exe64, mainPath, 0, 15000);
            Check(o.Ok, "relocation: original validates in place", Describe(o));
            prep = Ahk.Prepare(Util.ReadScript(mainPath), mainPath, true);
            string moved = Path.Combine(dir, @"elsewhere\main.ahk");
            Util.WriteScript(moved, prep.Text);
            o = Ahk.Validate(Ahk.Exe64, moved, prep.LineOffset, 15000);
            Check(o.Ok, "relocation: prepared copy validates from another folder", Describe(o) + "\n" + prep.Text);
            Check(prep.Includes.Count == 4, "relocation: include dependency list", string.Join(", ", prep.Includes));

            // 9. junction safety: cleanup must remove the link and never touch the target's files
            string target = Path.Combine(dir, "junction-target");
            Util.WriteScript(Path.Combine(target, "sentinel.ahk"), "x := 1\r\n");
            string holder = Path.Combine(dir, "holder");
            Directory.CreateDirectory(holder);
            bool made = Native.CreateJunction(Path.Combine(holder, "Lib"), target);
            bool resolves = File.Exists(Path.Combine(holder, @"Lib\sentinel.ahk"));
            Util.SafeDeleteTree(holder);
            Check(made && resolves && !Directory.Exists(holder) && File.Exists(Path.Combine(target, "sentinel.ahk")),
                "junction: created, resolves, SafeDeleteTree removes link but keeps target files",
                "made=" + made + " resolves=" + resolves + " holderGone=" + !Directory.Exists(holder) + " sentinelKept=" + File.Exists(Path.Combine(target, "sentinel.ahk")));

            // 10. relocation: <Lib> include inside an *included* file (resolved via A_ScriptDir\Lib junction)
            string proj2 = Path.Combine(dir, "proj2");
            Util.WriteScript(Path.Combine(proj2, @"Lib\Deep.ahk"), "Deep() => 4\r\n");
            Util.WriteScript(Path.Combine(proj2, @"src\mid.ahk"), "#Include <Deep>\r\nMid() => Deep()\r\n");
            string main2 = Path.Combine(proj2, "main.ahk");
            Util.WriteScript(main2, "#Requires AutoHotkey v2.0\r\n#Include src\\mid.ahk\r\nx := Mid()\r\n");
            prep = Ahk.Prepare(Util.ReadScript(main2), main2, true);
            string moved2 = Path.Combine(dir, @"elsewhere2\main.ahk");
            Ahk.WriteRelocated(moved2, prep, main2);
            o = Ahk.Validate(Ahk.Exe64, moved2, prep.LineOffset, 15000);
            Check(o.Ok, "relocation: nested <Lib> include resolves through the Lib junction", Describe(o));
            Util.SafeDeleteTree(Path.Combine(dir, "elsewhere2"));
            Check(File.Exists(Path.Combine(proj2, @"Lib\Deep.ahk")), "relocation: cleanup left the original Lib untouched", null);

            // 11. engine source ranges on the cases (no AutoHotkey involved): nested, on token boundaries, every node
            //     re-parses from its own text; no-op edits give the file back byte for byte
            string rangeDetail;
            Check(RangeCheck.SelfTest(out rangeDetail), "ranges: every node of harness\\cases has a correct source range", rangeDetail);

            // 12. isolation guarantees
            var after = UserAhkPids();
            var missing = before.Where(id => !after.Contains(id)).ToList();
            Check(missing.Count == 0, "isolation: every pre-existing (non-harness) AutoHotkey process is still running", "missing pids: " + string.Join(",", missing));
            var leftovers = AhkCommandLines().Where(kv => kv.Value.IndexOf(dir, StringComparison.OrdinalIgnoreCase) >= 0).Select(kv => kv.Key).ToList();
            Check(leftovers.Count == 0, "isolation: no selftest AutoHotkey process left behind", "leftover pids: " + string.Join(",", leftovers));

            Console.WriteLine(_fail == 0 ? "SELFTEST OK" : "SELFTEST FAILED (" + _fail + ")");
            return _fail == 0 ? 0 : 1;
        }
    }
}
