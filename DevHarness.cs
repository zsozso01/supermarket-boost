using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using HarmonyLib;
using Mirror;
using StarterAssets;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace SupermarketBoost
{
    /// <summary>
    /// Developer test harness. Off unless a file named "devharness" sits next to the plugin DLL
    /// (releases never ship it). Commands are read from BepInEx/sb_cmd.txt (one per line, the file is
    /// consumed), results go to the BepInEx log as "[TEST] PASS|FAIL|INFO ..." lines.
    /// </summary>
    internal static class DevHarness
    {
        public static bool Enabled { get; private set; }
        private static string _cmdFile;
        private static float _nextPoll;
        private static readonly Queue<string> Pending = new Queue<string>();
        private static bool _busy;
        private static Camera _thirdCam;
        public static bool ForceThirdPerson;

        public static void Init(string pluginDir)
        {
            Enabled = File.Exists(Path.Combine(pluginDir, "devharness"));
            if (!Enabled) return;
            Application.runInBackground = true;
            _cmdFile = Path.Combine(Paths.BepInExRootPath, "sb_cmd.txt");
            try { if (File.Exists(_cmdFile)) File.Delete(_cmdFile); } catch { }
            Log("INFO", $"harness on, commands from {_cmdFile}");
        }

        private static void Log(string kind, string msg) => Plugin.Log.LogInfo($"[TEST] {kind} {msg}");
        private static void Pass(string msg) => Log("PASS", msg);
        private static void Fail(string msg) => Log("FAIL", msg);
        private static void Info(string msg) => Log("INFO", msg);
        private static void Check(bool ok, string msg) => Log(ok ? "PASS" : "FAIL", msg);

        public static void Tick(MonoBehaviour host)
        {
            if (!Enabled) return;
            if (!Application.runInBackground) Application.runInBackground = true;
            if (Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + 0.25f;
                try
                {
                    if (File.Exists(_cmdFile))
                    {
                        var lines = File.ReadAllLines(_cmdFile);
                        File.Delete(_cmdFile);
                        foreach (var l in lines)
                            if (!string.IsNullOrWhiteSpace(l)) Pending.Enqueue(l.Trim());
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning($"[TEST] command file: {e.Message}"); }
            }
            if (!_busy && Pending.Count > 0) host.StartCoroutine(Run(Pending.Dequeue()));
        }

        private static IEnumerator Run(string line)
        {
            _busy = true;
            Info($"> {line}");
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            IEnumerator body = null;
            try { body = Dispatch(parts[0].ToLowerInvariant(), parts.Skip(1).ToArray()); }
            catch (Exception e) { Fail($"{line}: {e.GetType().Name}: {e.Message}"); }
            if (body != null)
            {
                while (true)
                {
                    object cur;
                    try
                    {
                        if (!body.MoveNext()) break;
                        cur = body.Current;
                    }
                    catch (Exception e) { Fail($"{line}: {e.GetType().Name}: {e.Message}\n{e.StackTrace}"); break; }
                    yield return cur;
                }
            }
            Info($"< {parts[0]} done");
            _busy = false;
        }

        private static IEnumerator Dispatch(string cmd, string[] a)
        {
            switch (cmd)
            {
                case "state": State(); return null;
                case "dumpui": DumpUi(a.Length > 0 ? a[0] : null); return null;
                case "click": return ClickUi(string.Join(" ", a));
                case "continue": Plugin.Instance.Toast(MainMenu.Continue()); return WaitFor("in store as host", () => NetworkServer.active && GameHelpers.LocalPlayer != null, 90f);
                case "openstore": return OpenStore();
                case "spawncustomer": return SpawnCustomer(a.Length > 0 && a[0] == "thief", a.Length > 1 ? int.Parse(a[1]) : (a.Length > 0 && a[0] != "thief" ? int.Parse(a[0]) : 1));
                case "npcs": Npcs(); return null;
                case "tpnpc": return TpNpc(uint.Parse(a[0]));
                case "shoot": return Shoot(a.Length > 0 ? uint.Parse(a[0]) : 0u, a.Length > 1 ? int.Parse(a[1]) : 1);
                case "shootwall": return ShootWall();
                case "broom": return Broom(uint.Parse(a[0]));
                case "speed": return Speed(float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture));
                case "cosmetics": return CosmeticsTest();
                case "give": return GivePistol();
                case "holster": return Holster();
                case "reload": return Reload();
                case "ragdollplayer": return RagdollPlayer();
                case "thirdperson": ThirdPerson(a.Length == 0 || a[0] != "off"); return null;
                case "look": Look(a); return null;
                case "camdump": CamDump(); return null;
                case "static": StaticGet(a[0]); return null;
                case "fsm": FsmDump(string.Join(" ", a)); return null;
                case "globals": Globals(); return null;
                case "tree": Tree(a[0], a.Length > 1 ? int.Parse(a[1]) : 2); return null;
                case "thirdangle": ThirdAngle = a[0]; return null;
                case "ik": Ik(a); return null;
                case "iroll": IRoll(a[0], P(a[1])); return null;
                case "drift": Drift(a[0]); return null;
                case "grip": Grip(a); return null;
                case "groll":
                    PistolModel.RollR = P(a[0]); PistolModel.RollL = P(a[1]);
                    Grip(new[] { "r", F(PistolModel.RightIkPos.x), F(PistolModel.RightIkPos.y), F(PistolModel.RightIkPos.z), F(PistolModel.RightKnuckles.x), F(PistolModel.RightKnuckles.y), F(PistolModel.RightKnuckles.z) });
                    Grip(new[] { "l", F(PistolModel.LeftIkPos.x), F(PistolModel.LeftIkPos.y), F(PistolModel.LeftIkPos.z), F(PistolModel.LeftKnuckles.x), F(PistolModel.LeftKnuckles.y), F(PistolModel.LeftKnuckles.z) });
                    return null;
                case "ikfit": return IkFit(a);
                case "curl": FingerCurl.Curl = P(a[0]); FingerCurl.ThumbCurl = P(a[1]); if (a.Length > 2) FingerCurl.Axis = P(a[2]); Info($"curl {FingerCurl.Curl} {FingerCurl.ThumbCurl} axis {FingerCurl.Axis}"); return null;
                case "hold": Hold(a); return null;
                case "campos": CamPos(); return null;
                case "guncam": GunCam(a.Length > 0 ? a[0] : "side", a.Length > 1 ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 0.32f); return null;
                case "objects": Objects(a[0], a.Length > 1 ? a[1] : null); return null;
                case "screenshot": return Screenshot(a.Length > 0 ? a[0] : $"sb_{DateTime.Now:HHmmss}.png");
                case "wait": return WaitSeconds(float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture));
                case "config": ConfigDump(); return null;
                case "backups": Backups(); return null;
                case "timeofday": GameData.Instance.NetworktimeOfDay = float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture); Info($"timeOfDay={GameData.Instance.timeOfDay}"); return null;
                case "quit": Info("quitting"); Application.Quit(); return null;
                default: Fail($"unknown command '{cmd}'"); return null;
            }
        }

        // ---------- generic ----------

        private static IEnumerator WaitSeconds(float s)
        {
            float end = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        private static IEnumerator WaitFor(string what, Func<bool> cond, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < end)
            {
                bool ok = false;
                try { ok = cond(); } catch { }
                if (ok) { Pass(what); yield break; }
                yield return null;
            }
            Fail($"{what} (timeout {timeout}s)");
        }

        private static void State()
        {
            var p = GameHelpers.LocalPlayer;
            var gd = GameData.Instance;
            var sb = new StringBuilder();
            sb.Append($"steam={SteamManager.Initialized} lobby={(SteamLobby.Instance ? "yes" : "null")} scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} ");
            sb.Append($"menu={MainMenu.InMenu} server={NetworkServer.active} client={NetworkClient.active} hasModHost={Net.ServerHasMod} ");
            sb.Append($"timeScale={Time.timeScale:0.##} speed={DaySpeed.Speed} fps={1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime):0} res={Screen.width}x{Screen.height} ");
            if (gd != null) sb.Append($"day={gd.gameDay} time={gd.timeOfDay:0.00} open={gd.isSupermarketOpen} maxCustomers={gd.maxCustomersNPCs} ");
            if (p != null)
                sb.Append($"player={p.netId} pos={p.transform.position} item={p.equippedItem} pistolIdx={PistolItem.Index} mag={Pistol.Mag} input={GameHelpers.InputAllowed} ");
            if (NPC_Manager.Instance != null)
            {
                var npcs = UnityEngine.Object.FindObjectsByType<NPC_Info>(FindObjectsSortMode.None);
                sb.Append($"npcs={npcs.Length} customers={npcs.Count(n => n.isCustomer)} thieves={npcs.Count(n => n.isAThief)} employees={npcs.Count(n => n.isEmployee)}");
            }
            Info("state " + sb);
        }

        private static string PathOf(Transform t)
        {
            var s = t.name;
            while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }

        private static string TextOf(Component c)
        {
            var tmp = c.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null) return tmp.text;
            var txt = c.GetComponentInChildren<Text>(true);
            return txt != null ? txt.text : "";
        }

        private static void DumpUi(string filter)
        {
            int n = 0;
            foreach (var b in UnityEngine.Object.FindObjectsByType<Selectable>(FindObjectsSortMode.None))
            {
                if (!b.gameObject.activeInHierarchy) continue;
                string path = PathOf(b.transform), text = TextOf(b).Replace("\n", " ");
                if (filter != null && path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                Info($"ui {b.GetType().Name} '{text}' {path} interactable={b.interactable}");
                n++;
            }
            Info($"dumpui {n} selectables");
        }

        private static IEnumerator ClickUi(string target)
        {
            foreach (var b in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
            {
                if (!b.gameObject.activeInHierarchy) continue;
                if (PathOf(b.transform).EndsWith(target, StringComparison.OrdinalIgnoreCase) ||
                    TextOf(b).Trim().Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    b.onClick.Invoke();
                    var pm = b.GetComponent<PlayMakerFSM>();
                    if (pm != null) pm.SendEvent("ButtonClick");
                    Pass($"clicked {PathOf(b.transform)}");
                    yield break;
                }
            }
            Fail($"no active button '{target}'");
        }

        private static IEnumerator Screenshot(string file)
        {
            yield return new WaitForEndOfFrame();
            string path = Path.IsPathRooted(file) ? file : Path.Combine(Paths.BepInExRootPath, "screens", file);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            ScreenCapture.CaptureScreenshot(path);
            yield return null; yield return null;
            Info($"screenshot {path}");
        }

        private static string FsmVal(object v)
        {
            if (v == null) return "null";
            if (v is HutongGames.PlayMaker.NamedVariable nv) return (nv.UseVariable ? "$" + nv.Name + "=" : "") + nv.ToString();
            if (v is HutongGames.PlayMaker.FsmEvent ev) return "ev:" + ev.Name;
            if (v is HutongGames.PlayMaker.FsmOwnerDefault od) return "owner:" + (od.OwnerOption == HutongGames.PlayMaker.OwnerDefaultOption.UseOwner ? "self" : od.GameObject?.Value?.name);
            if (v is Array arr) return "[" + string.Join(",", arr.Cast<object>().Select(FsmVal)) + "]";
            return v.ToString();
        }

        private static void FsmDump(string pathSuffix)
        {
            int n = 0;
            string only = null;
            int bar = pathSuffix.IndexOf('|');
            if (bar >= 0) { only = pathSuffix.Substring(bar + 1); pathSuffix = pathSuffix.Substring(0, bar); }
            foreach (var fsm in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
            {
                if (!fsm.gameObject.scene.IsValid() || !PathOf(fsm.transform).EndsWith(pathSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                if (only != null && fsm.FsmName != only) continue;
                Info($"FSM {PathOf(fsm.transform)} '{fsm.FsmName}' active={fsm.ActiveStateName}");
                foreach (var st in fsm.FsmStates)
                {
                    Info($"  state {st.Name} -> " + string.Join(", ", st.Transitions.Select(t => t.EventName + ":" + t.ToState)));
                    foreach (var act in st.Actions)
                    {
                        var fields = act.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                            .Where(f => f.DeclaringType != typeof(HutongGames.PlayMaker.FsmStateAction))
                            .Select(f => { try { return f.Name + "=" + FsmVal(f.GetValue(act)); } catch { return f.Name + "=?"; } });
                        Info($"    {act.GetType().Name} {string.Join(" ", fields)}");
                    }
                }
                if (++n >= 6) break;
            }
            Info($"fsm matched {n}");
        }

        private static void Tree(string path, int depth)
        {
            var root = GameObject.Find(path);
            if (root == null)
                foreach (var go in Resources.FindObjectsOfTypeAll<Transform>())
                    if (go.gameObject.scene.IsValid() && PathOf(go).EndsWith(path)) { root = go.gameObject; break; }
            if (root == null) { Fail($"tree: {path} not found"); return; }
            void Walk(Transform t, int d, string indent)
            {
                var rt = t as RectTransform;
                Info($"{indent}{t.name} active={t.gameObject.activeSelf}" + (rt ? $" pos={rt.anchoredPosition} size={rt.sizeDelta} anchor={rt.anchorMin}-{rt.anchorMax}" : "") +
                     " [" + string.Join(",", t.GetComponents<Component>().Skip(1).Select(c => c.GetType().Name)) + "]" +
                     (t.GetComponent<TMP_Text>() ? " '" + t.GetComponent<TMP_Text>().text.Replace("\n", " ") + "'" : ""));
                if (d <= 0) return;
                foreach (Transform c in t) Walk(c, d - 1, indent + "  ");
            }
            Walk(root.transform, depth, "");
        }

        private static void Globals()
        {
            foreach (var v in HutongGames.PlayMaker.FsmVariables.GlobalVariables.GetAllNamedVariables())
                Info($"global {v.VariableType} {v.Name} = {v}");
        }

        private static void StaticGet(string spec)
        {
            int dot = spec.LastIndexOf('.');
            var type = AccessTools.TypeByName(spec.Substring(0, dot));
            if (type == null) { Fail($"no type {spec}"); return; }
            var v = Traverse.Create(type).Property(spec.Substring(dot + 1)).PropertyExists()
                ? Traverse.Create(type).Property(spec.Substring(dot + 1)).GetValue()
                : Traverse.Create(type).Field(spec.Substring(dot + 1)).GetValue();
            Info($"static {spec} = {(v is UnityEngine.Object uo ? (uo ? uo.name + " (" + uo.GetType().Name + ")" : "null(destroyed)") : v ?? "null")}");
        }

        private static void Objects(string typeName, string member)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null) { Fail($"no type {typeName}"); return; }
            var all = Resources.FindObjectsOfTypeAll(type);
            int n = 0;
            foreach (var o in all)
            {
                var c = o as Component;
                if (c != null && !c.gameObject.scene.IsValid()) continue;
                string extra = member != null ? " " + member + "=" + Traverse.Create(o).Field(member).GetValue() : "";
                Info($"obj {typeName} {(c ? PathOf(c.transform) + " active=" + c.gameObject.activeInHierarchy + " scene=" + c.gameObject.scene.name : o.name)}{extra}");
                if (++n >= 40) break;
            }
            Info($"objects {typeName}: {n}");
        }

        private static void ConfigDump()
        {
            Info($"config CapMultiplier={Plugin.CapMultiplier.Value} SpawnDelay={Plugin.SpawnDelayMultiplier.Value} Thief={Plugin.ThiefChanceMultiplier.Value} " +
                 $"BreakdownInterval={Plugin.BreakdownIntervalMultiplier.Value} ShotsToKO={Plugin.ShotsToKnockOut.Value} ConfigVersion={Plugin.ConfigVersion.Value}");
        }

        private static void Backups()
        {
            var dir = Path.Combine(Path.GetDirectoryName(Application.persistentDataPath), "Supermarket Together Backups (SupermarketBoost)");
            if (!Directory.Exists(dir)) { Fail($"no backup folder {dir}"); return; }
            var all = Directory.GetDirectories(dir).OrderBy(d => d).ToArray();
            Info($"backups {all.Length}: " + string.Join(", ", all.Skip(Math.Max(0, all.Length - 5)).Select(Path.GetFileName)));
        }

        // ---------- store ----------

        private static IEnumerator OpenStore()
        {
            var gd = GameData.Instance;
            if (gd == null) { Fail("openstore: no GameData"); yield break; }
            if (gd.isSupermarketOpen) { Pass("store already open"); yield break; }
            gd.CmdOpenSupermarket();
            yield return WaitFor("store open", () => GameData.Instance.isSupermarketOpen, 10f);
            Info($"maxCustomersNPCs={gd.maxCustomersNPCs}");
        }

        private static IEnumerator SpawnCustomer(bool thief, int count)
        {
            var mgr = NPC_Manager.Instance;
            if (mgr == null || !NetworkServer.active) { Fail("spawncustomer: need host + NPC_Manager"); yield break; }
            var parent = mgr.customersnpcParentOBJ.transform;
            var spawn = AccessTools.Method(typeof(NPC_Manager), "SpawnCustomerNPC");
            for (int i = 0; i < count; i++)
            {
                int before = parent.childCount;
                mgr.StartCoroutine((IEnumerator)spawn.Invoke(mgr, null));
                float end = Time.realtimeSinceStartup + 30f;
                while (parent.childCount <= before && Time.realtimeSinceStartup < end) yield return null;
                if (parent.childCount <= before) { Fail("spawncustomer: nothing spawned in 30s"); yield break; }
                var npc = parent.GetChild(parent.childCount - 1).GetComponent<NPC_Info>();
                if (thief && npc != null) npc.isAThief = true;
                Pass($"spawned customer netId={npc?.netId} thief={npc?.isAThief} at {npc?.transform.position}");
            }
        }

        private static void Npcs()
        {
            var p = GameHelpers.LocalPlayer;
            foreach (var n in UnityEngine.Object.FindObjectsByType<NPC_Info>(FindObjectsSortMode.None))
            {
                var r = n.GetComponent<Ragdoll>();
                Info($"npc {n.netId} {n.name} customer={n.isCustomer} thief={n.isAThief} employee={n.isEmployee} state={n.state} carrying={n.productsIDCarrying.Count} " +
                     $"toBuy={n.productsIDToBuy.Count} down={r?.IsDown} ko={r?.KnockedOut} dist={(p ? Vector3.Distance(p.transform.position, n.transform.position) : -1):0.0} pos={n.transform.position}");
            }
        }

        private static NPC_Info FindNpc(uint id)
        {
            if (id == 0)
            {
                var p = GameHelpers.LocalPlayer;
                return UnityEngine.Object.FindObjectsByType<NPC_Info>(FindObjectsSortMode.None)
                    .Where(n => !n.isEmployee && (n.GetComponent<Ragdoll>() == null || !n.GetComponent<Ragdoll>().KnockedOut))
                    .OrderBy(n => p ? Vector3.Distance(p.transform.position, n.transform.position) : 0f).FirstOrDefault();
            }
            return NetworkClient.spawned.TryGetValue(id, out var ni) && ni ? ni.GetComponent<NPC_Info>() : null;
        }

        private static void Teleport(Vector3 pos, Vector3 lookAt)
        {
            var fpc = FirstPersonController.Instance;
            var cc = fpc.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            fpc.transform.position = pos;
            var flat = lookAt - pos; flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f) SetYawPitch(Quaternion.LookRotation(flat).eulerAngles.y, 10f);
            if (cc) cc.enabled = true;
        }

        private static IEnumerator TpNpc(uint id)
        {
            var npc = FindNpc(id);
            if (npc == null) { Fail($"tpnpc: no npc {id}"); yield break; }
            var dir = npc.transform.forward; dir.y = 0;
            Teleport(npc.transform.position + dir.normalized * 2.5f, npc.transform.position);
            yield return null;
            Pass($"teleported to npc {npc.netId}");
        }

        private static void AimCamera(Vector3 target)
        {
            var cam = Camera.main;
            var fpc = FirstPersonController.Instance;
            if (cam == null || fpc == null) return;
            var d = target - cam.transform.position;
            var flat = d; flat.y = 0;
            SetYawPitch(Quaternion.LookRotation(flat).eulerAngles.y, null);
            float pitch = -Mathf.Atan2(d.y, flat.magnitude) * Mathf.Rad2Deg;
            SetPitch(pitch);
        }

        // The game's CustomCameraController owns yaw (x) and pitch (y) and re-applies them every frame.
        private static void SetYawPitch(float? yaw, float? pitch)
        {
            var ccc = UnityEngine.Object.FindAnyObjectByType<CustomCameraController>();
            if (ccc == null) return;
            var t = Traverse.Create(ccc);
            if (yaw.HasValue) t.Field("x").SetValue(yaw.Value);
            if (pitch.HasValue) t.Field("y").SetValue(pitch.Value);
        }

        private static void SetPitch(float pitch) => SetYawPitch(null, pitch);

        private static void CamDump()
        {
            var cam = Camera.main;
            if (cam == null) { Fail("no main camera"); return; }
            Info($"camera {PathOf(cam.transform)} local={cam.transform.localEulerAngles} comps=" +
                 string.Join(",", cam.GetComponents<Component>().Select(c => c.GetType().Name)) +
                 " parentComps=" + (cam.transform.parent ? string.Join(",", cam.transform.parent.GetComponents<Component>().Select(c => c.GetType().Name)) : "-"));
        }

        private static void Look(string[] a)
        {
            if (a.Length >= 1 && uint.TryParse(a[0], out var id) && a.Length == 1)
            {
                var npc = FindNpc(id);
                if (npc) AimCamera(npc.transform.position + Vector3.up * 1.2f);
            }
            else if (a.Length >= 2)
            {
                SetYawPitch(float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture));
            }
            Info("look done");
        }

        // ---------- pistol ----------

        private static IEnumerator GivePistol()
        {
            var p = GameHelpers.LocalPlayer;
            if (p == null) { Fail("give: no local player"); yield break; }
            if (PistolItem.Holds(p)) { Pass("pistol already held"); yield break; }
            if (p.equippedItem != 0) { p.CmdChangeEquippedItem(0); yield return WaitSeconds(0.5f); }
            int notes = Notifications;
            p.CmdChangeEquippedItem(PistolItem.Index);
            yield return WaitFor($"equippedItem == {PistolItem.Index} (synced)", () => p.equippedItem == PistolItem.Index, 5f);
            yield return WaitFor("pistol model spawned in hands", () => p.instantiatedOBJ != null && p.instantiatedOBJ.GetComponent<PistolModel>() != null, 5f);
            yield return WaitSeconds(1f);
            var sync = p.GetComponent<PlayerSyncCharacter>();
            var rc = Traverse.Create(sync).Field("rightHandConstraint").GetValue<UnityEngine.Animations.Rigging.TwoBoneIKConstraint>();
            var rt = Traverse.Create(sync).Field("rightHandOBJ").GetValue<Transform>();
            var ri = p.instantiatedOBJ.transform.Find("Kick/RightHandIK");
            Check(rc != null && rc.weight > 0.99f && rt != null && ri != null && Vector3.Distance(rt.position, ri.position) < 0.01f,
                $"right hand IK on grip (weight={rc?.weight}, dist={(rt && ri ? Vector3.Distance(rt.position, ri.position) : -1f):0.000})");
            Info($"notifications during equip: {Notifications - notes}");
        }

        private static IEnumerator Holster()
        {
            var p = GameHelpers.LocalPlayer;
            p.CmdChangeEquippedItem(0);
            yield return WaitFor("holstered", () => p.equippedItem == 0 && p.instantiatedOBJ == null, 5f);
        }

        private static IEnumerator Reload()
        {
            if (!Pistol.Drawn) { Fail("reload: no pistol"); yield break; }
            Pistol.Mag = 3;
            Pistol.DevReload();
            yield return WaitFor("reloading started", () => Pistol.Reloading, 1f);
            yield return WaitSeconds(0.7f);
            yield return Screenshot("reload_mid.png");
            yield return WaitFor($"mag refilled to {Plugin.MagSize.Value}", () => !Pistol.Reloading && Pistol.Mag == Plugin.MagSize.Value, 3f);
        }

        public static int Notifications, Broom14;
        public static Vector2 Move;

        private static IEnumerator Shoot(uint id, int n)
        {
            var p = GameHelpers.LocalPlayer;
            if (!Pistol.Drawn) { yield return GivePistol(); }
            var npc = FindNpc(id);
            if (npc == null) { Fail($"shoot: no npc {id}"); yield break; }
            id = npc.netId;
            var cam = Camera.main;
            if (Vector3.Distance(cam.transform.position, npc.transform.position) > 12f ||
                Physics.Linecast(cam.transform.position, npc.transform.position + Vector3.up * 1.2f, out var block, ~0, QueryTriggerInteraction.Ignore) &&
                block.collider.GetComponentInParent<NPC_Info>() != npc)
            {
                var dir = npc.transform.forward; dir.y = 0;
                Teleport(npc.transform.position + dir.normalized * 3f, npc.transform.position);
                yield return null; yield return null;
            }
            int broomBefore = Broom14;
            bool wasThief = npc.isAThief;
            int carrying = npc.productsIDCarrying.Count;
            for (int i = 0; i < n; i++)
            {
                if (npc == null) break;
                var rag = npc.GetComponent<Ragdoll>();
                Vector3 aim = rag != null && rag.IsDown && rag.Pelvis ? rag.Pelvis.position : npc.transform.position + Vector3.up * 1.1f;
                AimCamera(aim);
                yield return null;
                int magBefore = Pistol.Mag;
                var shot = Pistol.DevFire(aim);
                Check(shot.Kind == HitKind.Npc && shot.Target == id, $"shot {i + 1}/{n} hit npc {id} (kind={shot.Kind} target={shot.Target})");
                Check(Pistol.Mag == magBefore - 1, $"ammo {magBefore} -> {Pistol.Mag}");
                yield return WaitSeconds(0.35f);
                rag = npc ? npc.GetComponent<Ragdoll>() : null;
                Check(rag != null && rag.IsDown, $"npc {id} ragdolled after shot {i + 1}");
                if (i == 0) yield return Screenshot($"shot_{id}_ragdoll.png");
            }
            Check(Broom14 == broomBefore, "no 'Need a broom' message while holding the pistol");
            yield return WaitSeconds(1.5f);
            var r2 = npc ? npc.GetComponent<Ragdoll>() : null;
            if (r2 != null) Info($"after {n} shots: down={r2.IsDown} ko={r2.KnockedOut} thief={wasThief} carryingBefore={carrying} carryingNow={npc.productsIDCarrying.Count}");
            yield return Screenshot($"shot_{id}_after.png");
        }

        private static IEnumerator ShootWall()
        {
            if (!Pistol.Drawn) yield return GivePistol();
            var c = Camera.main.transform;
            var shot = Pistol.DevFire(c.position + c.forward * 10f);
            Check(shot.Kind == HitKind.World, $"shot hit world at {shot.Point}");
            yield return WaitSeconds(0.2f);
        }

        private static IEnumerator Broom(uint id)
        {
            var npc = FindNpc(id);
            if (npc == null) { Fail($"broom: no npc {id}"); yield break; }
            id = npc.netId;
            npc.CmdAnimationPlay(0); // the game's own broom-hit command
            yield return WaitSeconds(0.4f);
            var rag = npc.GetComponent<Ragdoll>();
            Check(rag != null && rag.IsDown, $"broom ragdolled npc {id}");
            yield return Screenshot($"broom_{id}.png");
        }

        // ---------- speed ----------

        private static IEnumerator Speed(float s)
        {
            if (!GameData.Instance.isSupermarketOpen) { Fail("speed: store closed"); yield break; }
            DaySpeed.SetSpeed(s);
            yield return WaitSeconds(0.5f);
            Check(Mathf.Approximately(Time.timeScale, Mathf.Clamp(s, 1f, 8f)), $"timeScale={Time.timeScale} for speed {s}");
            var p = GameHelpers.LocalPlayer;
            var sync = p ? p.GetComponent<PlayerSyncCharacter>() : null;
            var anim = sync && sync.characterOBJ ? sync.characterOBJ.GetComponent<Animator>() : null;
            Check(anim == null || Mathf.Abs(anim.speed * Time.timeScale - 1f) < 0.01f, $"player animator speed {anim?.speed} (effective {(anim ? anim.speed * Time.timeScale : 0f):0.00})");
            float pd = PlayerRealtimePatch.PlayerDeltaTime();
            Check(Mathf.Abs(pd - Time.unscaledDeltaTime) < 0.005f || s <= 1f, $"player delta {pd:0.0000} vs real {Time.unscaledDeltaTime:0.0000}");

            // Walk forward for 1 s of real time and measure.
            var fpc = FirstPersonController.Instance;
            var start = fpc.transform.position;
            {
                float end = Time.realtimeSinceStartup + 1f;
                while (Time.realtimeSinceStartup < end) { Move = new Vector2(0, 1); yield return null; }
                Move = Vector2.zero;
                float dist = Vector3.Distance(new Vector3(start.x, 0, start.z), new Vector3(fpc.transform.position.x, 0, fpc.transform.position.z));
                Info($"walked {dist:0.00} m in 1 s real time at speed x{s}");
                Check(dist < 8f, $"player walk distance sane at x{s} ({dist:0.00} m/s)");
            }
        }

        // ---------- cosmetics ----------

        private static IEnumerator CosmeticsTest()
        {
            var fpc = FirstPersonController.Instance;
            var start = fpc.transform.position;
            Plugin.Instance.Toast(Cosmetics.Toggle());
            yield return WaitSeconds(1f);
            var inRoom = fpc.transform.position;
            Check(Vector3.Distance(start, inRoom) > 5f, $"teleported into cosmetics room {start} -> {inRoom}");
            bool elephant = false;
            foreach (var an in UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
                if (an.name.IndexOf("Herbert", StringComparison.OrdinalIgnoreCase) >= 0 || an.name.IndexOf("elephant", StringComparison.OrdinalIgnoreCase) >= 0)
                    foreach (var rr in an.GetComponentsInChildren<Renderer>()) if (rr.enabled && rr.gameObject.activeInHierarchy) { elephant = true; Info($"visible elephant {PathOf(an.transform)} at {an.transform.position}"); break; }
            Check(!elephant, "elephant hidden");
            yield return Screenshot("cosmetics_room.png");

            // Walk around in the room: forward 1.5 s, check we moved (not stuck on an invisible barrier).
            {
                foreach (var dir in new[] { new Vector2(0, -1), new Vector2(1, 0), new Vector2(-1, 0) })
                {
                    var s = fpc.transform.position;
                    float end = Time.realtimeSinceStartup + 0.8f;
                    while (Time.realtimeSinceStartup < end) { Move = dir; yield return null; }
                    Move = Vector2.zero;
                    Info($"walk {dir}: moved {Vector3.Distance(s, fpc.transform.position):0.00} m");
                }
            }
            Plugin.Instance.Toast(Cosmetics.Toggle());
            yield return WaitSeconds(1f);
            Check(Vector3.Distance(start, fpc.transform.position) < 1.5f, $"teleported back ({Vector3.Distance(start, fpc.transform.position):0.00} m from start)");
        }

        // ---------- player ragdoll ----------

        private static IEnumerator RagdollPlayer()
        {
            var p = GameHelpers.LocalPlayer;
            var rag = Ragdoll.For(p.gameObject);
            var start = p.transform.position;
            rag.Hit(p.transform.position + Vector3.up * 1.2f, p.transform.forward, 1f, false);
            yield return WaitSeconds(0.5f);
            Check(rag.IsDown, "local player ragdolled");
            Check(!GameHelpers.InputAllowed || Camera.main != null, "camera alive during ragdoll");
            yield return Screenshot("player_ragdoll.png");
            yield return WaitFor("player got up", () => !rag.IsDown, 12f);
            yield return WaitSeconds(1.2f);
            Check(GameHelpers.InputAllowed, "input restored after getting up");
            var sync = p.GetComponent<PlayerSyncCharacter>();
            var head = sync.characterOBJ.transform.Find("Bip01/Bip01 Pelvis/Bip01 Spine/Bip01 Spine1/Bip01 Spine2/Bip01 Neck/Bip01 Head");
            float headH = head ? head.position.y - p.transform.position.y : -1f;
            Check(headH > 1.3f && headH < 2.0f, $"model upright after getting up (head {headH:0.00} m above feet)");
            var rb = sync.characterOBJ.GetComponent<UnityEngine.Animations.Rigging.RigBuilder>();
            Check(rb == null || rb.enabled, $"RigBuilder enabled={rb?.enabled}");
            Info($"player moved {Vector3.Distance(start, p.transform.position):0.00} m");
        }

        // ---------- third person view (for checking the hold pose) ----------

        private static void ThirdPerson(bool on)
        {
            var p = GameHelpers.LocalPlayer;
            if (p == null) return;
            ForceThirdPerson = on;
            foreach (var r in p.GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetComponentInParent<PistolModel>() != null) continue;
                if (on) { if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) { _hidden.Add(r); r.shadowCastingMode = ShadowCastingMode.On; } }
            }
            if (!on) { foreach (var r in _hidden) if (r) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly; _hidden.Clear(); }
            Info($"thirdperson: {_hidden.Count} hidden renderers shown");
            if (on)
            {
                if (_thirdCam == null)
                {
                    var go = new GameObject("SB_ThirdPersonCam");
                    _thirdCam = go.AddComponent<Camera>();
                    _thirdCam.depth = 50;
                    _thirdCam.nearClipPlane = 0.05f;
                    go.AddComponent<ThirdPersonFollow>().Target = p.transform;
                }
                _thirdCam.enabled = true;
            }
            else if (_thirdCam) _thirdCam.enabled = false;
            Info($"thirdperson {on}");
        }

        public static string ThirdAngle = "front";

        private static float P(string v) => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>ik r|l x y z rx ry rz : move a grip point on the held pistol (and keep it for new ones).</summary>
        private static void Ik(string[] a)
        {
            var pos = new Vector3(P(a[1]), P(a[2]), P(a[3]));
            var rot = a.Length >= 7 ? Quaternion.Euler(P(a[4]), P(a[5]), P(a[6])) : Quaternion.identity;
            if (a[0] == "r") PistolModel.RightIkPos = pos; else PistolModel.LeftIkPos = pos;
            var p = GameHelpers.LocalPlayer;
            var t = p && p.instantiatedOBJ ? p.instantiatedOBJ.transform.Find(a[0] == "r" ? "Kick/RightHandIK" : "Kick/LeftHandIK") : null;
            if (t) { t.localPosition = pos; t.localRotation = rot; }
            Info($"ik {a[0]} {pos} {rot.eulerAngles}");
        }

        private static Vector3 V(string[] a, int i) => new Vector3(P(a[i]), P(a[i + 1]), P(a[i + 2]));

        /// <summary>
        /// ikfit [rx ry rz lx ly lz] : read the hand bones' axes in the idle pose (arms hanging: fingers down,
        /// palms facing the legs), then aim the grip points so the palms face the grip and the knuckles
        /// point forward. Optional wrist positions (Kick-local).
        /// </summary>
        private static IEnumerator IkFit(string[] a)
        {
            var p = GameHelpers.LocalPlayer;
            var sync = p.GetComponent<PlayerSyncCharacter>();
            var ch = sync.characterOBJ.transform;
            var rh = ch.Find("Bip01/Bip01 Pelvis/Bip01 Spine/Bip01 Spine1/Bip01 Spine2/Bip01 Neck/Bip01 R Clavicle/Bip01 R UpperArm/Bip01 R Forearm/Bip01 R Hand");
            var lh = ch.Find("Bip01/Bip01 Pelvis/Bip01 Spine/Bip01 Spine1/Bip01 Spine2/Bip01 Neck/Bip01 L Clavicle/Bip01 L UpperArm/Bip01 L Forearm/Bip01 L Hand");
            if (rh == null || lh == null) { Fail("ikfit: hand bones not found"); yield break; }
            if (PistolItem.Holds(p)) { p.CmdChangeEquippedItem(0); yield return WaitSeconds(1.2f); }
            var body = Quaternion.Inverse(ch.rotation);
            Quaternion qr = body * rh.rotation, ql = body * lh.rotation;
            Vector3 rFing = Quaternion.Inverse(qr) * Vector3.down, rPalm = Quaternion.Inverse(qr) * Vector3.left;
            Vector3 lFing = Quaternion.Inverse(ql) * Vector3.down, lPalm = Quaternion.Inverse(ql) * Vector3.right;
            Info($"ikfit bone axes: R fingers={rFing:F2} palm={rPalm:F2}  L fingers={lFing:F2} palm={lPalm:F2}");
            // Snap to the nearest bone axis (rigs are axis aligned).
            rFing = Snap(rFing); rPalm = Snap(rPalm); lFing = Snap(lFing); lPalm = Snap(lPalm);

            var rDF = new Vector3(0f, -0.35f, 1f).normalized; var rDP = Vector3.left;          // palm onto the grip's right side
            var lDF = new Vector3(0.25f, -0.45f, 1f).normalized; var lDP = new Vector3(0.9f, 0.35f, 0f).normalized; // support hand
            if (a.Length >= 8)
            {
                rDP = Quaternion.AngleAxis(P(a[6]), rDF) * rDP;
                lDP = Quaternion.AngleAxis(P(a[7]), lDF) * lDP;
            }
            var rRot = Quaternion.LookRotation(rDF, rDP) * Quaternion.Inverse(Quaternion.LookRotation(rFing, rPalm));
            var lRot = Quaternion.LookRotation(lDF, lDP) * Quaternion.Inverse(Quaternion.LookRotation(lFing, lPalm));
            Info($"ikfit computed R rot={rRot.eulerAngles:F1} L rot={lRot.eulerAngles:F1} (apply with ik)");
            if (a.Length >= 6) { PistolModel.RightIkPos = V(a, 0); PistolModel.LeftIkPos = V(a, 3); }
            Info($"ikfit R pos={PistolModel.RightIkPos:F3} rot={PistolModel.RightIkRot:F1}  L pos={PistolModel.LeftIkPos:F3} rot={PistolModel.LeftIkRot:F1}");
            p.CmdChangeEquippedItem(PistolItem.Index);
            yield return WaitSeconds(1f);
        }

        private static Vector3 Snap(Vector3 v)
        {
            float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
            if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(v.x), 0, 0);
            if (ay >= az) return new Vector3(0, Mathf.Sign(v.y), 0);
            return new Vector3(0, 0, Mathf.Sign(v.z));
        }

        /// <summary>iroll r|l degrees : roll a grip point about the hand's knuckle direction (positive = clockwise seen from the wrist).</summary>
        private static void IRoll(string hand, float deg)
        {
            bool r = hand == "r";
            var rot = Quaternion.Euler(r ? PistolModel.RightIkRot : PistolModel.LeftIkRot);
            var axis = r ? new Vector3(0f, -0.35f, 1f).normalized : new Vector3(0.25f, -0.45f, 1f).normalized;
            rot = Quaternion.AngleAxis(deg, axis) * rot;
            var pos = r ? PistolModel.RightIkPos : PistolModel.LeftIkPos;
            var e = rot.eulerAngles;
            Ik(new[] { hand, F(pos.x), F(pos.y), F(pos.z), F(e.x), F(e.y), F(e.z) });
        }

        /// <summary>drift npcId|player : after getting up, the body must stand on its network root.</summary>
        private static void Drift(string who)
        {
            GameObject go = null;
            if (who == "player") go = GameHelpers.LocalPlayer?.gameObject;
            else { var n = FindNpc(uint.Parse(who)); go = n ? n.gameObject : null; }
            if (go == null) { Fail($"drift: {who} not found"); return; }
            var rag = go.GetComponent<Ragdoll>();
            var pelvis = rag != null ? rag.Pelvis : null;
            if (pelvis == null) { Fail($"drift: {who} has no ragdoll yet"); return; }
            var d = pelvis.position - go.transform.position; d.y = 0f;
            float h = pelvis.position.y - go.transform.position.y;
            Check(!rag.IsDown && d.magnitude < 0.35f && h > 0.6f && h < 1.3f,
                $"{who} body on its root after getting up (horizontal {d.magnitude:0.00} m, pelvis height {h:0.00} m, down={rag.IsDown})");
        }

        /// <summary>grip r|l px py pz kx ky kz : wrist position and knuckle direction of one hand.</summary>
        private static void Grip(string[] a)
        {
            bool r = a[0] == "r";
            var pos = V(a, 1); var k = V(a, 4);
            if (r) { PistolModel.RightIkPos = pos; PistolModel.RightKnuckles = k; } else { PistolModel.LeftIkPos = pos; PistolModel.LeftKnuckles = k; }
            var p = GameHelpers.LocalPlayer;
            var t = p && p.instantiatedOBJ ? p.instantiatedOBJ.transform.Find(r ? "Kick/RightHandIK" : "Kick/LeftHandIK") : null;
            if (t) { t.localPosition = pos; t.localRotation = PistolModel.GripRot(r, k); }
            Info($"grip {a[0]} pos={pos:F3} knuckles={k:F2}");
        }

        private static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>hold up right forward : third-person gun offset from the chest.</summary>
        private static void Hold(string[] a)
        {
            PistolHolder.HoldOffset = new Vector3(P(a[1]), P(a[0]), P(a[2]));
            Info($"hold {PistolHolder.HoldOffset}");
        }

        private static void CamPos()
        {
            var p = GameHelpers.LocalPlayer;
            var sync = p ? p.GetComponent<PlayerSyncCharacter>() : null;
            Info($"campos main={Camera.main?.transform.position} third={(_thirdCam ? _thirdCam.transform.position.ToString() + " en=" + _thirdCam.enabled : "none")} " +
                 $"player={p?.transform.position} fwd={p?.transform.forward} char={(sync && sync.characterOBJ ? sync.characterOBJ.transform.forward.ToString() : "-")} " +
                 $"gun={(p && p.instantiatedOBJ ? p.instantiatedOBJ.transform.position.ToString() : "-")} cams={string.Join(",", Camera.allCameras.Select(c => c.name + ":" + c.depth))}");
        }
        private static readonly List<Renderer> _hidden = new List<Renderer>();
        private static Camera _gunCam;

        private static void GunCam(string angle, float dist)
        {
            if (angle == "off") { if (_gunCam) _gunCam.enabled = false; Info("guncam off"); return; }
            var p = GameHelpers.LocalPlayer;
            var gun = p && p.instantiatedOBJ ? p.instantiatedOBJ.transform : null;
            if (gun == null) { Fail("guncam: no gun held"); return; }
            if (_gunCam == null)
            {
                _gunCam = new GameObject("SB_GunCam").AddComponent<Camera>();
                _gunCam.depth = 60; _gunCam.nearClipPlane = 0.01f; _gunCam.fieldOfView = 40f;
                _gunCam.gameObject.AddComponent<GunCamFollow>();
            }
            _gunCam.enabled = true;
            var f = _gunCam.GetComponent<GunCamFollow>();
            f.Target = gun; f.Angle = angle; f.Dist = dist;
            Info($"guncam {angle} {dist}");
        }

        private class GunCamFollow : MonoBehaviour
        {
            public Transform Target; public string Angle; public float Dist;
            private void LateUpdate()
            {
                if (!Target) return;
                Vector3 c = Target.TransformPoint(new Vector3(0f, 0f, 0.0f));
                Vector3 off = Angle == "left" ? -Target.right : Angle == "top" ? Target.up + Target.right * 0.3f :
                              Angle == "front" ? Target.forward + Target.right * 0.4f + Target.up * 0.2f :
                              Angle == "back" ? -Target.forward + Target.up * 0.15f + Target.right * 0.1f :
                              Angle == "rq" ? (Target.right - Target.forward * 0.8f + Target.up * 0.35f) : Target.right;
                transform.position = c + off.normalized * Dist;
                transform.LookAt(c, Target.up);
            }
        }

        private class ThirdPersonFollow : MonoBehaviour
        {
            public Transform Target;
            private void LateUpdate()
            {
                if (!Target) return;
                Vector3 off = ThirdAngle == "side" ? Target.right * 1.4f + Target.forward * 0.4f
                            : ThirdAngle == "back" ? -Target.forward * 1.6f + Target.right * 0.5f
                            : Target.forward * 1.5f + Target.right * 0.5f;
                transform.position = Target.position + Vector3.up * 1.45f + off;
                transform.LookAt(Target.position + Vector3.up * 1.25f);
            }
        }

        // ---------- hooks ----------

        [HarmonyPatch(typeof(GameCanvas), nameof(GameCanvas.CreateCanvasNotification))]
        private static class CountNotes
        {
            private static void Postfix(string hash, bool __runOriginal)
            {
                if (!Enabled || !__runOriginal) return;
                Notifications++;
                if (hash == "message14" && PistolItem.Holds(GameHelpers.LocalPlayer)) Broom14++;
            }
        }

        [HarmonyPatch(typeof(Rewired.Player), nameof(Rewired.Player.GetAxisRaw), typeof(string))]
        private static class InjectMove
        {
            private static void Postfix(string actionName, ref float __result)
            {
                if (!Enabled || Move == Vector2.zero) return;
                if (actionName == "MoveH") __result = Move.x;
                else if (actionName == "MoveV") __result = Move.y;
            }
        }
    }
}
